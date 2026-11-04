using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace XboxScarlet;

/// <summary>Puente con la interfaz: mandar eventos, recibir mensajes, servir archivos locales, perfil y biblioteca.</summary>
sealed partial class MainForm
{
    // ───────────────────────────── Puente con la interfaz ─────────────────────────────

    void Ui(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) BeginInvoke(a);
        else a();
    }

    void Send(object message) => SendTo(null, message);

    /// <summary>Manda un mensaje a una vista web (null = la principal).</summary>
    void SendTo(CoreWebView2 target, object message)
    {
        string json;
        try { json = JsonSerializer.Serialize(message, J); }
        catch (Exception ex) { Log.Error("json", ex); return; }
        Ui(() =>
        {
            try { (target ?? web.CoreWebView2)?.PostWebMessageAsJson(json); }
            catch (Exception ex) { Log.Error("enviar", ex); }
        });
    }

    /// <summary>Marca de la interfaz: versión de la app + fecha de web\index.html.</summary>
    static string UiStamp()
    {
        long t = 0;
        try { t = File.GetLastWriteTimeUtc(Path.Combine(Paths.Web, "index.html")).Ticks; } catch { }
        return Application.ProductVersion + "-" + t.ToString("x");
    }

    void Emit(string ev, object data) => Send(new Dictionary<string, object> { ["event"] = ev, ["data"] = data });

    /// <summary>A la interfaz y también al menú rápido que se abre encima de los juegos.</summary>
    void EmitAll(string ev, object data)
    {
        var msg = new Dictionary<string, object> { ["event"] = ev, ["data"] = data };
        Send(msg);
        Ui(() => { var v = quick?.View; if (v != null) SendTo(v, msg); });
    }

    async void OnMessage(string json, CoreWebView2 from)
    {
        long id = 0;
        string type = "";
        try
        {
            JsonElement payload;
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var idv) && idv.ValueKind == JsonValueKind.Number) id = idv.GetInt64();
                if (root.TryGetProperty("type", out var tv)) type = tv.GetString() ?? "";
                payload = root.TryGetProperty("payload", out var pv) ? pv.Clone() : default;
            }
            object data = await HandleMessage(type, payload, from);
            SendTo(from, new Dictionary<string, object> { ["id"] = id, ["ok"] = true, ["data"] = data });
        }
        catch (Exception ex)
        {
            Log.Error("mensaje " + type, ex);
            SendTo(from, new Dictionary<string, object> { ["id"] = id, ["ok"] = false, ["error"] = ex.Message });
        }
    }

    /// <summary>
    /// Un mensaje de la interfaz (window.chrome.webview.postMessage). Cada tema tiene su archivo:
    /// MainForm.Mensajes.Juegos / Steam / Sistema / Red / Interfaz.
    /// </summary>
    Task<object> HandleMessage(string type, JsonElement p, CoreWebView2 from)
    {
        int dot = type.IndexOf('.');
        string prefix = dot < 0 ? type : type[..dot];
        return prefix switch
        {
            "game" or "ext" or "fs" or "compat" => GamesMessage(type, p, from),
            "steam" or "steamKey" or "steamId" or "sgdbKey" or "sgdb" or "cache" or "install" or "download" => SteamMessage(type, p, from),
            "system" or "audio" or "mixer" or "media" or "battery" or "cursor" or "autostart" or "gamebar" or "storage" or "diag" or "clipboard" or "rumble" or "pad" or "pads" or "bt" or "capture" => SystemMessage(type, p, from),
            "wifi" or "vpn" or "net" or "web" => NetMessage(type, p, from),
            "quick" or "overlay" or "ui" or "app" or "sounds" or "album" or "setup" or "update" => UiMessage(type, p, from),
            _ => throw new NotSupportedException("Mensaje desconocido: " + type)
        };
    }

    /// <summary>Agrega los juegos de la Xbox Store a la biblioteca (se vuelven a buscar cada 5 minutos como máximo).</summary>
    async Task<List<Dictionary<string, object>>> WithXboxGamesAsync(List<Dictionary<string, object>> list)
    {
        try
        {
            if (DateTime.Now - xboxScanned > TimeSpan.FromMinutes(5))
            {
                xboxScanned = DateTime.Now;
                await Task.Run(() => xbox.ScanAsync());
            }
            foreach (var g in xbox.Games)
            {
                long last = cfg.AppLastUsed.TryGetValue("xbox:" + g.Pfn, out var t) ? t : 0;
                var (grid, hero) = xbox.ArtFor(g.AppId);
                list.Add(new Dictionary<string, object>
                {
                    ["appid"] = g.AppId, ["name"] = g.Name, ["source"] = "xbox", ["kind"] = g.IsApp ? "app" : "game", ["installed"] = true,
                    ["hours"] = Hours("xbox:" + g.Pfn), ["lastTs"] = last, ["last"] = SteamService.Rel(last), ["ach"] = new[] { 0, 0 },
                    ["cols"] = g.IsApp ? new List<string>() : new List<string> { "Xbox" }, ["size"] = g.SizeGb, ["art"] = grid, ["hero"] = hero, ["storeId"] = g.StoreId
                });
            }
            return list.OrderByDescending(e => (long)e["lastTs"]).ToList();
        }
        catch (Exception ex) { Log.Error("xbox: biblioteca", ex); return list; }
    }

    /// <summary>Los juegos que agregaste tú, como un juego más de la biblioteca.</summary>
    List<Dictionary<string, object>> WithExternalGames(List<Dictionary<string, object>> list)
    {
        foreach (var g in cfg.ExternalGames)
        {
            string key = ExternalGames.Key(g);
            long last = cfg.AppLastUsed.TryGetValue(key, out var ts) ? ts : 0;
            list.Add(new Dictionary<string, object>
            {
                ["appid"] = g.Id, ["name"] = g.Name, ["source"] = "ext", ["kind"] = "game", ["installed"] = File.Exists(g.Path),
                ["hours"] = Hours(key), ["lastTs"] = last, ["last"] = last > 0 ? SteamService.Rel(last) : "nunca", ["ach"] = new[] { 0, 0 },
                ["cols"] = new List<string> { "Externos" }, ["size"] = 0.0,
                ["art"] = g.Image != null && File.Exists(g.Image) ? ArtCache.LocalUrl(g.Image) : null, ["iconArt"] = g.IconStyle,
                ["path"] = g.Path, ["args"] = g.Args ?? ""
            });
        }
        return list.OrderByDescending(e => (long)e["lastTs"]).ToList();
    }

    async Task<object> Profile()
    {
        var cached = SteamService.CachedProfile();
        var fresh = steam.FetchProfileAsync(art);
        if (cached == null) { var r = await fresh; RetryProfileIfOffline(r); return r; }
        _ = fresh.ContinueWith(t =>
        {
            if (t.Status != TaskStatus.RanToCompletion || t.Result == null) return;
            if (JsonSerializer.Serialize(t.Result, J) != JsonSerializer.Serialize(cached, J)) Emit("profile.changed", t.Result);
            RetryProfileIfOffline(t.Result);
        });
        return cached;
    }

    int profileRetries;

    /// <summary>Al prender la consola el Wi-Fi tarda: si no hubo red, se vuelve a intentar al rato.</summary>
    async void RetryProfileIfOffline(Dictionary<string, object> shown)
    {
        if (steam.ProfileOnline || profileRetries >= 5) return;
        profileRetries++;
        await Task.Delay(TimeSpan.FromSeconds(30 * profileRetries));
        try
        {
            var p = await steam.FetchProfileAsync(art);
            if (p != null && JsonSerializer.Serialize(p, J) != JsonSerializer.Serialize(shown, J)) Emit("profile.changed", p);
            RetryProfileIfOffline(p);
        }
        catch (Exception ex) { Log.Error("perfil (reintento)", ex); }
    }

    /// <summary>Sirve archivos locales (portadas en caché, avatar, capturas) a la interfaz en https://files.scarlet/f?p=RUTA</summary>
    void ServeFile(object sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            var uri = new Uri(e.Request.Uri);
            string path = System.Web.HttpUtility.ParseQueryString(uri.Query)["p"];
            // Direcciones guardadas antes del cambio de nombre (…\XboxScarlet\…): la carpeta ahora se llama Scarlet.
            if (!string.IsNullOrEmpty(path) && !File.Exists(path) && path.Contains(@"\XboxScarlet\", StringComparison.OrdinalIgnoreCase))
                path = Regex.Replace(path, @"\\XboxScarlet\\", @"\Scarlet\", RegexOptions.IgnoreCase);
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || !Allowed(path))
            {
                e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long len = fs.Length;
            string range = e.Request.Headers.Contains("Range") ? e.Request.Headers.GetHeader("Range") : null;
            var m = range == null ? null : Regex.Match(range, @"bytes=(\d*)-(\d*)");
            if (m != null && m.Success && len > 0)
            {
                // Los videos se piden por pedazos (para empezar rápido y poder adelantar): se contesta 206 con ese pedazo.
                long start, end;
                if (m.Groups[1].Value.Length == 0) { start = Math.Max(0, len - long.Parse(m.Groups[2].Value)); end = len - 1; }
                else { start = long.Parse(m.Groups[1].Value); end = m.Groups[2].Value.Length > 0 ? Math.Min(long.Parse(m.Groups[2].Value), len - 1) : len - 1; }
                if (start >= len) { fs.Dispose(); e.Response = env.CreateWebResourceResponse(null, 416, "Range Not Satisfiable", $"Content-Range: bytes */{len}"); return; }
                end = Math.Min(end, start + 4 * 1024 * 1024 - 1);   // como mucho 4 MB por pedazo; el reproductor pide el siguiente
                var buf = new byte[end - start + 1];
                fs.Seek(start, SeekOrigin.Begin);
                int read = 0;
                while (read < buf.Length) { int n = fs.Read(buf, read, buf.Length - read); if (n <= 0) break; read += n; }
                fs.Dispose();
                e.Response = env.CreateWebResourceResponse(new MemoryStream(buf, 0, read), 206, "Partial Content",
                    $"Content-Type: {Mime(path)}\r\nContent-Length: {read}\r\nContent-Range: bytes {start}-{start + read - 1}/{len}\r\nAccept-Ranges: bytes\r\nAccess-Control-Allow-Origin: *");
                return;
            }
            e.Response = env.CreateWebResourceResponse(fs, 200, "OK", $"Content-Type: {Mime(path)}\r\nContent-Length: {len}\r\nAccept-Ranges: bytes\r\nCache-Control: max-age=604800\r\nAccess-Control-Allow-Origin: *");
        }
        catch (Exception ex) { Log.Error("servir archivo", ex); }
    }

    static string SoundsDir => Path.Combine(AppContext.BaseDirectory, "sounds");

    /// <summary>
    /// Sonidos propios (nombre sin extensión → dirección local): los de la carpeta "sounds" junto a la app y, encima,
    /// los del paquete importado (en tus datos de Scarlet, así las actualizaciones no lo borran).
    /// </summary>
    static string lastSounds;
    static Dictionary<string, string> SoundsList()
    {
        var map = new Dictionary<string, string>();
        try
        {
            foreach (var dir in new[] { SoundsDir, SoundPack.Dir, SoundPack.AchDir })
                if (Directory.Exists(dir))
                    foreach (var f in Directory.EnumerateFiles(dir))
                        if (Regex.IsMatch(Path.GetExtension(f), @"^\.(wav|mp3|ogg|m4a)$", RegexOptions.IgnoreCase))
                            map[Path.GetFileNameWithoutExtension(f).ToLowerInvariant()] = ArtCache.LocalUrl(f);
            string list = string.Join(", ", map.Keys);   // se anota solo si cambió (antes salía en cada pantalla que pedía sonidos)
            if (map.Count > 0 && list != lastSounds) { lastSounds = list; Log.Write("sonidos propios: " + list); }
        }
        catch (Exception ex) { Log.Error("sonidos", ex); }
        return map;
    }

    bool Allowed(string path) =>
        Paths.Under(path, SoundsDir) || Paths.Under(path, SoundPack.Dir) || Paths.Under(path, SoundPack.AchDir) || Paths.Under(path, Paths.Cache) || Paths.Under(path, steam.SteamPath) || album.Roots().Any(r => Paths.Under(path, r));

    static string Mime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".svg" => "image/svg+xml",
        ".wav" => "audio/wav",
        ".mp3" => "audio/mpeg",
        ".ogg" => "audio/ogg",
        ".m4a" => "audio/mp4",
        ".img" => Sniff(path),
        _ => "application/octet-stream"
    };

    /// <summary>Imágenes guardadas sin extensión (íconos de sitios web): el tipo se ve en los primeros bytes.</summary>
    static string Sniff(string path)
    {
        try
        {
            var b = new byte[4];
            using (var f = File.OpenRead(path)) f.Read(b, 0, 4);
            return b[0] == 0x89 && b[1] == 0x50 ? "image/png" : b[0] == 0xFF && b[1] == 0xD8 ? "image/jpeg" : b[0] == 'G' && b[1] == 'I' ? "image/gif"
                : b[0] == 'R' && b[1] == 'I' ? "image/webp" : b[0] == 0 && b[1] == 0 && b[2] == 1 ? "image/x-icon" : "application/octet-stream";
        }
        catch { return "application/octet-stream"; }
    }
}
