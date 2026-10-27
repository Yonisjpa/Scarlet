namespace XboxScarlet;

/// <summary>
/// Sigue al juego que se lanzó: detecta cuando su ventana final ya ocupa la pantalla y lleva 2 s estable
/// (para quitar la pantalla de carga), lo trae al frente cuando vuelves, y avisa cuando se cierra.
/// </summary>
sealed class GameTracker
{
    readonly IntPtr own;
    readonly SteamService steam;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };

    public long AppId { get; private set; }
    public bool Active => AppId != 0;
    public bool IsReady { get; private set; }
    /// <summary>Segundos desde que se pidió abrir el juego (0 si ya está listo o no hay juego).</summary>
    public double LaunchSeconds => Active && !IsReady ? (DateTime.Now - start).TotalSeconds : 0;

    bool isSteam;
    string installDir;
    DateTime start, candSince, steamDialogSince;
    IntPtr cand, hwnd;
    Native.RECT candRect;
    uint pid;
    int deadTicks;
    bool releasedTopMost;

    public event Action<long> Ready, Failed, Exited;
    /// <summary>Pide quitar "siempre encima" a la pantalla de carga (un diálogo de Steam necesita verse).</summary>
    public event Action ReleaseTopMost;

    public GameTracker(IntPtr own, SteamService steam)
    {
        this.own = own;
        this.steam = steam;
        timer.Tick += (_, _) =>
        {
            try { if (!IsReady) LaunchTick(); else AliveTick(); }
            catch (Exception ex) { Log.Error("tracker", ex); }
        };
    }

    bool slow;   // juegos de Xbox: tardan como los de Steam, pero su .exe no siempre corre desde la carpeta del paquete

    public void Begin(long appid, bool steamGame, string dir, bool slowStart = false)
    {
        AppId = appid; isSteam = steamGame; installDir = dir; slow = slowStart;
        start = DateTime.Now; steamDialogSince = DateTime.MinValue;
        cand = hwnd = IntPtr.Zero; pid = 0; deadTicks = 0; IsReady = false; releasedTopMost = false;
        timer.Interval = 250;
        timer.Start();
        Log.Write($"lanzando {appid} (steam={steamGame}) {dir}");
    }

    /// <summary>Al abrir la app (por ejemplo, tras un reinicio) con un juego ya corriendo: se toma como "listo" sin pantalla de carga.</summary>
    public void Adopt(long appid, string dir)
    {
        AppId = appid; isSteam = true; installDir = dir;
        start = DateTime.Now; deadTicks = 0; releasedTopMost = true;
        hwnd = FindGameWindow();
        pid = 0;
        if (hwnd != IntPtr.Zero) Native.GetWindowThreadProcessId(hwnd, out pid);
        IsReady = true;
        timer.Interval = 1000;
        timer.Start();
        Log.Write($"juego ya abierto al iniciar: {appid} hwnd={hwnd}");
    }

    public void Stop()
    {
        timer.Stop();
        AppId = 0; IsReady = false; hwnd = IntPtr.Zero; pid = 0;
    }

    void Release()
    {
        if (releasedTopMost) return;
        releasedTopMost = true;
        ReleaseTopMost?.Invoke();
    }

    static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam.exe", "steamwebhelper.exe", "gameoverlayui.exe", "gameoverlayui64.exe", "explorer.exe", "xboxscarlet.exe", "scarlet.exe",
        "shellexperiencehost.exe", "searchhost.exe", "startmenuexperiencehost.exe", "textinputhost.exe", "applicationframehost.exe",
        "lockapp.exe", "armourycrate.exe", "armourycratese.exe", "gamebar.exe", "gamebarftserver.exe", "xboxpcapp.exe", "systemsettings.exe"
    };

    void LaunchTick()
    {
        double secs = (DateTime.Now - start).TotalSeconds;
        IntPtr fg = Native.GetForegroundWindow();
        if (fg != IntPtr.Zero && fg != own)
        {
            string path = Native.WindowProcessPath(fg, out uint fpid) ?? "";
            string exe = Path.GetFileName(path);

            // Si Steam muestra un diálogo (actualización, nube, EULA…), la pantalla negra no debe taparlo.
            bool steamUi = exe.Equals("steam.exe", StringComparison.OrdinalIgnoreCase) || exe.Equals("steamwebhelper.exe", StringComparison.OrdinalIgnoreCase);
            if (steamUi && !Covers(fg))
            {
                // Las ventanas de Steam las esconde MainForm; solo si después de 25 s sigue una (p. ej. pide aceptar algo) se deja ver.
                if (steamDialogSince == DateTime.MinValue) steamDialogSince = DateTime.Now;
            }
            else steamDialogSince = DateTime.MinValue;

            if (IsCandidate(fg, path, exe))
            {
                Native.GetWindowRect(fg, out var r);
                if (fg == cand && r.Equals(candRect))
                {
                    if (Covers(fg) && (DateTime.Now - candSince).TotalSeconds >= 2) { MarkReady(fg, fpid); return; }
                }
                else { cand = fg; candRect = r; candSince = DateTime.Now; }
            }
        }

        if (secs > 25) Release();   // tarda mucho: que se vea lo que esté pasando (un launcher, por ejemplo)

        bool hasDir = !string.IsNullOrEmpty(installDir);
        if (secs > (isSteam || hasDir || slow ? 60 : 15))
        {
            if (cand != IntPtr.Zero && Native.IsWindow(cand)) { Native.GetWindowThreadProcessId(cand, out uint cp); MarkReady(cand, cp); }
            else if (isSteam && steam.RunningAppId() == AppId) MarkReady(IntPtr.Zero, 0);
            else if (!isSteam && hasDir && AnyProcessIn(installDir)) MarkReady(IntPtr.Zero, 0);
            else { long id = AppId; Stop(); Log.Write($"no se detectó la ventana del juego {id}"); Failed?.Invoke(id); }
        }
    }

    bool IsCandidate(IntPtr h, string path, string exe)
    {
        if (!Native.IsWindowVisible(h) || Native.IsIconic(h) || Native.IsToolWindow(h)) return false;
        if (exe.Length > 0 && Excluded.Contains(exe)) return false;
        if (!isSteam) return string.IsNullOrEmpty(installDir) || (exe.Length > 0 && Paths.Under(path, installDir));   // apps / juegos de Xbox
        if (exe.Length > 0 && !string.IsNullOrEmpty(installDir) && Paths.Under(path, installDir)) return true;
        return steam.RunningAppId() == AppId;   // juegos con launcher propio o protegidos por anticheat
    }

    static bool Covers(IntPtr h)
    {
        if (!Native.GetWindowRect(h, out var r)) return false;
        var b = Screen.FromHandle(h).Bounds;
        long w = Math.Max(0, Math.Min(r.Right, b.Right) - Math.Max(r.Left, b.Left));
        long hh = Math.Max(0, Math.Min(r.Bottom, b.Bottom) - Math.Max(r.Top, b.Top));
        return w * hh >= 0.85 * b.Width * b.Height;
    }

    void MarkReady(IntPtr h, uint p)
    {
        hwnd = h; pid = p; IsReady = true; deadTicks = 0;
        timer.Interval = 1000;
        Log.Write($"juego listo {AppId} hwnd={h} pid={p}");
        Ready?.Invoke(AppId);
    }

    void AliveTick()
    {
        bool alive = (hwnd != IntPtr.Zero && Native.IsWindow(hwnd)) || (pid != 0 && ProcessAlive(pid));
        if (isSteam)
        {
            if (steam.RunningAppId() == AppId) alive = true;
            else if (!alive) alive = AnyProcessIn(installDir);
        }
        else if (!alive && !string.IsNullOrEmpty(installDir)) alive = AnyProcessIn(installDir);
        if (alive) { deadTicks = 0; return; }
        if (++deadTicks >= 3) { long id = AppId; Stop(); Log.Write($"juego cerrado {id}"); Exited?.Invoke(id); }
    }

    static bool ProcessAlive(uint id)
    {
        try
        {
            using var p = Process.GetProcessById((int)id);
            try { return !p.HasExited; } catch { return true; }
        }
        catch { return false; }
    }

    static bool AnyProcessIn(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return false;
        foreach (var p in Process.GetProcesses())
            using (p)
                if (Paths.Under(Native.ProcessPath((uint)p.Id), dir)) return true;
        return false;
    }

    IntPtr FindGameWindow()
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;
        foreach (var h in Native.TopWindows())
        {
            if (h == own) continue;
            string path = Native.WindowProcessPath(h, out uint wp);
            bool mine = (pid != 0 && wp == pid) || (isSteam && Paths.Under(path, installDir));
            if (!mine) continue;
            long a = Native.Area(h);
            if (a > bestArea) { bestArea = a; best = h; }
        }
        return best;
    }

    /// <summary>Trae el juego al frente. false si no encontró su ventana.</summary>
    public bool Focus()
    {
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd)) hwnd = FindGameWindow();
        if (hwnd == IntPtr.Zero) return false;
        Native.ForceForeground(hwnd);
        return true;
    }

    /// <summary>Le pide al juego que se cierre normal; si a los 6 s sigue abierto (y es de Steam), lo cierra a la fuerza.</summary>
    public void Close()
    {
        IntPtr h = hwnd != IntPtr.Zero ? hwnd : FindGameWindow();
        uint p = pid;
        string dir = installDir;
        bool st = isSteam;
        Stop();
        if (h != IntPtr.Zero && Native.IsWindow(h)) Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        if (!st && string.IsNullOrEmpty(dir)) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(6000);
            try
            {
                foreach (var proc in Process.GetProcesses())
                    using (proc)
                    {
                        bool match = (p != 0 && proc.Id == p) || Paths.Under(Native.ProcessPath((uint)proc.Id), dir);
                        if (match) try { proc.Kill(true); } catch { }
                    }
            }
            catch (Exception ex) { Log.Error("cerrar juego", ex); }
        });
    }
}
