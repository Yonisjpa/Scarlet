using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace XboxScarlet;

/// <summary>Ventanas: tiempo de juego, apps junto al juego, foco, mostrar y esconder Scarlet, ventanitas de Steam.</summary>
sealed partial class MainForm
{
    // ───── Tiempo de juego (Xbox y apps) ─────
    string playKey;
    DateTime playStart;

    double Hours(string key) => cfg.PlaySeconds.TryGetValue(key, out var s) ? Math.Round(s / 3600.0, 1) : 0.0;

    /// <summary>Suma el rato jugado desde la última vez (se llama al cerrar, al suspender y al pausar el juego).</summary>
    void FlushPlay()
    {
        if (playKey == null || playStart == default) return;
        long secs = (long)(DateTime.UtcNow - playStart).TotalSeconds;
        playStart = default;
        if (secs <= 0 || secs > 24 * 3600) return;
        cfg.PlaySeconds[playKey] = (cfg.PlaySeconds.TryGetValue(playKey, out var s) ? s : 0) + secs;
        cfg.Save();
    }

    /// <summary>
    /// Como en la consola: al ir al Inicio el juego se congela (no gasta batería ni suena) y sigue donde estaba al volver.
    /// Todos los juegos, menos los que apagues en sus propiedades (juegos en línea). Las apps (música, YouTube) no.
    /// </summary>
    void PauseGameIfMarked()
    {
        if (tracker == null || !tracker.Active || !tracker.IsReady || GameFreezer.IsFrozen || cfg.NoPauseOnHome.Contains(tracker.AppId)) return;
        var g = lastGames.FirstOrDefault(e => (long)e["appid"] == tracker.AppId);
        if (g == null || (g.TryGetValue("kind", out var k) && (k as string) == "app")) return;
        if (freezeFailed == tracker.AppId) return;   // ya falló una vez (p. ej. un antitrampas no deja): no se insiste
        uint pid = tracker.Pid;
        if ((pid == 0 || !ProcessAlive(pid)) && tracker.Window != IntPtr.Zero) Native.GetWindowThreadProcessId(tracker.Window, out pid);
        if (pid == 0 || !ProcessAlive(pid)) pid = GameProcess(IntPtr.Zero, AudioMixer.Sessions()).pid;
        if (pid == 0) return;
        if (GameFreezer.Suspend(pid, tracker.AppId)) FlushPlay();
        else freezeFailed = tracker.AppId;
    }
    long freezeFailed;
    int borderlessFixes;

    // ───── Apps abiertas junto al juego (Apple Music, YouTube para ver una guía…) ─────
    long sideAppId; IntPtr sideWindow; string sideExe; DateTime sideSince;

    bool IsSideApp(IntPtr h)
    {
        if (sideAppId == 0 || h == IntPtr.Zero || h == Handle) return false;
        if (h == sideWindow) return true;
        string exe = Path.GetFileNameWithoutExtension(Native.WindowProcessPath(h, out _) ?? "");
        return sideExe != null && exe.Equals(sideExe, StringComparison.OrdinalIgnoreCase);
    }

    bool IsGameWindow(IntPtr h)
    {
        if (tracker == null || !tracker.Active || h == IntPtr.Zero) return false;
        if (h == tracker.Window) return true;
        string path = Native.WindowProcessPath(h, out uint pid);
        return (tracker.Pid != 0 && pid == tracker.Pid) || (!string.IsNullOrEmpty(tracker.InstallDir) && Paths.Under(path ?? "", tracker.InstallDir));
    }

