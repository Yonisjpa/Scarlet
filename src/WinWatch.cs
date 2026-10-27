namespace XboxScarlet;

/// <summary>
/// Vigilantes de ventanas (aparece una ventana de Steam, se cierra la del juego) en un hilo propio que solo apunta y
/// avisa. Antes vivían en el hilo de la pantalla de Scarlet: si ese hilo estaba ocupado, el juego que generaba el aviso
/// podía quedarse esperando, y Scarlet a su vez esperaba al juego (congelones de varios segundos en Stellar Blade).
/// Aquí el hilo no hace nada más que recibir avisos, así que nunca hace esperar a nadie.
/// </summary>
static class WinWatch
{
    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint msg, IntPtr w, IntPtr l);

    const uint OUTOFCONTEXT = 0x0000, SKIPOWNPROCESS = 0x0002, WM_APP_WATCH = 0x8001;

    /// <summary>Se mostró una ventana de Steam (se avisa en el hilo de la pantalla, sin prisa).</summary>
    public static event Action<IntPtr> SteamShown;
    /// <summary>Se cerró una ventana del proceso que se está vigilando (el juego).</summary>
    public static event Action<IntPtr> Destroyed;

    static SynchronizationContext ui;
    static uint threadId;
    static WinEventProc showProc, destroyProc;   // se guardan para que no los recoja el recolector de basura
    static IntPtr destroyHook;
    static volatile uint watchPid, hookedPid;
    static readonly HashSet<uint> steamPids = new();
    static DateTime steamPidsAt = DateTime.MinValue;

    public static void Start(SynchronizationContext uiContext)
    {
        ui = uiContext;
        var t = new Thread(Loop) { IsBackground = true, Name = "Scarlet · vigilante de ventanas" };
        t.Start();
    }

    /// <summary>Vigila que se cierre la ventana de este proceso (0 = ninguno).</summary>
    public static void WatchProcess(uint pid)
    {
        if (pid == watchPid) return;
        watchPid = pid;
        if (threadId != 0) PostThreadMessage(threadId, WM_APP_WATCH, IntPtr.Zero, IntPtr.Zero);
    }

    static void Loop()
    {
        try
        {
            threadId = Native.GetCurrentThreadId();
            showProc = OnShow;
            destroyProc = OnDestroy;
            if (SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, showProc, 0, 0, OUTOFCONTEXT | SKIPOWNPROCESS) == IntPtr.Zero) Log.Write("no se pudo vigilar ventanas nuevas");
            Rewatch();
            while (GetMessage(out var m, IntPtr.Zero, 0, 0) > 0)
            {
                if (m.message == WM_APP_WATCH && m.hwnd == IntPtr.Zero) { Rewatch(); continue; }
                TranslateMessage(ref m);
                DispatchMessage(ref m);
            }
        }
        catch (Exception ex) { Log.Error("vigilante de ventanas", ex); }
    }

    /// <summary>El aviso de "ventana cerrada" solo para el proceso del juego, no para todo Windows.</summary>
    static void Rewatch()
    {
        uint pid = watchPid;
        if (pid == hookedPid) return;
        if (destroyHook != IntPtr.Zero) { UnhookWinEvent(destroyHook); destroyHook = IntPtr.Zero; }
        hookedPid = pid;
        if (pid != 0) destroyHook = SetWinEventHook(0x8001, 0x8001, IntPtr.Zero, destroyProc, pid, 0, OUTOFCONTEXT);
    }

    static void OnShow(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || hwnd == IntPtr.Zero) return;
        try
        {
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            // Steam recién abierto (o reiniciado) tiene procesos nuevos: si la ventana no es de uno conocido, se vuelve a
            // revisar la lista (a lo más una vez por segundo).
            if (DateTime.Now - steamPidsAt > TimeSpan.FromSeconds(5) || (!steamPids.Contains(pid) && DateTime.Now - steamPidsAt > TimeSpan.FromSeconds(1)))
            {
                steamPidsAt = DateTime.Now; steamPids.Clear();
                foreach (var n in new[] { "steam", "steamwebhelper" })
                    foreach (var p in Process.GetProcessesByName(n)) using (p) steamPids.Add((uint)p.Id);
            }
            if (!steamPids.Contains(pid)) return;   // solo interesan las de Steam: lo demás ni se manda a la pantalla
            ui?.Post(_ => { try { SteamShown?.Invoke(hwnd); } catch (Exception ex) { Log.Error("ventana de Steam", ex); } }, null);
        }
        catch { }
    }

    static void OnDestroy(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || hwnd == IntPtr.Zero) return;
        ui?.Post(_ => { try { Destroyed?.Invoke(hwnd); } catch (Exception ex) { Log.Error("ventana cerrada", ex); } }, null);
    }
}
