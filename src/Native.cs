namespace XboxScarlet;

/// <summary>Funciones de Windows (user32, kernel32, powrprof) que .NET no trae directo.</summary>
static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr hObject);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);
    [DllImport("powrprof.dll")] public static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);
    [DllImport("powrprof.dll")] public static extern uint PowerGetEffectiveOverlayScheme(out Guid effectiveOverlayGuid);

    const int SW_RESTORE = 9;
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WM_CLOSE = 0x0010;

    /// <summary>Ruta completa del .exe de un proceso (funciona aunque el proceso sea de otro usuario o esté protegido, si Windows lo permite).</summary>
    public static string ProcessPath(uint pid)
    {
        IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    public static string WindowProcessPath(IntPtr hWnd, out uint pid)
    {
        GetWindowThreadProcessId(hWnd, out pid);
        return pid == 0 ? null : ProcessPath(pid);
    }

    public static bool IsToolWindow(IntPtr hWnd) => (GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0;

    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int dwProcessId);
    [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

    /// <summary>
    /// Trae una ventana al frente aunque otra app (un juego) tenga el foco. Devuelve si lo consiguió.
    /// Se intenta por pasos, del más suave al más insistente, para no molestar cuando no hace falta.
    /// </summary>
    public static bool ForceForeground(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return false;
        if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);
        IntPtr fg = GetForegroundWindow();
        if (fg == hWnd) return true;
        AllowSetForegroundWindow(-1 /* ASFW_ANY */);

        // 1) Compartir la entrada con la ventana del frente: Windows deja cambiar el foco.
        uint fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
        uint me = GetCurrentThreadId();
        bool attached = false;
        try
        {
            attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
        }
        finally { if (attached) AttachThreadInput(me, fgThread, false); }
        if (GetForegroundWindow() == hWnd) return true;

        // 2) Una pulsación de Alt hace que Windows considere que hubo entrada del usuario.
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 0x0002, UIntPtr.Zero);
        BringWindowToTop(hWnd);
        SetForegroundWindow(hWnd);
        if (GetForegroundWindow() == hWnd) return true;

        // 3) Lo mismo que hace Alt+Tab.
        SwitchToThisWindow(hWnd, true);
        return GetForegroundWindow() == hWnd;
    }

    /// <summary>Deja que la app que se va a abrir tome el foco (Scarlet está al frente y le "presta" el permiso).</summary>
    public static void AllowAnyForeground() { try { AllowSetForegroundWindow(-1); } catch { } }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    /// <summary>Ventana que no se activa al tocarla ni al mostrarse (WS_EX_NOACTIVATE).</summary>
    public static void SetNoActivate(IntPtr hWnd, bool on)
    {
        long ex = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
        long want = on ? ex | 0x08000000L : ex & ~0x08000000L;
        if (want != ex) SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(want));
    }

    public static void Minimize(IntPtr hWnd) { if (hWnd != IntPtr.Zero && IsWindow(hWnd)) ShowWindow(hWnd, 6 /* SW_MINIMIZE */); }

    /// <summary>¿Hay un juego en pantalla completa exclusiva? Ahí Windows no deja poner nada encima de forma estable.</summary>
    public static bool ExclusiveFullscreen()
    {
        try { return SHQueryUserNotificationState(out int s) == 0 && s == 3 /* QUNS_RUNNING_D3D_FULL_SCREEN */; }
        catch { return false; }
    }

    /// <summary>"juego.exe «Título»" para el log.</summary>
    public static string Describe(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return "(nada)";
        string exe = Path.GetFileName(WindowProcessPath(hWnd, out _) ?? "?");
        return $"{exe} «{Title(hWnd)}»";
    }

    /// <summary>Manda la ventana al fondo sin activarla (deja ver lo que haya detrás).</summary>
    public static void SendToBottom(IntPtr hWnd) =>
        SetWindowPos(hWnd, new IntPtr(1) /* HWND_BOTTOM */, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>Ventanas visibles de nivel superior.</summary>
    public static List<IntPtr> TopWindows()
    {
        var list = new List<IntPtr>();
        EnumWindows((h, _) => { if (IsWindowVisible(h) && GetWindow(h, 4 /* GW_OWNER */) == IntPtr.Zero && !IsToolWindow(h)) list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }

    public static string Title(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static long Area(IntPtr hWnd) =>
        GetWindowRect(hWnd, out var r) ? Math.Max(0L, r.Right - r.Left) * Math.Max(0L, r.Bottom - r.Top) : 0;
}