    /// <summary>
    /// Abre una app (música, video) sin cerrar el juego: el juego se queda congelado y callado detrás, con su "Jugando",
    /// y regresas a él desde el Inicio. Si la app ya estaba abierta, solo se trae al frente.
    /// </summary>
    async Task OpenAlongsideAsync(long appid, string source)
    {
        if (sideAppId == appid && sideWindow != IntPtr.Zero && Native.IsWindow(sideWindow))
        {
            TopMost = false; Native.ForceForeground(sideWindow); HideSoon(); return;
        }
        MuteGameBehind(IntPtr.Zero);
        PauseGameIfMarked();
        var before = Native.TopWindows().ToHashSet();
        string exe = null, dir = null;
        TopMost = false;
        Native.AllowAnyForeground();
        if (source == "xbox")
        {
            var g = xbox.Find(appid) ?? throw new InvalidOperationException("No encontré esa app de Xbox");
            cfg.AppLastUsed["xbox:" + g.Pfn] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            dir = g.Dir;
            xbox.Launch(g);
        }
        else
        {
            var a = apps.FirstOrDefault(x => x.AppId == appid) ?? throw new InvalidOperationException("No encontré esa app en apps.json");
            cfg.AppLastUsed[a.Id ?? a.Name ?? ""] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            exe = Path.GetFileNameWithoutExtension(a.Exec ?? "");
            a.Start();
        }
        cfg.Save();
        sideAppId = appid; detachedApp = appid; sideWindow = IntPtr.Zero; sideExe = exe; sideSince = DateTime.Now;
        Log.Write($"abriendo {appid} junto al juego {tracker?.AppId}");
        for (int i = 0; i < 40; i++)   // hasta 10 s: su ventana nueva (o la que ya tenía, si estaba abierta)
        {
            await Task.Delay(250);
            foreach (var h in Native.TopWindows())
            {
                if (h == Handle || Native.Area(h) < 200_000) continue;
                string path = Native.WindowProcessPath(h, out _) ?? "";
                string name = Path.GetFileNameWithoutExtension(path);
                bool mine = (dir != null && Paths.Under(path, dir)) || (exe != null && name.Equals(exe, StringComparison.OrdinalIgnoreCase)) || (!before.Contains(h) && !NotApps.Contains(name) && !IsGameWindow(h));
                if (!mine) continue;
                sideWindow = h; sideExe = name;
                Native.ForceForeground(h);
                Log.Write($"app junto al juego: {Native.Describe(h)}");
                HideSoon();
                return;
            }
        }
        Log.Write("app junto al juego: no apareció su ventana");
    }

    /// <summary>Si cerraste la app de al lado, la interfaz le quita su "Sonando" / "Jugando".</summary>
    void CheckSideApp()
    {
        if (sideAppId == 0) return;
        // Con su ventana conocida se mira esa (Edge sigue abierto aunque cierres YouTube); si no, su programa.
        bool alive = sideWindow != IntPtr.Zero ? Native.WindowOpen(sideWindow)
                     : sideExe != null && Process.GetProcessesByName(sideExe).Any(p => { using (p) return p.MainWindowHandle != IntPtr.Zero; });
        if (alive || DateTime.Now - sideSince < TimeSpan.FromSeconds(15)) return;
        Log.Write($"la app {sideAppId} que estaba junto al juego se cerró");
        Emit("app.closed", new { appid = sideAppId });
        sideAppId = 0; sideWindow = IntPtr.Zero; sideExe = null;
    }

    static bool ProcessAlive(uint id) { try { using var p = Process.GetProcessById((int)id); return !p.HasExited; } catch { return false; } }

    /// <summary>Volver al juego (o a lo que estaba abierto antes del menú).</summary>
    void FocusGame()
    {
        if (GameFreezer.Resume()) playStart = DateTime.UtcNow;
        AudioFocus.RestoreAll();
        if (tracker.Active && tracker.Focus()) { HideSoon(); return; }
        // Apple Music y otras apps de la Store que escondieron su ventana (siguen sonando): se vuelven a abrir y Windows
        // trae la ventana de la que ya corre (no se abre otra).
        if (tracker.Active && xbox.Find(tracker.AppId) is XboxGame xg)
        {
            Log.Write($"{xg.Name} no tiene ventana a la vista: se le pide a Windows que la muestre");
            Native.AllowAnyForeground();
            try { xbox.Launch(xg); } catch (Exception ex) { Log.Error("mostrar app", ex); }
            _ = Task.Delay(1500).ContinueWith(_ => Ui(() => { if (tracker.Active && tracker.Focus()) HideSoon(); }));
            return;
        }
        if (lastForeign != IntPtr.Zero && Native.IsWindow(lastForeign)) { Native.ForceForeground(lastForeign); return; }
        Native.SendToBottom(Handle);
    }

    /// <summary>Con el juego al frente, la interfaz se oculta para no gastar batería.</summary>
    /// <summary>
    /// Con el juego al frente, la interfaz se esconde: así su página se marca «oculta» y deja de dibujar, animar y
    /// leer el mando (beta 1.2: se reintenta unos segundos si el juego tarda en tomar el frente).
    /// </summary>
    async void HideSoon()
    {
        for (int i = 0; i < 12; i++)
        {
            await Task.Delay(i == 0 ? 600 : 1000);
            if (Native.GetForegroundWindow() != Handle) { web.Visible = false; return; }
            if (tracker == null || !tracker.Active) return;
        }
    }

    void ShowUi()
    {
        steamUiAllowed = false;   // de regreso en Scarlet: Steam vuelve a esconderse
        var fg = Native.GetForegroundWindow();
        if (fg != IntPtr.Zero && fg != Handle) lastForeign = fg;
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        if (Perf.EcoOn) Perf.Eco(false, ScarletPids(), "de regreso en Scarlet");   // la interfaz responde al instante
        if (wasGaming) MainMemory(true);
        web.Visible = true;
        Native.ForceForeground(Handle);
        web.Focus();
    }

