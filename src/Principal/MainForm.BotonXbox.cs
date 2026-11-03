using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace XboxScarlet;

/// <summary>Botón Xbox: un toque, doble toque, mantener y volver a Scarlet.</summary>
sealed partial class MainForm
{
    // ───────────────────────────── Botón Xbox ─────────────────────────────

    int guideActions;
    IntPtr beforeGuide;

    // Freno: abrir y cerrar ventanas más rápido de lo que se alcanzan a dibujar llegó a tumbar la app (log-4, 16:31).
    const int GUIDE_GAP_MS = 300;
    readonly Stopwatch guideClock = Stopwatch.StartNew();
    long lastGuideAt = -10_000;

    bool GuideTooSoon(string what)
    {
        long now = guideClock.ElapsedMilliseconds;
        if (now - lastGuideAt < GUIDE_GAP_MS) { Log.Write($"botón Xbox: {what} ignorado (muy seguido)"); return true; }
        lastGuideAt = now;
        return false;
    }

    /// <summary>1 toque: menú rápido. Con la interfaz al frente se usa el suyo; con cualquier otra cosa (un juego), el panel encima.</summary>
    void OnGuideSingle()
    {
        guideActions++;
        if (quick != null && quick.IsClosing) { Log.Write("botón Xbox: toque ignorado (el menú se está cerrando)"); return; }
        if (GuideTooSoon("toque")) return;
        if (quick != null && quick.IsOpen) { quick.RequestClose("botón Xbox"); return; }
        if (GameHung) { AskAboutHungGame(); return; }
        // Con el juego cargando, el panel encima salía en blanco (el juego cambia la pantalla en ese momento):
        // se abre solo en cuanto el juego ya esté en pantalla.
        if (tracker != null && tracker.Active && !tracker.IsReady)
        {
            if (quickPending) { quickPending = false; Log.Write("toque: ya no se abre el menú al cargar"); return; }
            quickPending = true;
            Log.Write("toque: el juego está cargando; el menú se abre cuando esté listo");
            ToastForm.Pop("Menú rápido", "Se abrirá en cuanto cargue el juego", "≡", null, null);
            return;
        }
        var fg = Native.GetForegroundWindow();
        if (fg == Handle && web.Visible) { Log.Write("toque: menú rápido de la interfaz"); Emit("quick.toggle", null); return; }
        // Juegos en pantalla completa exclusiva (encima no se puede dibujar): el menú se abre en la pantalla de Scarlet;
        // el juego se congela y al cerrarlo regresas a él.
        if (tracker != null && tracker.Active && tracker.IsReady && cfg.FullMenu.Contains(tracker.AppId))
        {
            Log.Write("toque: menú rápido en la pantalla de Scarlet (juego en pantalla completa exclusiva)");
            BringUiToFront(); Emit("quick.toggle", null); return;
        }
        if (IsGameBar(fg)) fg = beforeGuide;
        // Sin juego abierto y con Steam (Big Picture que se abrió solo con el botón Xbox) o el escritorio al frente: el menú es
        // el de Scarlet, no el panel encima (antes se abría sobre "explorer.exe «»" y el menú quedaba raro).
        if ((tracker == null || !tracker.Active) && StrayShell(fg))
        {
            Log.Write($"toque: {Native.Describe(fg)} al frente sin juego; menú rápido de la interfaz");
            if (BigPictureWindow() != IntPtr.Zero) { lastBpClose = DateTime.Now; Shell.Open("steam://close/bigpicture"); }
            BringUiToFront(mute: false); Emit("quick.toggle", null); return;
        }
        bool keepFocus = tracker != null && tracker.Active && cfg.MenuKeepsFocus.Contains(tracker.AppId);
        if (quick != null && quick.ShowOver(fg, keepFocus)) { xin.ForwardKeys = true; if (!keepFocus) WarnExclusive(); return; }
        Log.Write("toque: menú rápido en la interfaz (el panel no está listo)");
        BringUiToFront(mute: false); Emit("quick.toggle", null);
    }

    bool quickPending;

    /// <summary>Ventanas que no son algo que tú abriste: Steam / Big Picture, el escritorio o la barra de tareas.</summary>
    static bool StrayShell(IntPtr h)
    {
        if (h == IntPtr.Zero) return true;
        string exe = Path.GetFileName(Native.WindowProcessPath(h, out _) ?? "").ToLowerInvariant();
        if (exe is "steam.exe" or "steamwebhelper.exe" or "shellhost.exe" or "shellexperiencehost.exe" or "searchhost.exe" or "startmenuexperiencehost.exe") return true;
        return exe == "explorer.exe" && string.IsNullOrWhiteSpace(Native.Title(h));   // escritorio / barra de tareas (no una carpeta)
    }

    void OnGuideDouble()
    {
        quickPending = false;
        if (GameHung) { guideActions++; AskAboutHungGame(); return; }
        guideActions++;
        if (GuideTooSoon("doble toque")) return;
        if (quick != null && quick.IsOpen) quick.HideNow(false, "doble toque");
        var fg = Native.GetForegroundWindow();
        bool hidden = fg != Handle || !web.Visible;
        Log.Write(hidden ? $"doble toque: al Inicio desde {Native.Describe(fg)}" : "doble toque: en la interfaz");
        if (hidden) BringUiToFront();
        Emit("guide.double", new { hidden });
    }

