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

    /// <summary>Panel a la derecha, del alto de la pantalla (mismo ancho que el menú rápido de la interfaz: 44rem a 1.5vh).</summary>
    void Place()
    {
        var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        int w = (int)Math.Round(b.Height * 0.66);
        Bounds = new Rectangle(b.Right - w, b.Top, w, b.Height);
    }

    /// <summary>Se prepara oculto al arrancar, para que abra al instante.</summary>
    public async Task InitAsync(CoreWebView2Environment env, string webFolder, bool devTools)
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
        cw.ProcessFailed += (_, e) => { Log.Write("menú rápido: WebView2 falló " + e.ProcessFailedKind); try { cw.Reload(); } catch { } };
        cw.NavigationCompleted += (_, _) => ready = true;
        cw.Navigate("https://app.scarlet/index.html?overlay=quick");
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
    public bool ShowOver(IntPtr front)
    {
        if (!ready) { Log.Write("menú rápido: todavía cargando"); return false; }
        returnTo = front;
        Place();
        IsOpen = true; IsClosing = false; FocusLost = false;
        Show();
        Native.SetWindowPos(Handle, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, 0x0001 | 0x0002 /* NOSIZE | NOMOVE */);
        bool got = Native.ForceForeground(Handle);
        web.Focus();
        Post("quick.open");
        Log.Write($"menú rápido abierto sobre {Native.Describe(front)}{(got ? "" : " (sin foco todavía)")}");
        _ = Reassert();
        return true;
    }

    /// <summary>
    /// Algunos juegos recuperan el foco justo después; se pide de nuevo un par de veces y, si el juego insiste,
    /// ya no se pelea: el menú sigue funcionando con los botones que le pasa la app.
    /// </summary>
    async Task Reassert()
    {
        int tries = 0;
        foreach (int ms in new[] { 120, 300, 700 })
        {
            await Task.Delay(ms);
            if (!IsOpen) return;
            if (Native.GetForegroundWindow() == Handle) continue;
            tries++;
            if (Native.ForceForeground(Handle)) web.Focus();
        }
        if (!IsOpen) return;
        FocusLost = Native.GetForegroundWindow() != Handle;
        if (tries > 0) Log.Write(FocusLost
            ? $"menú rápido: {Native.Describe(Native.GetForegroundWindow())} no suelta el foco; el menú se maneja por la app{(Native.ExclusiveFullscreen() ? " (pantalla completa exclusiva)" : "")}"
            : $"menú rápido: el juego tomó el foco {tries} vez/veces, se recuperó");
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
        try { Hidden?.Invoke(); } catch { }
        Log.Write($"menú rápido cerrado ({why ?? closeReason ?? "interfaz"})");
        closeReason = null;
        if (giveBack && returnTo != IntPtr.Zero && Native.IsWindow(returnTo)) Native.ForceForeground(returnTo);
    }

    // Antes se cerraba solo al perder el foco, y con algunos juegos eso pasaba en el mismo instante en que abría
    // ("se quiere abrir pero no lo hace"). Ahora solo se cierra con B, el botón Xbox o sus botones.
}