    /// <summary>
    /// Instalar / desinstalar: Steam pide confirmar en una ventanita. Solo esa se deja ver (una ventana NUEVA de Steam,
    /// más chica que la pantalla, en los 90 s siguientes); la ventana principal de Steam y Big Picture siguen escondidas.
    /// Antes se volvían a mostrar todas las ventanas de Steam escondidas y por eso apareció Steam completo.
    /// </summary>
    async Task SteamDialogAsync(string uri, string what)
    {
        GuardSteam(0);   // aquí sí se quiere ver la ventanita de Steam
        steamBefore = new HashSet<IntPtr>();
        Native.EnumWindows((h, lp) => { steamBefore.Add(h); return true; }, IntPtr.Zero);
        steamDialogUntil = DateTime.Now.AddSeconds(90);
        Log.Write($"Steam: {what} ({uri}); se espera su ventanita de confirmación");
        TopMost = false;
        Shell.Open(uri);
        for (int i = 0; i < 50; i++)   // hasta ~10 s
        {
            await Task.Delay(200);
            IntPtr dlg = IntPtr.Zero;
            Native.EnumWindows((h, lp) => { if (Native.IsWindowVisible(h) && SteamDialogAllowed(h)) { dlg = h; return false; } return true; }, IntPtr.Zero);
            if (dlg == IntPtr.Zero)   // quizá el guardián ya la había escondido: se busca también entre las escondidas
                Native.EnumWindows((h, lp) => { if (SteamDialogAllowed(h) && Native.Title(h).Length > 0) { dlg = h; return false; } return true; }, IntPtr.Zero);
            if (dlg != IntPtr.Zero)
            {
                steamDialogs.Add(dlg);
                Native.ShowWindowAsync(dlg, 5 /* SW_SHOW */);
                Native.ForceForeground(dlg);
                Log.Write($"Steam: ventanita para {what}: «{Native.Title(dlg)}»");
                return;
            }
        }
        Log.Write($"Steam: no apareció la ventanita para {what}");
    }

    HashSet<IntPtr> steamBefore = new();
    readonly HashSet<IntPtr> steamDialogs = new();
    DateTime steamDialogUntil = DateTime.MinValue;

    /// <summary>¿Es la ventanita de confirmación de Steam que pediste (nueva, chica, no Big Picture)?</summary>
    bool SteamDialogAllowed(IntPtr h)
    {
        if (steamDialogs.Contains(h)) return true;
        if (DateTime.Now > steamDialogUntil) return false;
        string exe = Path.GetFileName(Native.WindowProcessPath(h, out _) ?? "");
        if (!exe.Equals("steamwebhelper.exe", StringComparison.OrdinalIgnoreCase) && !exe.Equals("steam.exe", StringComparison.OrdinalIgnoreCase)) return false;
        string t = Native.Title(h);
        if (t.Contains("Big Picture", StringComparison.OrdinalIgnoreCase) || t.Contains("amigos", StringComparison.OrdinalIgnoreCase) || t.Contains("Friends", StringComparison.OrdinalIgnoreCase)) return false;
        // Las ventanitas de confirmación son chicas; la ventana principal de Steam es grande (más de un cuarto de la pantalla).
        long screen = (long)Screen.PrimaryScreen.Bounds.Width * Screen.PrimaryScreen.Bounds.Height;
        long a = Native.Area(h);
        bool isNew = !steamBefore.Contains(h);
        return a >= 20_000 && a < screen * (isNew ? 45 : 25) / 100;
    }

    async Task OpenSystemAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return;
        if (uri.StartsWith("steam://install/", StringComparison.OrdinalIgnoreCase)) { await SteamDialogAsync(uri, "instalar"); return; }
        if (uri == "explorer") { Shell.Open("explorer.exe"); return; }
        if (uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)) uri = "steam://openurl/" + uri;   // en el navegador de Steam (Big Picture)
        // Tienda, noticias y controles se abren en Big Picture; se abre solo cuando hace falta y se cierra al lanzar un juego.
        bool needsBigPicture = Regex.IsMatch(uri, @"^steam://(store|openurl|controllerconfig|nav|url|open/(news|friends|bigpicture))", RegexOptions.IgnoreCase);
        if (needsBigPicture) steamUiAllowed = true;
        bool justBp = uri.Equals("steam://open/bigpicture", StringComparison.OrdinalIgnoreCase);   // el ícono de Steam (beta 1.4)
        if (needsBigPicture && BigPictureWindow() == IntPtr.Zero)
        {
            Shell.Open("steam://open/bigpicture");
            for (int i = 0; i < 40 && BigPictureWindow() == IntPtr.Zero; i++) await Task.Delay(150);
            if (justBp) return;
            await Task.Delay(800);
        }
        Shell.Open(uri);
    }
}
