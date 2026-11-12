using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace XboxScarlet;

/// <summary>Vigilancia de la consola: batería, juego que no responde, toques bloqueados, diagnóstico, Dolby, íconos y portapapeles.</summary>
sealed partial class MainForm
{
    // ───── Batería baja (la interfaz tenía el aviso, pero la app nunca lo mandaba) ─────
    bool battWarned;
    void CheckBattery()
    {
        try
        {
            var ps = SystemInformation.PowerStatus;
            if (ps.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery)) return;
            float lvl = ps.BatteryLifePercent;
            bool charging = ps.PowerLineStatus == PowerLineStatus.Online;
            if (charging || lvl > 0.20f) { battWarned = false; return; }
            if (battWarned || lvl > 0.15f) return;
            battWarned = true;
            Log.Write($"batería baja: {Math.Round(lvl * 100)} %");
            Emit("battery.low", new { level = lvl });   // la interfaz lo muestra (encima del juego si estás jugando)
        }
        catch { }
    }

    // ───── Juego que no responde ─────
    int hungTicks; bool hungAsked; DateTime hungSnooze = DateTime.MinValue;

    /// <summary>Si el juego deja de responder 20 s mientras lo tienes enfrente, Scarlet te pregunta si esperar o cerrarlo.</summary>
    void CheckHung(IntPtr front)
    {
        if (tracker == null || !tracker.Active) { hungTicks = 0; hungAsked = false; return; }
        IntPtr h = tracker.Window != IntPtr.Zero ? tracker.Window : tracker.Candidate;
        bool hung = h != IntPtr.Zero && Native.IsWindow(h) && Native.IsHungAppWindow(h) && !GameBusy(h);
        if (!hung) { if (hungTicks >= 2) Log.Write($"el juego volvió a responder (estuvo ~{hungTicks * 3} s sin responder)"); hungTicks = 0; hungAsked = false; return; }
        if (++hungTicks == 2) Log.Write($"el juego no responde: {Native.Describe(h)} · {MemoryReport(h)}");
        bool inScarlet = front == Handle && web.Visible;
        // Ya no se le quita la pantalla a un juego congelado (eso pudo ser lo que terminó de tumbar a Stellar Blade):
        // a los ~20 s solo un aviso chiquito encima; si quieres cerrarlo, presionas el botón Xbox y ahí se pregunta.
        if (hungTicks < 7 || hungAsked || inScarlet || DateTime.Now < hungSnooze) return;
        hungAsked = true;
        Log.Write("el juego lleva 20 s sin responder: aviso encima (sin quitarle la pantalla)");
        ToastForm.Pop(NameOfApp(tracker.AppId) + " no responde", "Espera un momento. Si quieres cerrarlo, presiona el botón Xbox", "!", "#f2c230", null);
    }

    // Windows marca "no responde" cuando la ventana deja de atender mensajes 5 s, pero Stellar Blade hace eso a ratos
    // mientras sigue jugándose perfecto (su ventana está ocupada y el juego corre en otros hilos). Si el proceso sigue
    // usando el procesador, está vivo: solo cuenta como congelado si además casi no trabaja.
    TimeSpan busyCpu; uint busyPid; DateTime busyAt;
    bool GameBusy(IntPtr h)
    {
        try
        {
            Native.GetWindowThreadProcessId(h, out uint p);
            using var pr = Process.GetProcessById((int)p);
            var cpu = pr.TotalProcessorTime; var now = DateTime.UtcNow;
            bool same = p == busyPid && busyAt != default;
            double used = same ? (cpu - busyCpu).TotalMilliseconds / Math.Max(1, (now - busyAt).TotalMilliseconds) : 1;
            busyPid = p; busyCpu = cpu; busyAt = now;
            return used > 0.15;   // más de 15 % de un núcleo: está trabajando
        }
        catch { return false; }
    }

    /// <summary>¿El juego está congelado ahora mismo (para que el botón Xbox ofrezca cerrarlo)?</summary>
    bool GameHung => tracker != null && tracker.Active && hungTicks >= 3;

    /// <summary>Al presionar el botón Xbox con el juego congelado: Scarlet al frente y la pregunta de esperar o cerrarlo.</summary>
    void AskAboutHungGame()
    {
        Log.Write("botón Xbox con el juego congelado: se pregunta si esperar o cerrarlo");
        TopMost = false;
        ShowUi();
        Emit("game.hung", new { appid = tracker.AppId, name = NameOfApp(tracker.AppId) });
    }

    [StructLayout(LayoutKind.Sequential)] struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    /// <summary>Para el log: memoria libre del equipo y cuánta usa el juego (para saber si se congela por falta de memoria).</summary>
    static string MemoryReport(IntPtr gameWindow)
    {
        try
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            GlobalMemoryStatusEx(ref m);
            string game = "";
            Native.GetWindowThreadProcessId(gameWindow, out uint pid);
            try { using var p = Process.GetProcessById((int)pid); game = $" · el juego usa {p.WorkingSet64 / 1048576} MB ({p.PrivateMemorySize64 / 1048576} MB propios)"; } catch { }
            return $"memoria libre {m.ullAvailPhys / 1048576} de {m.ullTotalPhys / 1048576} MB ({m.dwMemoryLoad} % en uso), archivo de paginación libre {m.ullAvailPageFile / 1048576} MB{game}";
        }
        catch { return "memoria: sin datos"; }
    }

    // ───── Toques bloqueados (el sonido de error de Windows al tocar la pantalla) ─────
    IntPtr lastBlocked;

    /// <summary>
    /// Windows suena "error" cuando tocas una ventana bloqueada por una ventanita que espera respuesta. Si esa ventanita
    /// está escondida (casi siempre un aviso de Steam que se escondió), no hay forma de contestarla: se muestra al frente.
    /// </summary>
    void CheckTouchBlocked()
    {
        ClearStrayImeWindows();
        try
        {
            var b = Screen.PrimaryScreen.Bounds;
            IntPtr root = Native.RootAt(new Point(b.Left + b.Width / 2, b.Top + b.Height / 2));
            if (root == IntPtr.Zero || Native.IsWindowEnabled(root)) { lastBlocked = IntPtr.Zero; return; }
            // 1.6.1: las ventanitas invisibles del teclado de Windows («Default IME», «MSCTFIME UI») siempre están
            // «deshabilitadas»; no son un aviso esperando respuesta (al encender se confundían con uno).
            if (Native.ClassName(root) is "IME" or "MSCTFIME UI") { lastBlocked = IntPtr.Zero; return; }
            IntPtr popup = Native.BlockingPopup(root);
            bool visible = popup != IntPtr.Zero && Native.IsWindowVisible(popup);
            if (root != lastBlocked)
            {
                lastBlocked = root;
                Native.GetWindowRect(root, out var rr);
                Log.Write($"toque bloqueado: {Native.Describe(root)} [{Native.ClassName(root)}, {rr.Right - rr.Left}×{rr.Bottom - rr.Top}] espera a {(popup == IntPtr.Zero ? "(ninguna ventana)" : Native.Describe(popup))}{(visible ? "" : " (escondida)")}");
            }
            bool steamPopup = popup != IntPtr.Zero && (hiddenSteam.Contains(popup) || Path.GetFileName(Native.WindowProcessPath(popup, out _) ?? "").StartsWith("steam", StringComparison.OrdinalIgnoreCase));
            if (popup != IntPtr.Zero && !visible && steamPopup)
            {
                steamDialogs.Add(popup);   // que el guardián de Steam no la vuelva a esconder
                TopMost = false;
                Native.ShowWindowAsync(popup, 5 /* SW_SHOW */);
                Native.ForceForeground(popup);
                Log.Write("toque bloqueado: se muestra la ventanita para que la contestes");
            }
            else if (popup == IntPtr.Zero && root == Handle)
            {
                Native.EnableWindow(Handle, true);   // Scarlet quedó bloqueada sin ninguna ventanita: se desbloquea
                Log.Write("toque bloqueado: Scarlet se desbloqueó");
            }
        }
        catch (Exception ex) { Log.Error("toque bloqueado", ex); }
    }

    // ───── 1.8.1: ventanitas del teclado de Windows que se quedan encima de todo ─────
    // Windows crea para cada programa ventanitas internas del teclado («Default IME», «MSCTFIME UI»). Siempre están
    // «deshabilitadas»: si una de Scarlet se queda visible, con tamaño y encima de todo, cada toque que cae en ella
    // suena «error» y no hace nada, en Scarlet y en cualquier juego. Se revisan 9 puntos de la pantalla y, si la de
    // arriba es una de esas de Scarlet, se esconde (Windows la vuelve a crear cuando hace falta).
    readonly HashSet<IntPtr> imeLogged = new();

    void ClearStrayImeWindows()
    {
        try
        {
            var b = Screen.PrimaryScreen.Bounds;
            uint me = (uint)Environment.ProcessId;
            for (int i = 0; i < 9; i++)
            {
                var pt = new Point(b.Left + b.Width * (1 + 2 * (i % 3)) / 6, b.Top + b.Height * (1 + 2 * (i / 3)) / 6);
                IntPtr root = Native.RootAt(pt);
                if (root == IntPtr.Zero || root == Handle || Native.IsWindowEnabled(root)) continue;
                Native.GetWindowThreadProcessId(root, out uint pid);
                string cls = Native.ClassName(root);
                if (pid != me || cls is not ("IME" or "MSCTFIME UI")) continue;
                Native.GetWindowRect(root, out var r);
                Native.ShowWindowAsync(root, 0 /* SW_HIDE */);
                if (imeLogged.Add(root))
                    Log.Write($"toque: se escondió una ventanita del teclado de Scarlet («{cls}», {r.Right - r.Left}×{r.Bottom - r.Top} en {r.Left},{r.Top}) que estaba encima de todo y bloqueaba los toques");
            }
        }
        catch (Exception ex) { Log.Error("toque (ventanitas del teclado)", ex); }
    }

    /// <summary>Batería con datos reales de Windows: si carga, cuánto falta para llenarse; si no, cuánto queda.</summary>
    static object BatteryInfo()
    {
        try
        {
            long rate = 0, remaining = 0, full = 0; bool charging = false, any = false;
            using (var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT ChargeRate, DischargeRate, RemainingCapacity, Charging FROM BatteryStatus"))
                foreach (System.Management.ManagementObject o in s.Get())
                    using (o)
                    {
                        any = true;
                        charging |= o["Charging"] is bool c && c;
                        remaining += Convert.ToInt64(o["RemainingCapacity"] ?? 0);
                        rate += charging ? Convert.ToInt64(o["ChargeRate"] ?? 0) : Convert.ToInt64(o["DischargeRate"] ?? 0);
                    }
            using (var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
                foreach (System.Management.ManagementObject o in s.Get()) using (o) full += Convert.ToInt64(o["FullChargedCapacity"] ?? 0);
            if (!any) return null;
            var ps = SystemInformation.PowerStatus;
            bool plugged = ps.PowerLineStatus == PowerLineStatus.Online;
            int? toFull = charging && rate > 0 && full > remaining ? (int)Math.Round((full - remaining) * 60.0 / rate) : null;
            int? left = !plugged && rate > 0 ? (int)Math.Round(remaining * 60.0 / rate) : ps.BatteryLifeRemaining > 0 ? ps.BatteryLifeRemaining / 60 : null;
            return new { charging, plugged, minutesToFull = toFull, minutesLeft = plugged ? null : left };
        }
        catch (Exception ex) { Log.Error("batería", ex); return null; }
    }

    /// <summary>Pantalla de diagnóstico (Ajustes): lo que Scarlet está detectando ahora mismo.</summary>
    object Diagnostics()
    {
        string spatial = "—";
        try
        {
            string id = Windows.Media.Devices.MediaDevice.GetDefaultAudioRenderId(Windows.Media.Devices.AudioDeviceRole.Default);
            var f = Windows.Media.Audio.SpatialAudioDeviceConfiguration.GetForDeviceId(id)?.ActiveSpatialAudioFormat ?? "";
            spatial = f.Length == 0 ? "ninguno" :
                f.Equals(Windows.Media.Audio.SpatialAudioFormatSubtype.DolbyAtmosForHeadphones, StringComparison.OrdinalIgnoreCase) ? "Dolby Atmos para audífonos" :
                f.Equals(Windows.Media.Audio.SpatialAudioFormatSubtype.DolbyAtmosForHomeTheater, StringComparison.OrdinalIgnoreCase) ? "Dolby Atmos para cine en casa" :
                f.Equals(Windows.Media.Audio.SpatialAudioFormatSubtype.DolbyAtmosForSpeakers, StringComparison.OrdinalIgnoreCase) ? "Dolby Atmos para bocinas" :
                f.Equals(Windows.Media.Audio.SpatialAudioFormatSubtype.WindowsSonic, StringComparison.OrdinalIgnoreCase) ? "Windows Sonic" : f;
        }
        catch (Exception ex) { spatial = "error: " + ex.Message; }
        string gameExe = null;
        try { gameExe = GameProcess(IntPtr.Zero, AudioMixer.Sessions()).exe; } catch { }
        var outs = AudioMixer.Outputs();
        var diag = new Dictionary<string, object>
        {
            ["Versión"] = "beta " + Application.ProductVersion,
            ["Juego en curso"] = tracker != null && tracker.Active ? $"{NameOfApp(tracker.AppId)} · {(tracker.IsReady ? "listo" : "abriendo")} · {gameExe ?? "proceso sin detectar"}" : "ninguno",
            ["Apps con sonido"] = AudioMixer.Describe() is string d && d.Length > 0 ? d : "ninguna",
            ["Audio espacial"] = spatial,
            ["Salida de audio"] = outs.FirstOrDefault(o => o.current)?.name ?? "—",
            ["Música (Windows)"] = media.Describe(),
            ["Rendimiento"] = Perf.Describe() + (quick != null && quick.Suspended ? " · el menú encima del juego está congelado (cerrado)" : ""),
            ["Steam"] = $"{(steamUiAllowed ? "visible (tienda/chat)" : "escondido")}{(BigPictureWindow() != IntPtr.Zero ? " · Big Picture abierto" : "")}{(AudioFocus.SteamMuted ? " · en silencio" : "")}",
            ["Puntero"] = CursorHider.Hidden ? "escondido" : "visible",
            ["Silenciadas por Scarlet"] = AudioFocus.MutedList() is string m && m.Length > 0 ? m : "ninguna",
            ["Juego callado"] = gameExe == null ? "—" : AudioFocus.IsMuted(gameExe) switch { true => "sí", false => "no (está sonando)", _ => "no tiene sonido" },
            ["Juego congelado"] = GameFreezer.IsFrozen ? "sí" : "no",
            ["Toque en pantalla"] = lastBlocked != IntPtr.Zero && Native.IsWindow(lastBlocked) ? "bloqueado por " + Native.Describe(lastBlocked) : "normal"
        };
        int? briNow = SystemService.GetBrightness();
        diag["Brillo"] = briNow is int bn ? bn + " %" : "esta pantalla no deja cambiarlo";
        diag["Volumen general"] = AudioMixer.GetMaster() is int vn ? vn + " %" : "no se pudo leer";
        diag["Wi-Fi (permiso)"] = SystemService.RadioAccess;
        // Cada salida de audio con su volumen y qué apps suenan por ella: para ver por dónde sale de verdad el sonido.
        int n = 0;
        foreach (var (device, detail) in AudioMixer.Inventory()) diag[$"Salida {++n}: {device}"] = detail;
        return diag;
    }

    string NameOfApp(long appid) => lastGames.FirstOrDefault(e => (long)e["appid"] == appid)?["name"] as string ?? appid.ToString();

    /// <summary>¿La salida de audio actual está usando Dolby Atmos (Configuración › Sonido › Audio espacial)?</summary>
    static bool AtmosActive()
    {
        try
        {
            string id = Windows.Media.Devices.MediaDevice.GetDefaultAudioRenderId(Windows.Media.Devices.AudioDeviceRole.Default);
            if (string.IsNullOrEmpty(id)) return false;
            var conf = Windows.Media.Audio.SpatialAudioDeviceConfiguration.GetForDeviceId(id);
            string f = conf?.ActiveSpatialAudioFormat ?? "";
            if (f.Length == 0) return false;
            return new[] { Windows.Media.Audio.SpatialAudioFormatSubtype.DolbyAtmosForHeadphones, Windows.Media.Audio.SpatialAudioFormatSubtype.DolbyAtmosForHomeTheater, Windows.Media.Audio.SpatialAudioFormatSubtype.DolbyAtmosForSpeakers }
                .Any(d => string.Equals(d, f, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) { Log.Error("Dolby Atmos (detectar)", ex); return false; }
    }

    static bool? dolby;
    /// <summary>¿La consola tiene Dolby Atmos (Dolby Access o el audio Dolby de ASUS)?</summary>
    static bool DolbyInstalled()
    {
        if (dolby != null) return dolby.Value;
        try
        {
            var pm = new Windows.Management.Deployment.PackageManager();
            dolby = pm.FindPackagesForUser("").Any(p => (p.Id.FamilyName ?? "").Contains("Dolby", StringComparison.OrdinalIgnoreCase));
            if (dolby == false)
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Dolby")) dolby = k != null;
            if (dolby == false)
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                    dolby = k?.GetSubKeyNames().Any(n => n.Contains("Dolby", StringComparison.OrdinalIgnoreCase)) == true;
            if (dolby == false)
            {
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                dolby = Directory.EnumerateFiles(sys, "dax*.dll").Any() || Directory.EnumerateFiles(Path.Combine(sys, "DriverStore", "FileRepository"), "*dolby*", new EnumerationOptions { RecurseSubdirectories = false }).Any()
                    || Directory.EnumerateDirectories(Path.Combine(sys, "DriverStore", "FileRepository"), "*dax*").Any();
            }
        }
        catch { dolby = false; }
        Log.Write("Dolby Atmos: " + (dolby == true ? "sí" : "no encontrado"));
        return dolby.Value;
    }

    static readonly HttpClient web2 = new() { Timeout = TimeSpan.FromSeconds(12) };
    static MainForm() { web2.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36 Edg/130.0"); }

    /// <summary>
    /// Ícono oficial de una app web (YouTube…): el que el sitio declara para instalarse como app (su manifest),
    /// o su apple-touch-icon. Es el mismo ícono que usaría Edge o Chrome al instalarla.
    /// </summary>
    async Task<string> AppIconAsync(long appid)
    {
        var a = apps.FirstOrDefault(x => x.AppId == appid);
        if (a == null) return null;
        var m = Regex.Match((a.Args ?? "") + " " + (a.Exec ?? ""), @"https?://[^\s""']+");
        if (!m.Success) return null;
        try
        {
            var site = new Uri(m.Value);
            var home = new Uri(site.GetLeftPart(UriPartial.Authority) + "/");
            string html = await web2.GetStringAsync(home);
            var candidates = new List<(string url, int size)>();

            var man = Regex.Match(html, @"<link[^>]+rel=[""']manifest[""'][^>]*>", RegexOptions.IgnoreCase);
            var href = man.Success ? Regex.Match(man.Value, @"href=[""']([^""']+)[""']", RegexOptions.IgnoreCase) : null;
            if (href != null && href.Success)
            {
                var manUrl = new Uri(home, System.Net.WebUtility.HtmlDecode(href.Groups[1].Value));
                using var d = JsonDocument.Parse(await web2.GetStringAsync(manUrl));
                if (d.RootElement.TryGetProperty("icons", out var icons))
                    foreach (var i in icons.EnumerateArray())
                    {
                        string purpose = i.TryGetProperty("purpose", out var pu) ? pu.GetString() ?? "" : "";
                        if (purpose.Contains("monochrome")) continue;
                        string src = i.TryGetProperty("src", out var s) ? s.GetString() : null;
                        if (string.IsNullOrEmpty(src)) continue;
                        int size = 0;
                        var sm = Regex.Match(i.TryGetProperty("sizes", out var sz) ? sz.GetString() ?? "" : "", @"(\d+)x\d+");
                        if (sm.Success) size = int.Parse(sm.Groups[1].Value);
                        if (purpose.Contains("maskable") && !purpose.Contains("any")) size /= 2;   // los "maskable" traen mucho margen
                        candidates.Add((new Uri(manUrl, src).ToString(), size));
                    }
            }
            foreach (Match t in Regex.Matches(html, @"<link[^>]+rel=[""']apple-touch-icon[^""']*[""'][^>]*>", RegexOptions.IgnoreCase))
            {
                var h = Regex.Match(t.Value, @"href=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                if (h.Success) candidates.Add((new Uri(home, System.Net.WebUtility.HtmlDecode(h.Groups[1].Value)).ToString(), 180));
            }
            foreach (Match t in Regex.Matches(html, @"<link[^>]+rel=[""'](?:shortcut )?icon[""'][^>]*>", RegexOptions.IgnoreCase))
            {
                var h = Regex.Match(t.Value, @"href=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                var sz = Regex.Match(t.Value, @"sizes=[""'](\d+)x", RegexOptions.IgnoreCase);
                if (h.Success) candidates.Add((new Uri(home, System.Net.WebUtility.HtmlDecode(h.Groups[1].Value)).ToString(), sz.Success ? int.Parse(sz.Groups[1].Value) : 32));
            }
            var best = candidates.Where(c => !c.url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) && c.size >= 96).OrderByDescending(c => c.size).FirstOrDefault();
            // Respaldo: el servicio de íconos de Google da el ícono más grande que tenga el sitio.
            if (best.url == null) best = ($"https://www.google.com/s2/favicons?domain={home.Host}&sz=256", 256);
            Log.Write($"ícono de {a.Name}: {best.url}");
            return await art.CacheAsync(best.url, "appicon");
        }
        catch (Exception ex)
        {
            Log.Error("ícono de " + a.Name, ex);
            try { return await art.CacheAsync($"https://www.google.com/s2/favicons?domain={new Uri(m.Value).Host}&sz=256", "appicon"); }
            catch { return null; }
        }
    }

    /// <summary>Copia la captura al portapapeles: como imagen (para pegarla en Discord / WhatsApp Web) y como archivo.</summary>
    bool CopyToClipboard(string path)
    {
        if (!album.IsAlbumFile(path)) return false;
        try
        {
            var data = new DataObject();
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { path });
            Image img = null;
            if (!Regex.IsMatch(path, @"\.(mp4|webm)$", RegexOptions.IgnoreCase))
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var tmp = Image.FromStream(fs);
                img = new Bitmap(tmp);
                data.SetImage(img);
            }
            Clipboard.SetDataObject(data, true);
            img?.Dispose();
            return true;
        }
        catch (Exception ex) { Log.Error("copiar captura", ex); return false; }
    }

    /// <summary>
    /// La primera vez que se comparte, Windows pregunta si deja que Scarlet reciba conexiones en tu red.
    /// Esa ventanita quedaría detrás de Scarlet, así que se trae al frente.
    /// </summary>
    async Task BringFirewallPromptAsync()
    {
        for (int i = 0; i < 12; i++)
        {
            await Task.Delay(500);
            foreach (var h in Native.TopWindows())
            {
                string t = Native.Title(h);
                if (t.Contains("Firewall", StringComparison.OrdinalIgnoreCase) || t.Contains("seguridad de Windows", StringComparison.OrdinalIgnoreCase) || t.Contains("Windows Security", StringComparison.OrdinalIgnoreCase))
                {
                    TopMost = false;
                    Native.ForceForeground(h);
                    Log.Write("compartir: Windows pide permiso de red: " + t);
                    return;
                }
            }
        }
    }
}
