using System.Net.NetworkInformation;

namespace XboxScarlet;

/// <summary>
/// ¿Hay internet de verdad? (no solo Wi-Fi: los hoteles y aeropuertos conectan pero no dejan salir) y ¿esta red deja
/// llegar a Steam? Se revisa al cambiar de red y cada 30 s, con pruebas cortas.
/// </summary>
static class NetCheck
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(4) };
    public static volatile bool Internet = true;       // hasta revisar se supone que sí (no esconder nada de más al arrancar)
    public static volatile bool SteamReachable = true;
    public static DateTime CheckedAt = DateTime.MinValue;
    public static event Action Changed;
    static int steamFails, netFails;
    static readonly SemaphoreSlim gate = new(1, 1);
    static CancellationTokenSource soon;

    public static void Start()
    {
        NetworkChange.NetworkAddressChanged += (_, _) => Soon();
        NetworkChange.NetworkAvailabilityChanged += (_, _) => Soon();
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await CheckAsync();
                await Task.Delay(Internet && SteamReachable ? 30000 : 15000);
            }
        });
    }

    /// <summary>Cambió la red: se revisa en 2 s (cuando ya agarró dirección).</summary>
    static void Soon()
    {
        soon?.Cancel();
        var cts = soon = new CancellationTokenSource();
        _ = Task.Delay(2000, cts.Token).ContinueWith(t => { if (!t.IsCanceled) _ = CheckAsync(); }, TaskScheduler.Default);
    }

    public static async Task CheckAsync()
    {
        if (!await gate.WaitAsync(0)) return;
        try
        {
            bool net = false, steamOk = SteamReachable, adapter = NetworkInterface.GetIsNetworkAvailable();
            if (adapter)
            {
                // Las dos pruebas a la vez: la de Windows (detecta los Wi-Fi de hotel que piden iniciar sesión) y la de Steam.
                var win = Task.Run(async () => { try { return (await http.GetStringAsync("http://www.msftconnecttest.com/connecttest.txt")).Trim() == "Microsoft Connect Test"; } catch { return false; } });
                var stm = Task.Run(async () => { try { using var r = await http.GetAsync("https://api.steampowered.com/ISteamWebAPIUtil/GetServerInfo/v1/", HttpCompletionOption.ResponseHeadersRead); return r.IsSuccessStatusCode; } catch { return false; } });
                bool w = await win, ok = await stm;
                net = w || ok;   // si alguna de las dos llega, hay internet (algunas redes bloquean la prueba de Windows)
                if (net)
                {
                    steamFails = ok ? 0 : steamFails + 1;
                    steamOk = steamFails < 2;   // dos fallas seguidas: no es un tropiezo, la red lo bloquea
                }
            }
            if (!net) { steamFails = 0; steamOk = true; }
            // Un tropiezo no es "sin internet": con la red conectada hacen falta dos fallas seguidas.
            netFails = net ? 0 : netFails + 1;
            if (!net && adapter && netFails < 2 && Internet) { _ = Task.Delay(5000).ContinueWith(_ => CheckAsync()); return; }
            CheckedAt = DateTime.Now;
            if (net != Internet || steamOk != SteamReachable)
            {
                Internet = net; SteamReachable = steamOk;
                Log.Write($"red: {(net ? "con internet" : "sin internet")}{(net && !steamOk ? " · esta red bloquea Steam" : "")}");
                try { Changed?.Invoke(); } catch (Exception ex) { Log.Error("red", ex); }
            }
        }
        finally { gate.Release(); }
    }
}

/// <summary>
/// VPN desde Scarlet, con programas gratis y confiables instalados en la consola:
///   · Cloudflare WARP: gratis, sin cuenta. Se maneja con warp-cli (no pide permisos de administrador).
///   · WireGuard (con un perfil .conf de Proton VPN u otro): prender o apagar un túnel pide administrador, así que la
///     primera vez Windows pregunta UNA vez y se dejan dos tareas programadas ("Scarlet\VPN encender/apagar") que
///     después Scarlet corre sin preguntar.
/// </summary>
static class Vpn
{
    const string Tunnel = "scarlet";
    static string Pf => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    static string Pf86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    /// <summary>warp-cli de Cloudflare WARP (ahora "Cloudflare One Client"): se busca en cualquier carpeta de Cloudflare y en el PATH.</summary>
    public static string WarpCli
    {
        get
        {
            foreach (var root in new[] { Pf, Pf86 }.Distinct())
            {
                string cf = Path.Combine(root, "Cloudflare");
                foreach (var f in new[] { Path.Combine(cf, "Cloudflare WARP", "warp-cli.exe"), Path.Combine(cf, "Cloudflare One Client", "warp-cli.exe") })
                    if (File.Exists(f)) return f;
                try { if (Directory.Exists(cf)) foreach (var d in Directory.EnumerateDirectories(cf)) { string f = Path.Combine(d, "warp-cli.exe"); if (File.Exists(f)) return f; } } catch { }
            }
            foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
                try { string f = Path.Combine(d.Trim(), "warp-cli.exe"); if (File.Exists(f)) return f; } catch { }
            return null;
        }
    }
    public static string WireGuardExe => new[] { Path.Combine(Pf, "WireGuard", "wireguard.exe"), Path.Combine(Pf86, "WireGuard", "wireguard.exe") }.FirstOrDefault(File.Exists);
    static string Dir => Directory.CreateDirectory(Path.Combine(Paths.Local, "vpn")).FullName;
    public static string Conf => Path.Combine(Dir, Tunnel + ".conf");
    const string TaskOn = @"\Scarlet\VPN encender", TaskOff = @"\Scarlet\VPN apagar";
    static readonly SemaphoreSlim gate = new(1, 1);

