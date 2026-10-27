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

    void Post(string ev)
    {
        try { web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new Dictionary<string, object> { ["event"] = ev, ["data"] = null })); }
        catch (Exception ex) { Log.Error("menú rápido", ex); }
    }

    /// <summary>Abre el panel encima de la ventana que estaba al frente (a la que se regresa al cerrar).</summary>
    public bool ShowOver(IntPtr front)
    {
        if (!ready) { Log.Write("menú rápido: todavía cargando"); return false; }
        returnTo = front;
        Place();
        IsOpen = true;
        Show();
        Native.SetWindowPos(Handle, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, 0x0001 | 0x0002 /* NOSIZE | NOMOVE */);
        Native.ForceForeground(Handle);
        web.Focus();
        _ = Reassert();
        Post("quick.open");
        Log.Write("menú rápido abierto");
        return true;
    }

    /// <summary>Algunos juegos recuperan el foco justo después; se vuelve a pedir un par de veces.</summary>
    async Task Reassert()
    {
        foreach (int ms in new[] { 120, 300, 700 })
        {
            await Task.Delay(ms);
            if (!IsOpen) return;
            if (Native.GetForegroundWindow() != Handle) { Native.ForceForeground(Handle); web.Focus(); Log.Write("menú rápido: el juego se quedó el foco, se recupera"); }
        }
    }

    /// <summary>Pide a la interfaz que se cierre con su animación (ella avisa con "quick.close" al terminar).</summary>
    public void RequestClose()
    {
        if (!IsOpen) return;
        Post("quick.close");
        // Por si la interfaz no contesta, se cierra de todos modos.
        _ = Task.Delay(700).ContinueWith(_ => { if (IsOpen) BeginInvoke(new Action(() => HideNow(true))); });
    }

    /// <summary>Oculta el panel ya; si se pide, regresa el foco a lo que estaba al frente (el juego).</summary>
    public void HideNow(bool giveBack)
    {
        if (!IsOpen) return;
        IsOpen = false;
        Hide();
        if (giveBack && returnTo != IntPtr.Zero && Native.IsWindow(returnTo)) Native.ForceForeground(returnTo);
    }

    // Antes se cerraba solo al perder el foco, y con algunos juegos eso pasaba en el mismo instante en que abría
    // ("se quiere abrir pero no lo hace"). Ahora solo se cierra con B, el botón Xbox o sus botones.
}
