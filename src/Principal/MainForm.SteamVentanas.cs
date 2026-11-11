using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace XboxScarlet;

/// <summary>Steam sin ventanas: abrirlo en silencio, esconder lo que aparece, sin internet, reconectar y Big Picture.</summary>
sealed partial class MainForm
{
    // ───── Sin ventanas de Steam (inmersión) ─────

    /// <summary>Si Steam no está abierto, se abre ya mismo en silencio (sin ventana), para que al lanzar un juego no aparezca.</summary>
    void StartSteamSilently()
    {
        try
        {
            if (Process.GetProcessesByName("steam").Length > 0 || steam.SteamPath == null) return;
            string exe = Path.Combine(steam.SteamPath, "steam.exe");
            if (!File.Exists(exe)) return;
            Process.Start(new ProcessStartInfo(exe, "-silent") { UseShellExecute = true });
            Log.Write("Steam abierto en silencio");
        }
        catch (Exception ex) { Log.Error("abrir Steam", ex); }
    }

    readonly HashSet<IntPtr> hiddenSteam = new();

    /// <summary>
    /// Esconde cualquier ventana de Steam (biblioteca, "preparando para jugar", ofertas, notificaciones, Big Picture…).
    /// Excepciones: cuando tú abres la tienda o el chat, y si un juego lleva más de 25 s arrancando con una ventana de Steam
    /// (normalmente porque pide aceptar algo), para que no se quede atorado.
    /// </summary>
    void HideSteamWindows()
    {
        if (steamUiAllowed) return;
        bool stuckLaunch = tracker != null && tracker.LaunchSeconds > 25;
        try
        {
            Native.EnumWindows((h, lp) =>
            {
                if (!Native.IsWindowVisible(h) || Native.Area(h) < 2500) return true;
                string exe = Path.GetFileName(Native.WindowProcessPath(h, out _) ?? "");
                if (!exe.Equals("steam.exe", StringComparison.OrdinalIgnoreCase) && !exe.Equals("steamwebhelper.exe", StringComparison.OrdinalIgnoreCase)) return true;
                if (stuckLaunch || SteamDialogAllowed(h) || OfflinePrompt(h)) return true;
                Native.ShowWindowAsync(h, 0 /* SW_HIDE */);
                if (hiddenSteam.Add(h)) Log.Write($"ventana de Steam escondida: \"{Native.Title(h)}\"");
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) { Log.Error("ventanas de Steam", ex); }
    }

    /// <summary>El puntero se ve solo donde hace falta (Explorador, Ajustes de Windows, Administrador de tareas); en lo demás, escondido.</summary>
    void PointerForFront()
    {
        if (!cfg.HideCursor) { if (CursorHider.Hidden) CursorHider.Restore(); return; }
        var fg = Native.GetForegroundWindow();
        string exe = Path.GetFileNameWithoutExtension(Native.WindowProcessPath(fg, out _) ?? "");
        // Solo ventanas de verdad del Explorador (carpetas) y herramientas de Windows; el escritorio que asoma un instante, no.
        bool explorerWindow = exe.Equals("explorer", StringComparison.OrdinalIgnoreCase) && Native.ClassName(fg) is "CabinetWClass" or "ExploreWClass";
        bool needs = exe is "SystemSettings" or "mmc" or "control" or "Taskmgr" or "regedit" or "notepad" || explorerWindow;
        if (needs && CursorHider.Hidden) CursorHider.Restore();
        else if (!needs && (!CursorHider.Hidden || slowTicks % 10 == 0)) CursorHider.Hide(force: true);   // cada ~30 s se reafirma
    }

    // ───── Ventanas de Steam: se esconden en el instante en que aparecen ("Iniciando juego…", avisos) ─────
    // Los avisos llegan del vigilante de ventanas (WinWatch), que vive en su propio hilo.
    void StartWindowHook()
    {
        WinWatch.SteamShown += OnWindowShown;
        WinWatch.Destroyed += h =>
        {
            if (tracker != null && tracker.Active && h == tracker.Window) tracker.WindowDestroyed(h);
        };
        WinWatch.Start(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
    }

    // ───── Sin internet ─────
    readonly HashSet<IntPtr> offlinePrompts = new();

    /// <summary>
    /// Steam abierto sin internet pregunta "¿Iniciar en modo sin conexión?". Esa ventanita NO se esconde (si se escondía,
    /// Steam se quedaba esperando y ningún juego abría): se deja al frente y se avisa qué tocar.
    /// </summary>
    readonly DateTime bornAt = DateTime.Now;
    bool offlineWaited, offlineWaitArmed;

    bool OfflinePrompt(IntPtr h)
    {
        if (NetCheck.Internet || h == IntPtr.Zero) return false;
        if (offlinePrompts.Contains(h)) return true;
        // 1.6.1: recién encendida, el Wi-Fi tarda 2–5 s en conectarse: Steam preguntaba «sin conexión» y su ventana tapaba
        // a Scarlet un instante. Se espera ~8 s; si sigue sin internet, entonces sí se muestra.
        if (DateTime.Now - bornAt < TimeSpan.FromSeconds(40) && !offlineWaited)
        {
            if (!offlineWaitArmed)
            {
                offlineWaitArmed = true;
                Log.Write("sin internet al encender: se esperan unos segundos antes de mostrar el aviso de Steam");
                _ = Task.Delay(8000).ContinueWith(_ => Ui(() =>
                {
                    offlineWaited = true;
                    if (NetCheck.Internet) { Log.Write("ya hay internet: el aviso de Steam no hizo falta"); return; }
                    foreach (var w in hiddenSteam.ToList())
                        if (Native.IsWindow(w) && OfflinePrompt(w)) { Native.ShowWindowAsync(w, 5 /* SW_SHOW */); Native.ForceForeground(w); }
                }));
            }
            return false;
        }
        if (SteamUptime() > TimeSpan.FromMinutes(3)) return false;
        string exe = Path.GetFileName(Native.WindowProcessPath(h, out _) ?? "");
        if (!exe.Equals("steamwebhelper.exe", StringComparison.OrdinalIgnoreCase) && !exe.Equals("steam.exe", StringComparison.OrdinalIgnoreCase)) return false;
        string t = Native.Title(h);
        if (t.Contains("Big Picture", StringComparison.OrdinalIgnoreCase) || t.Contains("amigos", StringComparison.OrdinalIgnoreCase) || t.Contains("Friends", StringComparison.OrdinalIgnoreCase)) return false;
        long screen = (long)Screen.PrimaryScreen.Bounds.Width * Screen.PrimaryScreen.Bounds.Height, a = Native.Area(h);
        if (a < 20_000 || a >= screen * 45 / 100) return false;
        offlinePrompts.Add(h);
        Log.Write($"sin internet: Steam pregunta algo («{t}»); se deja ver");
        ToastForm.Pop("Steam sin conexión", "Toca «Iniciar en modo sin conexión» para jugar lo instalado", "!", "#f2c230", null);
        Emit("steam.offline", new { title = t });
        return true;
    }

    // ───── Steam se reconecta ─────
    // Steam abierto sin internet (o con la red bloqueándolo) se queda "sin conexión" aunque después llegue el internet
    // (por ejemplo, al prender la VPN): no deja instalar ni ver amigos. Por eso se reinicia desde cero.
    bool reconnecting, reconnectPending;
    DateTime badSince = DateTime.MinValue, lastReconnect = DateTime.MinValue;

    async Task ReconnectSteamAsync(string why, bool force = false)
    {
        if (reconnecting || steam.SteamPath == null) return;
        if (!force && DateTime.Now - lastReconnect < TimeSpan.FromMinutes(3)) return;
        if (DateTime.Now - lastReconnect < TimeSpan.FromSeconds(45)) return;   // ni forzado: dos reinicios seguidos no
        if (!SteamCdp.SteamRunning) { reconnectPending = false; Ui(StartSteamSilently); return; }
        if (tracker != null && tracker.Active)   // reiniciar Steam cerraría el juego: se hace al salir de él
        {
            if (!reconnectPending) { Log.Write($"Steam: se reconectará al cerrar el juego ({why})"); Emit("steam.reconnect", new { state = "later" }); }
            reconnectPending = true;
            return;
        }
        if (installGuard.Enabled || enabling) { reconnectPending = true; return; }   // a media instalación, no
        reconnecting = true; reconnectPending = false; lastReconnect = DateTime.Now; badSince = DateTime.MinValue;
        Log.Write($"Steam: se reinicia para reconectarse ({why})");
        Emit("steam.reconnect", new { state = "restarting" });
        bool ok = false;
        try { ok = await SteamCdp.RestartSteamAsync(steam.SteamPath, () => Ui(StartSteamSilently)); }
        catch (Exception ex) { Log.Error("Steam: reiniciar", ex); }
        reconnecting = false;
        Log.Write("Steam: " + (ok ? "reiniciado y en línea" : "no terminó de reiniciarse"));
        Emit("steam.reconnect", new { state = ok ? "done" : "failed" });
        _ = NetCheck.CheckAsync();
        if (ok) _ = Task.Delay(4000).ContinueWith(_ => Emit("library.changed", new { }));   // juegos y amigos al día
    }

    void OnNetChanged()
    {
        EmitAll("net.state", new { internet = NetCheck.Internet, steam = NetCheck.SteamReachable });
        bool good = NetCheck.Internet && NetCheck.SteamReachable;
        if (!good) { if (badSince == DateTime.MinValue) badSince = DateTime.Now; }
        else if (badSince != DateTime.MinValue)
        {
            var bad = DateTime.Now - badSince; badSince = DateTime.MinValue;
            // Regresó la conexión tras un rato sin ella (VPN prendida desde su app, otra red): Steam se reinicia.
            // Un tropiezo corto no: Steam se reconecta solo.
            if (bad > TimeSpan.FromSeconds(40)) _ = ReconnectSteamAsync($"regresó la conexión tras {(int)bad.TotalSeconds} s");
        }
        if (NetCheck.Internet) return;
        Ui(() =>   // se fue el internet con Steam arrancando: la ventanita que ya se había escondido vuelve a verse
        {
            foreach (var h in hiddenSteam.ToList())
                if (Native.IsWindow(h) && OfflinePrompt(h)) { Native.ShowWindowAsync(h, 5 /* SW_SHOW */); Native.ForceForeground(h); }
        });
    }

    void OnWindowShown(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || steamUiAllowed || !Native.IsWindow(hwnd)) return;
        try
        {
            if (OfflinePrompt(hwnd)) { Native.ForceForeground(hwnd); return; }
            if (tracker != null && tracker.LaunchSeconds > 25) return;   // un juego atorado pidiendo algo en Steam: que se vea
            if (Native.Area(hwnd) < 2500) return;
            if (SteamDialogAllowed(hwnd)) { steamDialogs.Add(hwnd); Native.ForceForeground(hwnd); return; }
            Native.ShowWindowAsync(hwnd, 0 /* SW_HIDE */);
            if (hiddenSteam.Add(hwnd)) Log.Write($"ventana de Steam escondida al aparecer: \"{Native.Title(hwnd)}\"");
        }
        catch { }
    }

    DateTime lastBpClose = DateTime.MinValue;
    bool guideFocusTried;
    readonly List<DateTime> bpCloses = new();
    DateTime bpHideUntil = DateTime.MinValue;
    bool bpLoopTold;

    /// <summary>
    /// Big Picture escondido seguía corriendo detrás (con sus sonidos al navegar). Si tú no lo abriste (tienda, chat),
    /// se cierra de verdad. Steam sigue abierto en silencio para lanzar juegos, logros y amigos.
    /// </summary>
    void CloseStrayBigPicture()
    {
        if (steamUiAllowed || DateTime.Now - lastBpClose < TimeSpan.FromSeconds(4)) return;
        if (tracker != null && tracker.LaunchSeconds > 0 && tracker.LaunchSeconds < 25) return;   // Steam está arrancando un juego
        IntPtr bp = IntPtr.Zero;
        try
        {
            Native.EnumWindows((h, lp) =>
            {
                if (Native.Title(h).IndexOf("Big Picture", StringComparison.OrdinalIgnoreCase) < 0) return true;
                string exe = Path.GetFileName(Native.WindowProcessPath(h, out _) ?? "");
                if (exe.Equals("steamwebhelper.exe", StringComparison.OrdinalIgnoreCase) || exe.Equals("steam.exe", StringComparison.OrdinalIgnoreCase)) { bp = h; return false; }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        if (bp == IntPtr.Zero) return;
        // 1.6.1: al encender, Steam volvía a abrir Big Picture cada vez que se cerraba (cada ~6 s): la pantalla
        // parpadeaba. Si se reabre 2 veces en un minuto, se deja abierto pero escondido y Scarlet se queda al frente;
        // se vuelve a intentar cerrarlo pasado un minuto y medio.
        bpCloses.RemoveAll(t => DateTime.Now - t > TimeSpan.FromSeconds(60));
        if (DateTime.Now < bpHideUntil || bpCloses.Count >= 2)
        {
            if (DateTime.Now >= bpHideUntil)
            {
                bpHideUntil = DateTime.Now.AddSeconds(90); bpCloses.Clear();
                Log.Write("Big Picture se vuelve a abrir solo: se deja escondido en vez de cerrarlo (1:30 min)");
                if (!bpLoopTold) { bpLoopTold = true; Emit("steam.guidebp", new { changed = false, loop = true }); }
            }
            if (Native.IsWindowVisible(bp)) { Native.ShowWindowAsync(bp, 0 /* SW_HIDE */); hiddenSteam.Add(bp); }
            if ((tracker == null || !tracker.Active) && Native.GetForegroundWindow() != Handle) ShowUi();
            lastBpClose = DateTime.Now;
            return;
        }
        bpCloses.Add(DateTime.Now);
        lastBpClose = DateTime.Now;
        // ¿Lo abrió el botón Xbox? Steam tiene «El botón Guía enfoca Steam» prendido: se intenta apagar y se avisa una vez.
        bool byGuide = guideClock.ElapsedMilliseconds - lastGuideAt < 4000;
        Log.Write($"Big Picture abierto sin que lo pidieras{(byGuide ? " (justo después del botón Xbox)" : "")}: se cierra");
        Shell.Open("steam://close/bigpicture");
        if (byGuide && !guideFocusTried)
        {
            guideFocusTried = true;
            _ = Task.Run(async () =>
            {
                var (changed, found) = await SteamCdp.GuideFocusOffAsync();
                Log.Write($"Steam: «el botón Guía enfoca Steam» → {(changed ? "apagado desde Scarlet" : "no se pudo cambiar desde aquí")} (funciones: {(string.IsNullOrEmpty(found) ? "ninguna" : found)})");
                Emit("steam.guidebp", new { changed });
            });
        }
    }

    /// <summary>Ventana de Steam Big Picture, si está abierta.</summary>
    static IntPtr BigPictureWindow()
    {
        foreach (var h in Native.TopWindows())
        {
            if (Native.Title(h).IndexOf("Big Picture", StringComparison.OrdinalIgnoreCase) < 0) continue;
            string exe = Path.GetFileName(Native.WindowProcessPath(h, out _) ?? "");
            if (exe.Equals("steamwebhelper.exe", StringComparison.OrdinalIgnoreCase) || exe.Equals("steam.exe", StringComparison.OrdinalIgnoreCase)) return h;
        }
        return IntPtr.Zero;
    }

    async Task CloseBigPictureAsync()
    {
        if (BigPictureWindow() == IntPtr.Zero) return;
        Log.Write("cerrando Big Picture antes de lanzar el juego");
        Shell.Open("steam://close/bigpicture");
        for (int i = 0; i < 20 && BigPictureWindow() != IntPtr.Zero; i++) await Task.Delay(150);
        Log.Write(BigPictureWindow() == IntPtr.Zero ? "Big Picture cerrado" : "Big Picture sigue abierto (se lanza el juego de todos modos)");
        ShowUi();
    }
}
