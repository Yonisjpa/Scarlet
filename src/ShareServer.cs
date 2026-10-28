using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using QRCoder;

namespace XboxScarlet;

/// <summary>
/// Pasar capturas al celular, como en la Switch 2: se abre un servidor chiquito en tu red Wi-Fi y el código QR lleva
/// a una página con las fotos y videos elegidos. Sin cables, sin nube y sin apps. Se apaga solo a los 10 minutos.
/// Solo sirve los archivos que elegiste, con una dirección secreta que cambia cada vez.
/// </summary>
sealed class ShareServer
{
    const int FirstPort = 8737;
    static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    TcpListener listener;
    CancellationTokenSource cts;
    string token;
    List<string> files = new();
    DateTime expires;
    int port;

    public bool Active => listener != null && DateTime.UtcNow < expires;

    /// <summary>Empieza a compartir esos archivos. Devuelve la dirección para el QR (o null si no hay red).</summary>
    public object Start(IEnumerable<string> paths)
    {
        Stop();
        files = paths.Where(File.Exists).Distinct().ToList();
        if (files.Count == 0) return new { error = "No hay archivos para compartir." };
        string ip = LocalIp();
        if (ip == null) return new { error = "La consola no está conectada a una red Wi-Fi." };

        for (port = FirstPort; port < FirstPort + 10; port++)
        {
            try { listener = new TcpListener(IPAddress.Any, port); listener.Start(); break; }
            catch (SocketException) { listener = null; }
        }
        if (listener == null) return new { error = "No se pudo abrir la conexión." };

        token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        expires = DateTime.UtcNow + Lifetime;
        cts = new CancellationTokenSource();
        _ = AcceptLoop(listener, cts.Token);
        string mine = token;
        _ = Task.Delay(Lifetime).ContinueWith(_ => { if (token == mine) Stop(); });   // solo este enlace (si compartiste otro después, ese sigue)

        string url = $"http://{ip}:{port}/s/{token}";
        Log.Write($"compartir: {files.Count} archivo(s) en {url}");
        return new { url, qr = Qr(url), count = files.Count, minutes = (int)Lifetime.TotalMinutes, ssid = WifiName() };
    }

    public void Stop()
    {
        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        if (listener != null) Log.Write("compartir: terminado");
        listener = null; token = null;
    }