    static async Task<(int code, string output)> RunAsync(string exe, string args, int timeoutMs = 12000)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        using var p = Process.Start(psi);
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeoutMs);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } return (-1, "tardó demasiado"); }
        return (p.ExitCode, ((await o) + "\n" + (await e)).Trim());
    }

    // ───── WARP ─────

    /// <summary>warp-cli nuevo pide aceptar los términos (--accept-tos); el viejo no conoce esa opción.</summary>
    static async Task<(int code, string output)> Warp(string args, int timeoutMs = 12000)
    {
        var r = await RunAsync(WarpCli, "--accept-tos " + args, timeoutMs);
        if (Regex.IsMatch(r.output, @"unexpected argument|Found argument|unrecognized|unknown (flag|option)", RegexOptions.IgnoreCase))
            r = await RunAsync(WarpCli, args, timeoutMs);
        return r;
    }

    /// <summary>"on", "off", "connecting" o "unregistered" (falta registrarse la primera vez).</summary>
    static async Task<string> WarpStatus()
    {
        var (_, o) = await Warp("status", 6000);
        if (Regex.IsMatch(o, @"Registration ?Missing|registration is missing|Unable.*registration", RegexOptions.IgnoreCase)) return "unregistered";
        if (Regex.IsMatch(o, @"Disconnected", RegexOptions.IgnoreCase)) return "off";
        if (Regex.IsMatch(o, @"Connecting", RegexOptions.IgnoreCase)) return "connecting";
        if (Regex.IsMatch(o, @"\bConnected\b", RegexOptions.IgnoreCase)) return "on";
        return "off";
    }

    // ───── WireGuard ─────

    static async Task<string> WgStatus()
    {
        var (code, o) = await RunAsync("sc.exe", $"query \"WireGuardTunnel${Tunnel}\"", 5000);
        if (code != 0 || o.Contains("1060")) return "off";   // el servicio no existe: túnel apagado
        if (o.Contains("RUNNING")) return "on";
        if (o.Contains("START_PENDING")) return "connecting";
        return "off";
    }

    static async Task<bool> WgTasksReady() => (await RunAsync("schtasks.exe", $"/query /tn \"{TaskOn}\"", 5000)).code == 0 && (await RunAsync("schtasks.exe", $"/query /tn \"{TaskOff}\"", 5000)).code == 0;

    /// <summary>Una sola vez: Windows pide permiso y se crean las tareas que prenden/apagan el túnel sin volver a preguntar.</summary>
    public static async Task<(bool ok, string error)> SetupWireGuardAsync()
    {
        string wg = WireGuardExe;
        if (wg == null) return (false, "Primero instala WireGuard.");
        string user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        string Esc(string s) => s.Replace("'", "''");
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine($"$p = New-ScheduledTaskPrincipal -UserId '{Esc(user)}' -LogonType Interactive -RunLevel Highest");
        sb.AppendLine("$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 2)");
        sb.AppendLine($"$on = New-ScheduledTaskAction -Execute '{Esc(wg)}' -Argument '/installtunnelservice \"{Esc(Conf)}\"'");
        sb.AppendLine($"$off = New-ScheduledTaskAction -Execute '{Esc(wg)}' -Argument '/uninstalltunnelservice {Tunnel}'");
        sb.AppendLine("Register-ScheduledTask -TaskPath '\\Scarlet\\' -TaskName 'VPN encender' -Action $on -Principal $p -Settings $s -Force | Out-Null");
        sb.AppendLine("Register-ScheduledTask -TaskPath '\\Scarlet\\' -TaskName 'VPN apagar' -Action $off -Principal $p -Settings $s -Force | Out-Null");
        string script = Path.Combine(Dir, "preparar.ps1");
        File.WriteAllText(script, sb.ToString(), new UTF8Encoding(true));
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"")
            { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            using var cts = new CancellationTokenSource(60000);
            await p.WaitForExitAsync(cts.Token);
        }
        catch (System.ComponentModel.Win32Exception) { return (false, "Cancelaste el permiso de Windows."); }
        catch (Exception ex) { Log.Error("VPN: preparar WireGuard", ex); return (false, "No se pudo preparar: " + ex.Message); }
        bool ok = await WgTasksReady();
        Log.Write("VPN: tareas de WireGuard " + (ok ? "listas" : "no se crearon"));
        return ok ? (true, null) : (false, "Windows no dejó crear las tareas de la VPN.");
    }

    /// <summary>Busca el perfil (.conf) más nuevo en Descargas, Escritorio o Documentos y lo guarda para Scarlet.</summary>
    public static (bool ok, string name, string error) ImportWireGuard()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[] { Path.Combine(home, "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
        FileInfo best = null;
        foreach (var r in roots.Where(Directory.Exists))
            try
            {
                foreach (var f in new DirectoryInfo(r).EnumerateFiles("*.conf", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true }))
                {
                    if (f.Length > 64 * 1024) continue;
                    string t; try { t = File.ReadAllText(f.FullName); } catch { continue; }
                    if (!t.Contains("[Interface]") || !t.Contains("[Peer]") || !t.Contains("PrivateKey")) continue;
                    if (best == null || f.LastWriteTimeUtc > best.LastWriteTimeUtc) best = f;
                }
            }
            catch { }
        if (best == null) return (false, null, "No encontré un perfil de WireGuard (.conf) en Descargas, Escritorio o Documentos.");
        File.Copy(best.FullName, Conf, true);
        Log.Write($"VPN: perfil de WireGuard importado ({best.Name})");
        return (true, Path.GetFileNameWithoutExtension(best.Name), null);
    }

    public static void RemoveWireGuard() { try { File.Delete(Conf); } catch { } }

    // ───── para la interfaz ─────

    public static async Task<Dictionary<string, object>> StatusAsync(Config cfg)
    {
        bool warp = WarpCli != null, wg = WireGuardExe != null, profile = File.Exists(Conf);
        string provider = cfg.VpnProvider;
        if (provider == "warp" && !warp || provider == "wireguard" && !(wg && profile) || string.IsNullOrEmpty(provider))
            provider = warp ? "warp" : wg && profile ? "wireguard" : "none";
        string status = "off";
        try { status = provider == "warp" ? await WarpStatus() : provider == "wireguard" ? await WgStatus() : "off"; }
        catch (Exception ex) { Log.Write("VPN: estado: " + ex.Message); }
        return new Dictionary<string, object>
        {
            ["provider"] = provider, ["warp"] = warp, ["wireguard"] = wg, ["profile"] = profile ? (cfg.VpnProfileName ?? "Perfil") : null,
            ["status"] = status == "unregistered" ? "off" : status, ["internet"] = NetCheck.Internet, ["steam"] = NetCheck.SteamReachable
        };
    }

    /// <summary>Prende o apaga la VPN elegida y espera a que quede (hasta ~20 s).</summary>
    public static async Task<Dictionary<string, object>> SetAsync(Config cfg, bool on)
    {
        await gate.WaitAsync();
        try
        {
            var st = await StatusAsync(cfg);
            string provider = (string)st["provider"];
            if (provider == "none") return Err("Instala Cloudflare WARP o importa un perfil de WireGuard en Ajustes › VPN.");
            if (provider == "warp")
            {
                if (on && await WarpStatus() == "unregistered")
                {
                    var (_, reg) = await Warp("registration new", 20000);
                    Log.Write("VPN: WARP registrado · " + reg.Split('\n')[0]);
                }
                var (code, o) = await Warp(on ? "connect" : "disconnect");
                Log.Write($"VPN: WARP {(on ? "conectar" : "desconectar")} → {o.Split('\n')[0]}");
                if (code != 0 && !Regex.IsMatch(o, "Success", RegexOptions.IgnoreCase)) return Err("WARP no respondió: " + o.Split('\n')[0]);
            }
            else
            {
                if (!await WgTasksReady()) return Err("setup");
                var (code, o) = await RunAsync("schtasks.exe", $"/run /tn \"{(on ? TaskOn : TaskOff)}\"", 8000);
                Log.Write($"VPN: WireGuard {(on ? "encender" : "apagar")} → {o.Split('\n')[0]}");
                if (code != 0) return Err("Windows no dejó " + (on ? "prender" : "apagar") + " el túnel.");
            }
            string want = on ? "on" : "off", now = "";
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(500);
                now = provider == "warp" ? await WarpStatus() : await WgStatus();
                if (now == want) break;
            }
            _ = Task.Delay(1500).ContinueWith(_ => NetCheck.CheckAsync());   // con la VPN cambia lo que se alcanza
            var res = await StatusAsync(cfg);
            if (now != want) res["error"] = on ? "La VPN no terminó de conectar. Puede que esta red también la bloquee." : "La VPN no se apagó.";
            return res;
        }
        catch (Exception ex) { Log.Error("VPN", ex); return Err("No se pudo: " + ex.Message); }
        finally { gate.Release(); }
    }

    static Dictionary<string, object> Err(string e) => new() { ["error"] = e };
}
