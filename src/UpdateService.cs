using System.IO.Compression;

namespace XboxScarlet;

/// <summary>
/// Actualizaciones desde GitHub (Releases de Yonisjpa/Scarlet), directo en la consola:
/// busca la versión más nueva, la descarga, cierra Scarlet, cambia los archivos y la vuelve a abrir.
///
/// Modo seguro: antes de cambiar nada se guarda una copia de la versión actual. La versión nueva queda "a prueba"
/// hasta que arranca bien; si falla al abrir dos veces seguidas, se regresa sola a la anterior.
/// Tu configuración, portadas y caché no se tocan (viven en %APPDATA% y %LOCALAPPDATA%, no junto al .exe).
/// </summary>
static class UpdateService
{
    const string Repo = "Yonisjpa/Scarlet";
    const string Asset = "Scarlet-win-x64.zip";
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };

    static string Dir => Directory.CreateDirectory(Path.Combine(Paths.Local, "update")).FullName;
    static string BackupDir => Path.Combine(Dir, "anterior");
    static string StagedDir => Path.Combine(Dir, "nueva");
    static string ProbationFile => Path.Combine(Dir, "a-prueba.txt");
    static string AppDir => AppContext.BaseDirectory.TrimEnd('\\', '/');

    static UpdateService()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Scarlet-Updater/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static Version Current
    {
        get
        {
            var v = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    static Version Parse(string tag)
    {
        var m = Regex.Match(tag ?? "", @"(\d+)\.(\d+)(?:\.(\d+))?");
        return m.Success ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0) : null;
    }

    static string Display(Version v) => v.Build > 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";

    public sealed record Release(Version Version, string Name, string Notes, string Url, long Size, DateTimeOffset Date);

    /// <summary>La versión publicada más nueva (null si no hay red o no hay releases).</summary>
    public static async Task<Release> LatestAsync()
    {
        string json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        using var d = JsonDocument.Parse(json);
        var r = d.RootElement;
        var v = Parse(r.GetProperty("tag_name").GetString());
        if (v == null) return null;
        foreach (var a in r.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(a.GetProperty("name").GetString(), Asset, StringComparison.OrdinalIgnoreCase)) continue;
            return new Release(v,
                r.TryGetProperty("name", out var n) ? n.GetString() : "Scarlet " + Display(v),
                r.TryGetProperty("body", out var b) ? b.GetString() : "",
                a.GetProperty("browser_download_url").GetString(),
                a.GetProperty("size").GetInt64(),
                r.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String ? p.GetDateTimeOffset() : DateTimeOffset.MinValue);
        }
        return null;
    }

    /// <summary>Para la interfaz: versión actual, la publicada y si conviene actualizar.</summary>
    public static async Task<object> CheckAsync()
    {
        try
        {
            var r = await LatestAsync();
            if (r == null) return new { current = Display(Current), available = false, error = "Todavía no hay versiones publicadas." };
            bool newer = r.Version > Current;
            Log.Write($"actualizaciones: tienes {Display(Current)}, la publicada es {Display(r.Version)}");
            return new { current = Display(Current), latest = Display(r.Version), available = newer, name = r.Name, notes = r.Notes, sizeMb = Math.Round(r.Size / 1048576.0), date = r.Date.ToUnixTimeSeconds() };
        }
        catch (Exception ex)
        {
            Log.Error("actualizaciones", ex);
            return new { current = Display(Current), available = false, error = "No se pudo revisar (¿hay internet?)." };
        }
    }

    /// <summary>Descarga y deja lista la versión nueva; luego cierra Scarlet para cambiar los archivos y la vuelve a abrir.</summary>
    public static async Task<object> InstallAsync(Action<double> progress, Action closeApp)
    {
        try
        {
            var r = await LatestAsync();
            if (r == null || r.Version <= Current) return new { ok = false, error = "Ya tienes la versión más nueva." };

            string zip = Path.Combine(Dir, Asset);
            Log.Write($"actualizaciones: descargando {Display(r.Version)} ({r.Size / 1048576} MB)");
            using (var resp = await http.GetAsync(r.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? r.Size, done = 0;
                using var src = await resp.Content.ReadAsStreamAsync();
                using var dst = File.Create(zip);
                var buf = new byte[1 << 16];
                int n, lastPct = -1;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    done += n;
                    int pct = (int)(done * 100 / Math.Max(1, total));
                    if (pct != lastPct) { lastPct = pct; progress?.Invoke(pct / 100.0); }
                }
                if (done != r.Size) throw new IOException($"La descarga quedó incompleta ({done} de {r.Size} bytes).");
            }

            if (Directory.Exists(StagedDir)) Directory.Delete(StagedDir, true);
            ZipFile.ExtractToDirectory(zip, StagedDir);
            File.Delete(zip);
            if (!File.Exists(Path.Combine(StagedDir, "Scarlet.exe"))) throw new IOException("El paquete no trae Scarlet.exe.");

            File.WriteAllText(ProbationFile, $"{Display(r.Version)}\n0");
            RunSwapScript(StagedDir, keepBackup: true, args: "--updated");
            Log.Write($"actualizaciones: {Display(r.Version)} lista; Scarlet se reinicia para instalarla");
            closeApp();
            return new { ok = true };
        }
        catch (Exception ex)
        {
            Log.Error("actualizar", ex);
            return new { ok = false, error = "No se pudo actualizar: " + ex.Message };
        }
    }

    /// <summary>
    /// Script que espera a que Scarlet se cierre, guarda la versión actual (si se pide), copia la nueva encima y la abre.
    /// robocopy reintenta si algún archivo sigue ocupado un momento.
    /// </summary>
    static void RunSwapScript(string from, bool keepBackup, string args)
    {
        int pid = Environment.ProcessId;
        string exe = Path.Combine(AppDir, "Scarlet.exe");
        string log = Path.Combine(Dir, "instalar.log");
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Continue'");
        sb.AppendLine($"Start-Transcript -Path '{Esc(log)}' -Force | Out-Null");
        sb.AppendLine($"try {{ Wait-Process -Id {pid} -Timeout 30 -ErrorAction SilentlyContinue }} catch {{}}");
        sb.AppendLine("Start-Sleep -Milliseconds 800");
        // Si algo (AnyFSE, el modo Xbox) volvió a abrir Scarlet mientras tanto, se cierra para poder cambiar los archivos.
        sb.AppendLine($"Get-Process Scarlet -ErrorAction SilentlyContinue | Where-Object {{ $_.Path -eq '{Esc(exe)}' }} | Stop-Process -Force -ErrorAction SilentlyContinue");
        sb.AppendLine("Start-Sleep -Milliseconds 500");
        if (keepBackup)
            sb.AppendLine($"robocopy '{Esc(AppDir)}' '{Esc(BackupDir)}' /MIR /R:3 /W:1 /XF log.txt /NFL /NDL /NJH /NJS | Out-Null");
        sb.AppendLine($"robocopy '{Esc(from)}' '{Esc(AppDir)}' /MIR /R:20 /W:1 /XF log.txt /NFL /NDL /NJH /NJS | Out-Null");
        sb.AppendLine($"Start-Process -FilePath '{Esc(exe)}' -ArgumentList '{args}' -WorkingDirectory '{Esc(AppDir)}'");
        sb.AppendLine("Stop-Transcript | Out-Null");
        string script = Path.Combine(Dir, "instalar.ps1");
        File.WriteAllText(script, sb.ToString(), new UTF8Encoding(true));
        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"")
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Dir });
    }

    static string Esc(string s) => s.Replace("'", "''");

    // ───── Modo seguro ─────

    /// <summary>
    /// Al arrancar: si esta versión está "a prueba", cuenta el intento. Si ya falló dos veces, se regresa a la anterior
    /// y devuelve true (Scarlet debe cerrarse sin abrir la interfaz).
    /// </summary>
    public static bool StartupCheck()
    {
        try
        {
            if (!File.Exists(ProbationFile)) return false;
            var lines = File.ReadAllLines(ProbationFile);
            int tries = lines.Length > 1 && int.TryParse(lines[1], out int t) ? t : 0;
            if (tries >= 2 && Directory.Exists(BackupDir) && File.Exists(Path.Combine(BackupDir, "Scarlet.exe")))
            {
                Log.Write($"modo seguro: la versión {lines[0]} no arrancó bien dos veces; se regresa a la anterior");
                File.Delete(ProbationFile);
                File.WriteAllText(Path.Combine(Dir, "regresada.txt"), lines[0]);
                RunSwapScript(BackupDir, keepBackup: false, args: "--rolledback");
                return true;
            }
            File.WriteAllText(ProbationFile, $"{lines[0]}\n{tries + 1}");
        }
        catch (Exception ex) { Log.Error("modo seguro", ex); }
        return false;
    }

    /// <summary>La versión nueva arrancó bien (la interfaz cargó y no tronó en un rato): deja de estar a prueba.</summary>
    public static void ConfirmGood()
    {
        try
        {
            if (!File.Exists(ProbationFile)) return;
            string v = File.ReadAllLines(ProbationFile)[0];
            File.Delete(ProbationFile);
            Log.Write($"actualizaciones: la versión {v} arrancó bien");
        }
        catch (Exception ex) { Log.Error("modo seguro", ex); }
    }

    public static bool InProbation => File.Exists(ProbationFile);

    /// <summary>Si la última actualización se regresó sola, devuelve qué versión falló (una sola vez, para avisar).</summary>
    public static string TakeRolledBackNotice()
    {
        try
        {
            string f = Path.Combine(Dir, "regresada.txt");
            if (!File.Exists(f)) return null;
            string v = File.ReadAllText(f).Trim();
            File.Delete(f);
            return v;
        }
        catch { return null; }
    }

    /// <summary>
    /// Error grave estando a prueba: si este ya era el segundo arranque de la versión nueva, se regresa a la anterior ahora mismo
    /// (en vez de reiniciar otra vez la versión que falla). Devuelve true si inició el regreso.
    /// </summary>
    public static bool RollbackNowIfFailing()
    {
        try
        {
            if (!File.Exists(ProbationFile)) return false;
            var lines = File.ReadAllLines(ProbationFile);
            int tries = lines.Length > 1 && int.TryParse(lines[1], out int t) ? t : 0;
            if (tries < 2 || !File.Exists(Path.Combine(BackupDir, "Scarlet.exe"))) return false;
            Log.Write($"modo seguro: la versión {lines[0]} falló otra vez; se regresa a la anterior");
            File.Delete(ProbationFile);
            File.WriteAllText(Path.Combine(Dir, "regresada.txt"), lines[0]);
            RunSwapScript(BackupDir, keepBackup: false, args: "--rolledback");
            return true;
        }
        catch { return false; }
    }
}