    static string Qr(string text)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(data).GetGraphic(8, "#000000", "#ffffff", true);
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
    }

    /// <summary>IPv4 de la red que tiene salida (Wi-Fi o cable), no la de adaptadores virtuales.</summary>
    static string LocalIp()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet)
                .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1))
            {
                var props = ni.GetIPProperties();
                if (!props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))) continue;
                var a = props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
                if (a != null) return a.Address.ToString();
            }
        }
        catch (Exception ex) { Log.Error("compartir: red", ex); }
        return null;
    }

    /// <summary>Nombre de la red Wi-Fi a la que está conectada (null si no hay).</summary>
    internal static string WifiName()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show interfaces") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            var m = Regex.Match(o, @"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }
        catch { return null; }
    }

    async Task AcceptLoop(TcpListener l, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await l.AcceptTcpClientAsync(ct); }
            catch { return; }
            _ = Task.Run(() => Serve(c), ct);
        }
    }

    async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 10000;
                using var s = client.GetStream();
                var reader = new StreamReader(s, Encoding.ASCII, false, 4096, leaveOpen: true);
                string first = await reader.ReadLineAsync();
                if (first == null) return;
                string range = null, line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                    if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) range = line[6..].Trim();
                var parts = first.Split(' ');
                if (parts.Length < 2 || (parts[0] != "GET" && parts[0] != "HEAD")) { await Text(s, 405, "Método no permitido"); return; }
                bool head = parts[0] == "HEAD";
                string path = Uri.UnescapeDataString(parts[1].Split('?')[0]);
                string tok = token;
                if (tok == null || !Active) { await Text(s, 410, "Este enlace ya caducó. Vuelve a generar el código en la consola."); return; }

                if (path == $"/s/{tok}" || path == $"/s/{tok}/") { await Html(s, Page(tok)); return; }
                var m = Regex.Match(path, $@"^/s/{tok}/f/(\d+)(?:/.*)?$");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int i) && i >= 0 && i < files.Count)
                {
                    await SendFile(s, files[i], range, head, path.EndsWith("/dl"));
                    return;
                }
                await Text(s, 404, "No encontrado");
            }
            catch (IOException) { }   // el celular cerró la conexión (normal al adelantar un video)
            catch (Exception ex) { Log.Error("compartir", ex); }
        }
    }

    string Page(string tok)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=es><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'><title>Scarlet · Álbum</title>")
          .Append("<style>body{margin:0;font-family:-apple-system,system-ui,sans-serif;background:#111;color:#eee}h1{font-size:20px;margin:18px 16px 4px}p{margin:0 16px 14px;color:#aaa;font-size:14px}")
          .Append(".it{margin:0 12px 22px;background:#1c1c1e;border-radius:14px;overflow:hidden}.it img,.it video{width:100%;display:block;background:#000}")
          .Append(".bar{display:flex;justify-content:space-between;align-items:center;padding:10px 14px;font-size:13px;color:#bbb}.bar a{color:#111;background:#22d6e3;text-decoration:none;font-weight:600;padding:8px 14px;border-radius:99px}</style>")
          .Append($"<h1>Álbum de Scarlet</h1><p>{files.Count} archivo(s). En fotos, mantén presionada la imagen y elige «Guardar en Fotos». El enlace caduca en 10 minutos.</p>");
        for (int i = 0; i < files.Count; i++)
        {
            string name = WebUtility.HtmlEncode(Path.GetFileName(files[i]));
            string src = $"/s/{tok}/f/{i}/{Uri.EscapeDataString(Path.GetFileName(files[i]))}";
            bool video = IsVideo(files[i]);
            sb.Append("<div class=it>")
              .Append(video ? $"<video src='{src}' controls playsinline preload=metadata></video>" : $"<img src='{src}' alt=''>")
              .Append($"<div class=bar><span>{name}</span><a href='/s/{tok}/f/{i}/dl' download='{name}'>Descargar</a></div></div>");
        }
        return sb.ToString();
    }

    static bool IsVideo(string f) => Path.GetExtension(f).ToLowerInvariant() is ".mp4" or ".webm" or ".mov";

    static string Mime(string f) => Path.GetExtension(f).ToLowerInvariant() switch
    {
        ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".mp4" => "video/mp4", ".webm" => "video/webm", _ => "application/octet-stream"
    };

    static async Task SendFile(NetworkStream s, string file, string range, bool head, bool download)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long len = fs.Length, start = 0, end = len - 1;
        bool partial = false;
        var m = range == null ? null : Regex.Match(range, @"bytes=(\d*)-(\d*)");
        if (m != null && m.Success && len > 0)
        {
            if (m.Groups[1].Value.Length == 0) start = Math.Max(0, len - long.Parse(m.Groups[2].Value));
            else { start = long.Parse(m.Groups[1].Value); if (m.Groups[2].Value.Length > 0) end = Math.Min(long.Parse(m.Groups[2].Value), len - 1); }
            if (start >= len) { await Text(s, 416, ""); return; }
            partial = true;
        }
        long count = end - start + 1;
        var h = new StringBuilder();
        h.Append(partial ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n")
         .Append($"Content-Type: {Mime(file)}\r\nContent-Length: {count}\r\nAccept-Ranges: bytes\r\nConnection: close\r\nCache-Control: no-store\r\n");
        if (partial) h.Append($"Content-Range: bytes {start}-{end}/{len}\r\n");
        if (download) h.Append($"Content-Disposition: attachment; filename*=UTF-8''{Uri.EscapeDataString(Path.GetFileName(file))}\r\n");
        h.Append("\r\n");
        byte[] hb = Encoding.UTF8.GetBytes(h.ToString());
        await s.WriteAsync(hb);
        if (head) return;
        fs.Seek(start, SeekOrigin.Begin);
        var buf = new byte[81920];
        while (count > 0)
        {
            int n = await fs.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, count)));
            if (n <= 0) break;
            await s.WriteAsync(buf.AsMemory(0, n));
            count -= n;
        }
    }

    static async Task Html(NetworkStream s, string html)
    {
        byte[] body = Encoding.UTF8.GetBytes(html);
        byte[] hb = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
        await s.WriteAsync(hb); await s.WriteAsync(body);
    }

    static async Task Text(NetworkStream s, int code, string msg)
    {
        byte[] body = Encoding.UTF8.GetBytes(msg);
        byte[] hb = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} X\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await s.WriteAsync(hb); await s.WriteAsync(body);
    }
}