    /// <summary>Mantener: el selector de ventanas activas del modo Xbox (el mismo de Windows).</summary>
    void OnGuideHold()
    {
        guideActions++;
        if (GuideTooSoon("mantener")) return;
        if (quick != null && quick.IsOpen) quick.HideNow(false, "botón Xbox mantenido");
        Log.Write("botón Xbox mantenido: ventanas activas");
        Shell.Open("shell:::{3080F90E-D7AD-11D9-BD98-0000947B0257}");
    }

    /// <summary>
    /// Trae la interfaz al frente. Si el juego no suelta la pantalla (le pasa a algunos en pantalla completa exclusiva),
    /// se minimiza el juego, igual que haría Alt+Tab; al volver se restaura solo.
    /// </summary>
    async void BringUiToFront(bool mute = true)
    {
        IntPtr game = Native.GetForegroundWindow();
        if (quick != null && game == quick.Handle) game = quick.ReturnTo;
        // Silenciar el juego: por su proceso (aunque al frente estuviera el menú rápido); la música sigue sonando.
        if (mute) MuteGameBehind(game);
        ShowUi();
        await Task.Delay(150);
        if (Native.GetForegroundWindow() == Handle) { PauseGameIfMarked(); return; }
        // El juego no suelta la pantalla: primero se insiste sin tocarlo (minimizarlo dejaba a algunos juegos en ventana
        // o en negro). Solo en pantalla completa exclusiva, donde no hay otra forma, se minimiza.
        TopMost = true;
        bool ok = false;
        for (int i = 0; i < 3 && !ok; i++) { ok = Native.ForceForeground(Handle); if (!ok) await Task.Delay(120); }
        web.Focus();
        IntPtr now = Native.GetForegroundWindow();
        if (!ok && now != Handle && Native.ExclusiveFullscreen())
        {
            Log.Write($"Scarlet no pudo pasar al frente: {Native.Describe(now)} está en pantalla completa exclusiva; se minimiza");
            WarnExclusive();
            if (now != IntPtr.Zero && !(quick != null && now == quick.Handle)) Native.Minimize(now);
            else if (game != IntPtr.Zero && game != Handle) Native.Minimize(game);
            await Task.Delay(120);
            ok = Native.ForceForeground(Handle);
            web.Focus();
        }
        await Task.Delay(400);
        TopMost = false;   // solo un instante: luego el juego debe poder volver al frente
        if (Native.GetForegroundWindow() == Handle) PauseGameIfMarked();   // congelar solo si Scarlet de verdad quedó al frente
        else Log.Write($"Scarlet sigue detrás de {Native.Describe(Native.GetForegroundWindow())}");
    }

    /// <summary>Una vez por juego: si está en pantalla completa exclusiva, se avisa que conviene "ventana sin bordes".</summary>
    readonly HashSet<long> warnedExclusive = new();
    void WarnExclusive()
    {
        if (tracker == null || !tracker.Active || !Native.ExclusiveFullscreen() || !warnedExclusive.Add(tracker.AppId)) return;
        Log.Write($"el juego {tracker.AppId} está en pantalla completa exclusiva");
        Emit("game.exclusive", new { appid = tracker.AppId });
    }

    /// <summary>
    /// Si pediste quitarle el botón Xbox a la Game Bar y aun así se abre (Windows a veces tarda en aplicar el ajuste),
    /// se cierra en cuanto aparece y regresa lo que tenías al frente.
    /// </summary>
    async void OnGuideDown()
    {
        var fg = Native.GetForegroundWindow();
        if (!IsGameBar(fg)) beforeGuide = fg;
        if (cfg.GameBarNexusBackup == null) return;   // la Game Bar sigue siendo tuya: no se toca
        for (int t = 0; t < 15; t++)
        {
            await Task.Delay(100);
            if (!IsGameBar(Native.GetForegroundWindow())) continue;
            Log.Write("game bar: se abrió con el botón Xbox; se cierra");
            if (quick != null && quick.IsOpen) { Native.ForceForeground(quick.Handle); return; }
            IntPtr back = beforeGuide != IntPtr.Zero && Native.IsWindow(beforeGuide) ? beforeGuide : Handle;
            if (back == Handle) ShowUi(); else Native.ForceForeground(back);
            return;
        }
    }

    static bool IsGameBar(IntPtr h)
    {
        if (h == IntPtr.Zero) return false;
        string exe = Path.GetFileNameWithoutExtension(Native.WindowProcessPath(h, out _) ?? "");
        return exe.StartsWith("GameBar", StringComparison.OrdinalIgnoreCase) || exe.StartsWith("XboxGameBar", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Otra copia de la app (Game Bar › Inicio, AnyFSE, un acceso directo) pidió mostrar el menú.</summary>
    public void ShowFromOtherInstance() => Ui(() =>
    {
        bool hidden = Native.GetForegroundWindow() != Handle || !web.Visible;
        if (hidden && tracker != null && tracker.Active && tracker.IsReady) MuteGameBehind(Native.GetForegroundWindow());
        ShowUi();
        if (hidden && tracker != null && tracker.Active) Emit("guide.double", new { hidden = true });
    });
}
