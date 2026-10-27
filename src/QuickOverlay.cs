using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace XboxScarlet;

/// <summary>
/// Menú rápido (brillo, volumen, energía…) que se desliza por la derecha encima de lo que esté abierto, incluido un juego.
/// Es una ventana aparte con su propia vista web (la misma interfaz en modo "overlay"), para no tener que salir del juego.
/// </summary>
sealed class QuickOverlay : Form
{
    readonly WebView2 web = new() { Dock = DockStyle.Fill };
    readonly Action<string, CoreWebView2> onMessage;
    IntPtr returnTo;
    bool ready;

    public bool IsOpen { get; private set; }
    /// <summary>La ventana sobre la que se abrió (el juego).</summary>
    public IntPtr ReturnTo => returnTo;
    public CoreWebView2 View => ready ? web.CoreWebView2 : null;
    public event Action Hidden;

    public QuickOverlay(Action<string, CoreWebView2> onMessage)
    {
        this.onMessage = onMessage;
        Text = "Scarlet · menú rápido";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(43, 43, 47);
        web.DefaultBackgroundColor = BackColor;
        Controls.Add(web);
        Place();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080;   // TOOLWINDOW: no aparece en Alt+Tab ni en el selector de ventanas
            return cp;
        }
    }

    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public Native.RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)] struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    Rectangle want;   // dónde debe quedar el panel, en píxeles reales de la pantalla

    /// <summary>
    /// Panel a la derecha, del alto de la pantalla (mismo ancho que el menú rápido de la interfaz: 44rem a 1.5vh), en la
    /// pantalla donde está el juego. Se mide en píxeles reales: cuando un juego cambiaba la resolución, Windows reescalaba
    /// la ventana y el menú salía a pantalla completa. Nunca mide más de la mitad del ancho.
    /// </summary>
    void Place(IntPtr over = default)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        IntPtr mon = MonitorFromWindow(over != IntPtr.Zero ? over : IsHandleCreated ? Handle : IntPtr.Zero, 1 /* MONITOR_DEFAULTTOPRIMARY */);
        Rectangle b = mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi)
            ? Rectangle.FromLTRB(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom)
            : Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        int w = Math.Min((int)Math.Round(b.Height * 0.66), b.Width / 2);
        want = new Rectangle(b.Right - w, b.Top, w, b.Height);
        if (IsHandleCreated) Native.SetWindowPos(Handle, new IntPtr(-1) /* TOPMOST */, want.X, want.Y, want.Width, want.Height, 0x0010 /* NOACTIVATE */);
        else Bounds = want;
    }

    /// <summary>Si algo intenta agrandar o mover el panel (cambio de escala de Windows, el juego), se queda en su lugar.</summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x02E0 /* WM_DPICHANGED */) { BeginInvoke(new Action(() => Place())); return; }   // no aceptar el tamaño que propone Windows
        if (m.Msg == 0x0046 /* WM_WINDOWPOSCHANGING */ && !want.IsEmpty)
        {
            var wp = Marshal.PtrToStructure<WINDOWPOS>(m.LParam);
            bool sizing = (wp.flags & 0x0001 /* NOSIZE */) == 0, moving = (wp.flags & 0x0002 /* NOMOVE */) == 0;
            if ((sizing && (wp.cx != want.Width || wp.cy != want.Height)) || (moving && (wp.x != want.X || wp.y != want.Y)))
            {
                wp.x = want.X; wp.y = want.Y; wp.cx = want.Width; wp.cy = want.Height;
                wp.flags &= ~0x0003u;
                Marshal.StructureToPtr(wp, m.LParam, false);
            }
        }
        base.WndProc(ref m);
    }

    /// <summary>Se prepara oculto al arrancar, para que abra al instante.</summary>
    public async Task InitAsync(CoreWebView2Environment env, string webFolder, bool devTools, string stamp = "", EventHandler<CoreWebView2WebResourceRequestedEventArgs> serveFiles = null)
    {
        CreateControl();
        await web.EnsureCoreWebView2Async(env);
        var cw = web.CoreWebView2;
        cw.Settings.AreDefaultContextMenusEnabled = devTools;
        cw.Settings.AreDevToolsEnabled = devTools;
        cw.Settings.AreBrowserAcceleratorKeysEnabled = devTools;
        cw.Settings.IsZoomControlEnabled = false;
        cw.Settings.IsStatusBarEnabled = false;
        cw.Settings.IsPinchZoomEnabled = false;
        cw.Settings.IsSwipeNavigationEnabled = false;
        cw.SetVirtualHostNameToFolderMapping("app.scarlet", webFolder, CoreWebView2HostResourceAccessKind.Allow);
        cw.WebMessageReceived += (_, e) => onMessage(e.WebMessageAsJson, cw);
        // Imágenes guardadas (carátulas, íconos, avatar): sin esto, encima del juego no cargaba ninguna.
        if (serveFiles != null)
        {
            cw.AddWebResourceRequestedFilter("https://files.scarlet/*", CoreWebView2WebResourceContext.All);
            cw.WebResourceRequested += serveFiles;
        }
        cw.ProcessFailed += (_, e) => { Log.Write("menú rápido: WebView2 falló " + e.ProcessFailedKind); try { cw.Reload(); } catch { } };
        cw.NavigationCompleted += (_, _) =>
        {
            ready = true;
            if (!IsOpen) SetMemory(false);
            if (reopen && IsOpen) { reopen = false; Post("quick.open"); Log.Write("menú rápido: recargado, se vuelve a abrir"); }
        };
        cw.Navigate("https://app.scarlet/index.html?overlay=quick&v=" + Uri.EscapeDataString(stamp));
    }

    void Post(string ev, object data = null)
    {
        try { web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new Dictionary<string, object> { ["event"] = ev, ["data"] = data })); }
        catch (Exception ex) { Log.Error("menú rápido", ex); }
    }

    /// <summary>¿El juego se quedó con el foco? Entonces los botones del mando le llegan al menú por la app (ver XInputService.ForwardKeys).</summary>
    public bool FocusLost { get; private set; }

    /// <summary>Se cierra con su animación; mientras tanto se ignoran más toques del botón Xbox.</summary>
    public bool IsClosing { get; private set; }

    /// <summary>Abre el panel encima de la ventana que estaba al frente (a la que se regresa al cerrar).</summary>
    bool keepFocus;
    protected override bool ShowWithoutActivation => keepFocus;

    /// <summary>
    /// keepFocus: el menú se dibuja encima sin quitarle el foco al juego (el juego sigue sonando y no se pausa;
    /// los botones le llegan al menú por la app, aunque el juego también los recibe).
    /// </summary>
    // Juegos que le quitan el foco al menú (Zenless Zone Zero): pelear por él los hacía parpadear en negro.
    // Con ellos el menú se abre encima sin quitarles el foco y se maneja con los botones que le pasa la app.
    static readonly HashSet<string> grabbers = new(StringComparer.OrdinalIgnoreCase);
    static string ExeOf(IntPtr h) => Path.GetFileNameWithoutExtension(Native.WindowProcessPath(h, out _) ?? "");

    public bool ShowOver(IntPtr front, bool keepFocus = false)
    {
        if (!ready) { Log.Write("menú rápido: todavía cargando"); return false; }
        if (!keepFocus && grabbers.Contains(ExeOf(front))) keepFocus = true;
        returnTo = front;
        Place(front);
        SetMemory(true);
        IsOpen = true; IsClosing = false; FocusLost = false; drawn = false;
        this.keepFocus = keepFocus;
        Native.SetNoActivate(Handle, keepFocus);
        if (keepFocus)
        {
            Show();
            Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /* NOSIZE | NOMOVE | NOACTIVATE */);
            FocusLost = true;
            Post("quick.open");
            Log.Write($"menú rápido abierto sobre {Native.Describe(front)} (sin quitarle el foco)");
            _ = CheckDrawn();
            _ = CheckSize();
            return true;
        }
        _ = Task.Run(() => Log.Write("audio al abrir el menú: " + AudioMixer.Describe()));
        Show();
        Native.SetWindowPos(Handle, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, 0x0001 | 0x0002 /* NOSIZE | NOMOVE */);
        bool got = Native.ForceForeground(Handle);
        web.Focus();
        Post("quick.open");
        Log.Write($"menú rápido abierto sobre {Native.Describe(front)}{(got ? "" : " (sin foco todavía)")}");
        _ = Reassert();
        _ = CheckDrawn();
        _ = CheckSize();
        return true;
    }

    /// <summary>
    /// Algunos juegos recuperan el foco justo después; se pide de nuevo un par de veces y, si el juego insiste,
    /// ya no se pelea: el menú sigue funcionando con los botones que le pasa la app.
    /// </summary>
    async Task Reassert()
    {
        foreach (int ms in new[] { 120, 300, 700 })
        {
            await Task.Delay(ms);
            if (!IsOpen) return;
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == Handle) continue;
            // El juego recuperó el foco: ya no se pelea (no se le vuelve a quitar). El menú sigue encima y responde
            // con los botones que le pasa la app; la próxima vez, con este juego se abre sin quitarle el foco.
            FocusLost = true;
            string exe = ExeOf(fg);
            if (exe.Length > 0 && grabbers.Add(exe))
                Log.Write($"menú rápido: {Native.Describe(fg)} recupera el foco; desde ahora el menú se abre sin quitárselo{(Native.ExclusiveFullscreen() ? " (pantalla completa exclusiva)" : "")}");
            Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // que siga encima
            return;
        }
    }

    bool drawn, reopen, stale;

    /// <summary>Cerrado, el menú usa la menor memoria posible (se nota mientras juegas).</summary>
    void SetMemory(bool open)
    {
        try { if (web.CoreWebView2 != null) web.CoreWebView2.MemoryUsageTargetLevel = open ? CoreWebView2MemoryUsageTargetLevel.Normal : CoreWebView2MemoryUsageTargetLevel.Low; }
        catch { }
    }

    /// <summary>Revisa que haya quedado como panel; si no, lo corrige y lo anota en el log.</summary>
    async Task CheckSize()
    {
        foreach (int ms in new[] { 150, 700 })
        {
            await Task.Delay(ms);
            if (!IsOpen) return;
            Native.GetWindowRect(Handle, out var r);
            var now = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (now == want) continue;
            Log.Write($"menú rápido: quedó de {now.Width}×{now.Height} en vez de {want.Width}×{want.Height}; se corrige");
            Place(returnTo);
        }
    }

    /// <summary>La interfaz avisa que ya pintó el menú.</summary>
    public void Ack() => drawn = true;

    /// <summary>
    /// Si el menú no se dibuja en 1 s (pasó al abrirlo mientras un juego cambiaba de resolución), se recarga solo
    /// y se vuelve a abrir.
    /// </summary>
    async Task CheckDrawn()
    {
        await Task.Delay(1000);
        if (!IsOpen || drawn) return;
        Log.Write("menú rápido: no se dibujó; se recarga");
        Reload(true);
    }

    void Reload(bool openAfter)
    {
        try { reopen = openAfter; ready = false; web.CoreWebView2?.Reload(); }
        catch (Exception ex) { Log.Error("menú rápido (recargar)", ex); ready = true; }
    }

    /// <summary>
    /// Cambió la pantalla (resolución o modo, p. ej. al arrancar un juego): el menú se vuelve a preparar para que la
    /// siguiente vez abra bien. Si está abierto, se hace al cerrarse.
    /// </summary>
    public void Refresh(string why)
    {
        if (!ready && !stale) return;
        if (IsOpen) { stale = true; return; }
        stale = false;
        Log.Write("menú rápido: se prepara de nuevo (" + why + ")");
        Place();
        Reload(false);
    }

    /// <summary>Un botón del mando que llegó por la app (el menú no tiene el foco).</summary>
    public void SendKey(string key) { if (IsOpen && !IsClosing) Post("pad.key", key); }

    /// <summary>Pide a la interfaz que se cierre con su animación (ella avisa con "quick.close" al terminar).</summary>
    public void RequestClose(string why)
    {
        if (!IsOpen || IsClosing) return;
        IsClosing = true;
        closeReason = why;
        Post("quick.close");
        // Por si la interfaz no contesta, se cierra de todos modos.
        _ = Task.Delay(700).ContinueWith(_ => { if (IsOpen) BeginInvoke(new Action(() => HideNow(true, why + ", sin respuesta de la interfaz"))); });
    }
    string closeReason;

    /// <summary>Oculta el panel ya; si se pide, regresa el foco a lo que estaba al frente (el juego).</summary>
    public void HideNow(bool giveBack, string why = null)
    {
        if (!IsOpen) return;
        IsOpen = false; IsClosing = false; FocusLost = false;
        Hide();
        SetMemory(false);
        try { Hidden?.Invoke(); } catch { }
        Log.Write($"menú rápido cerrado ({why ?? closeReason ?? "interfaz"})");
        closeReason = null;
        if (stale) Refresh("pendiente");
        if (giveBack && returnTo != IntPtr.Zero && Native.IsWindow(returnTo)) Native.ForceForeground(returnTo);
    }

    // Antes se cerraba solo al perder el foco, y con algunos juegos eso pasaba en el mismo instante en que abría
    // ("se quiere abrir pero no lo hace"). Ahora solo se cierra con B, el botón Xbox o sus botones.
}
