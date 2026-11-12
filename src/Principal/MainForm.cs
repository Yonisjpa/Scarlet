using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace XboxScarlet;

/// <summary>
/// Ventana a pantalla completa con la interfaz (WebView2). Recibe los mensajes de la interfaz
/// (window.chrome.webview.postMessage) y le contesta / le manda eventos.
/// </summary>
sealed partial class MainForm : Form
{
    readonly Config cfg;
    readonly bool startup;
    readonly WebView2 web = new() { Dock = DockStyle.Fill };
    CoreWebView2Environment env;

    readonly SteamService steam;
    readonly ArtCache art;
    readonly PadService pads;
    readonly AlbumService album;
    readonly XboxService xbox;
    DateTime xboxScanned = DateTime.MinValue;
    readonly SystemService sys = new();
    readonly XInputService xin = new();
    readonly MediaService media;
    readonly ShareServer share = new();
    readonly List<AppEntry> apps;
    GameTracker tracker;
    QuickOverlay quick;

    readonly System.Windows.Forms.Timer slow = new() { Interval = 3000 };
    readonly System.Windows.Forms.Timer friendsTimer = new() { Interval = 30000 };
    readonly System.Windows.Forms.Timer steamGuard = new() { Interval = 400 };
    bool steamUiAllowed;   // solo cuando tú abres la tienda / el chat de Steam
    int slowTicks;
    bool achKicked;
    IntPtr lastForeign;
    List<Dictionary<string, object>> lastGames = new();

    static readonly JsonSerializerOptions J = new() { PropertyNamingPolicy = null, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    readonly bool updated;

    public MainForm(Config cfg, bool startup, bool updated = false)
    {
        this.cfg = cfg;
        this.startup = startup;
        this.updated = updated;
        steam = new SteamService(cfg);
        art = new ArtCache(cfg, steam);
        pads = new PadService(cfg);
        album = new AlbumService(steam);
        xbox = new XboxService(art);
        media = new MediaService(art);
        AudioFocus.RecoverAfterCrash();
        if (cfg.HideCursor) CursorHider.Hide();
        GameFreezer.RecoverAfterCrash();
        apps = AppEntry.Load();

        Text = "Scarlet";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = Screen.PrimaryScreen.Bounds;
        BackColor = Color.Black;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath); } catch { }

        web.DefaultBackgroundColor = Color.Black;
        Controls.Add(web);

        Load += async (_, _) => await InitAsync();
        // Si algo externo trae la ventana al frente con un juego abierto (p. ej. un botón de la Ally mapeado a "Inicio" en AnyFSE), se va al menú.
        Activated += (_, _) =>
        {
            if (tracker != null && tracker.Active && tracker.IsReady && !web.Visible)
            {
                web.Visible = true;
                MuteGameBehind(lastForeign);
                Emit("guide.double", new { hidden = true });
            }
        };
        FormClosed += (_, _) =>
        {
            xin.Dispose();
            SystemEvents.PowerModeChanged -= OnPower;
            SystemEvents.DisplaySettingsChanged -= OnDisplay;
        };
    }

    // ───────────────────────────── Arranque ─────────────────────────────

    async Task InitAsync()
    {
        try
        {
            tracker = new GameTracker(Handle, steam);
            tracker.Ready += OnGameReady;
            tracker.Failed += id => { TopMost = false; Emit("game.failed", new { appid = id }); };
            tracker.Exited += OnGameExited;
            tracker.ReleaseTopMost += () => TopMost = false;
            // En cuanto el juego abre su ventana, la pantalla de carga deja de taparlo: si no, algunos juegos (RE) no logran
            // pasar a pantalla completa y se quedan en ventana o en negro.
            tracker.WindowSeen += () =>
            {
                if (TopMost) { TopMost = false; Log.Write("pantalla de carga: deja pasar al juego"); }
                if (cfg.Borderless.Contains(tracker.AppId)) Compat.MakeBorderless(tracker.Candidate);   // juegos sin "ventana sin bordes"
            };
            try
            {
                long running = steam.RunningAppId();   // ¿se reinició la app con un juego abierto?
                if (running != 0) { tracker.Adopt(running, steam.InstallDirOf(running)); steam.SetRunning(running); }
            }
            catch (Exception ex) { Log.Error("juego abierto", ex); }

            var opts = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
            env = await CoreWebView2Environment.CreateAsync(null, Paths.WebData, opts);
            await web.EnsureCoreWebView2Async(env);
            var cw = web.CoreWebView2;
            var s = cw.Settings;
            s.AreDefaultContextMenusEnabled = cfg.DevTools;
            s.AreDevToolsEnabled = cfg.DevTools;
            s.AreBrowserAcceleratorKeysEnabled = cfg.DevTools;
            s.IsZoomControlEnabled = false;
            s.IsStatusBarEnabled = false;
            s.IsPinchZoomEnabled = false;
            s.IsSwipeNavigationEnabled = false;

            cw.SetVirtualHostNameToFolderMapping("app.scarlet", Paths.Web, CoreWebView2HostResourceAccessKind.Allow);
            cw.AddWebResourceRequestedFilter("https://files.scarlet/*", CoreWebView2WebResourceContext.All);
            cw.WebResourceRequested += ServeFile;
            cw.WebMessageReceived += (_, e) => OnMessage(e.WebMessageAsJson, cw);
            cw.ProcessFailed += (_, e) =>
            {
                Log.Write($"WebView2 falló: {e.ProcessFailedKind} ({e.Reason}, código {e.ExitCode})");
                if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                    cw.Reload();
                else if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                {
                    // Sin su proceso principal la interfaz ya no responde: se abre una copia nueva y esta se cierra.
                    Program.Restart("WebView2 se cerró");
                    BeginInvoke(new Action(Close));
                }
            };
            // La vista web guarda en caché index.html: tras una actualización seguía mostrando la interfaz vieja.
            // Se borra la caché si cambió la interfaz y se carga con una marca de versión.
            string stamp = UiStamp();
            if (cfg.UiStamp != stamp)
            {
                try { await cw.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage | CoreWebView2BrowsingDataKinds.ServiceWorkers); Log.Write("interfaz nueva: caché del navegador borrada"); }
                catch (Exception ex) { Log.Error("borrar caché", ex); }
                cfg.UiStamp = stamp; cfg.Save();
            }
            cw.Navigate("https://app.scarlet/index.html?v=" + stamp);
            // Mientras juegas (la interfaz escondida), el motor de la interfaz usa la menor memoria posible: con Stellar
            // Blade la consola llegó a quedarse con 1.8 GB libres y el juego se trababa mientras Windows usaba el disco.
            web.VisibleChanged += (_, _) =>
            {
                try { cw.MemoryUsageTargetLevel = web.Visible ? CoreWebView2MemoryUsageTargetLevel.Normal : CoreWebView2MemoryUsageTargetLevel.Low; }
                catch (Exception ex) { Log.Error("memoria de la interfaz", ex); }
            };

            // Menú rápido que se abre encima de los juegos (se prepara oculto).
            try
            {
                quick = new QuickOverlay(OnMessage);
                await quick.InitAsync(env, Paths.Web, cfg.DevTools, UiStamp(), ServeFile);
                try { quick.SetGlass(cfg.QuickGlass); } catch (Exception ex) { Log.Error("menú rápido (transparente)", ex); }
            }
            catch (Exception ex) { Log.Error("menú rápido", ex); quick = null; }

            hMain = Handle; hQuick = quick?.Handle ?? IntPtr.Zero;

            // Eventos de los servicios → interfaz
            steam.LibraryChanged += () => Emit("library.changed", null);
            xbox.ArtChanged += () => Emit("library.changed", null);
            steam.FriendOnline += f => Emit("friend.online", f);
            steam.FriendsChanged += () => Emit("friends.changed", steam.Friends);
            steam.AchievementUnlocked += async a =>
            {
                string file = null;
                try { file = await art.CacheFileAsync(a.Icon, "ach"); } catch { }
                Emit("achievement.unlocked", new { appid = a.AppId, name = a.Name, desc = a.Desc, icon = file != null ? ArtCache.LocalUrl(file) : null, iconPath = file,
                    percent = a.Percent, unlocked = a.Unlocked, total = a.Total });
            };
            steam.DownloadProgress += (id, p, kind) => Emit("download.progress", new { appid = id, progress = p, kind });
            steam.DownloadDone += id => Emit("download.done", new { appid = id });
            _ = Task.Run(DownloadLoop);
            pads.Connected += d => Emit("pad.connected", d);
            pads.Disconnected += d => Emit("pad.disconnected", d);
            pads.Found += (id, name) => Emit("bt.found", new { id, name });
            album.NewCapture += d => Emit("capture.saved", d);
            album.Saving += d => Ui(() => Emit("capture.saving", d));
            album.Watch();

            xin.GuideSingle += () => Ui(OnGuideSingle);
            xin.GuideDouble += () => Ui(OnGuideDouble);
            xin.GuideHold += () => Ui(OnGuideHold);
            xin.GuideDown += () => Ui(OnGuideDown);
            // Con el menú rápido abierto, los botones del mando le llegan aunque el juego se quede con el foco.
            xin.PadKey += k => Ui(() => quick?.SendKey(k));
            xin.PadActive += c => { var a = PadService.ActiveOf(c); if (a != null) Ui(() => EmitAll("pad.active", a)); };
            if (quick != null) quick.Hidden += () => { xin.ForwardKeys = false; menuToggledAt = DateTime.Now; };
            if (quick != null) quick.Opening += () => { menuToggledAt = DateTime.Now; Perf.Eco(false, ScarletPids(), "se abre el menú"); };

            xin.Start();

            media.Changed += st => EmitAll("media.changed", st);
            _ = media.StartAsync();

            SystemEvents.PowerModeChanged += OnPower;
            SystemEvents.DisplaySettingsChanged += OnDisplay;
            DisplayService.Reverted += () => Ui(() => Emit("display.reverted", null));   // 1.8: no se confirmó la resolución
            _ = Task.Run(() => { if (!DisplayService.ExternalOnly()) DisplayService.ApplyHdr(cfg); });   // ya había una tele conectada al arrancar

            slow.Tick += (_, _) => SlowTick();
            steamGuard.Tick += (_, _) => HideSteamWindows();
            steamGuard.Start();
            StartWindowHook();
            NetCheck.Changed += OnNetChanged;
            NetCheck.Start();
            StartSteamSilently();
            slow.Start();
            friendsTimer.Tick += async (_, _) => await steam.FriendsAsync(true);

            if (string.IsNullOrEmpty(cfg.InternalDisplay)) { cfg.InternalDisplay = Screen.PrimaryScreen.DeviceName; cfg.Save(); }
            Log.Write("Scarlet listo" + (startup ? " (inicio con Windows)" : "") + (updated ? " (recién actualizada)" : ""));
            // Modo seguro: si la versión nueva aguanta medio minuto abierta sin tronar, deja de estar a prueba.
            _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => UpdateService.ConfirmGood());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show("Falta el componente WebView2 de Microsoft. Se abrirá la página para instalarlo.", "Scarlet");
            Shell.Open("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
            Close();
        }
        catch (Exception ex)
        {
            Log.Error("inicio", ex);
            MessageBox.Show("No se pudo iniciar Scarlet:\n" + ex.Message, "Scarlet");
        }
    }

    void SlowTick()
    {
        if (IsDisposed || Disposing) return;   // 1.8.1: al cerrarse para actualizar, el reloj podía alcanzar a dar una vuelta más
        slowTicks++;
        if (steamDialogs.Count > 0 && steamDialogs.All(h => !Native.IsWindow(h) || !Native.IsWindowVisible(h)))
        {
            steamDialogs.Clear(); steamDialogUntil = DateTime.MinValue;
            Log.Write("Steam: se cerró la ventanita; de regreso a Scarlet");
            if (tracker == null || !tracker.Active) ShowUi();
        }
        // El vigilante de "ventana cerrada" solo mira el proceso del juego.
        uint watch = 0;
        if (tracker != null && tracker.Active && tracker.Window != IntPtr.Zero) Native.GetWindowThreadProcessId(tracker.Window, out watch);
        WinWatch.WatchProcess(watch);
        CloseStrayBigPicture();
        AudioFocus.MuteSteam(!steamUiAllowed);
        PointerForFront();
        // Si saliste de Scarlet por otro lado (selector de ventanas, Alt+Tab), lo que estaba silenciado vuelve a sonar.
        var front = Native.GetForegroundWindow();
        bool sideFront = IsSideApp(front);   // la app que abriste junto al juego (Apple Music, YouTube): el juego sigue callado
        bool snapBack = front != Handle && GameFreezer.IsFrozen && tracker != null && IsGameWindow(front)
            && DateTime.Now - homeAt < TimeSpan.FromSeconds(6) && homeRetries < 3;
        if (snapBack)
        {
            // 1.6.1: acabas de ir al Inicio y el juego se puso al frente solo (no lo pediste): Scarlet regresa al frente
            // y el juego sigue en pausa. Antes se reanudaba y te regresaba al juego.
            homeRetries++;
            Log.Write($"el juego se puso al frente solo justo después de ir al Inicio: Scarlet regresa al frente ({homeRetries})");
            TopMost = true; Native.ForceForeground(Handle); web.Focus();
            _ = Task.Delay(400).ContinueWith(_ => Ui(() => TopMost = false));
        }
        else if (front != Handle && !(quick != null && front == quick.Handle) && !sideFront)
        {
            AudioFocus.RestoreAll();
            if (GameFreezer.IsFrozen && tracker != null && IsGameWindow(front) && GameFreezer.Resume()) playStart = DateTime.UtcNow;
        }
        else if ((front == Handle && web.Visible || sideFront) && tracker != null && tracker.Active && tracker.IsReady)
        {
            // Estás en Scarlet (o en la app de al lado) con el juego detrás: se revisa que de verdad siga callado y congelado.
            if (cfg.MuteBehind && !muting) MuteGameBehind(IntPtr.Zero);
            PauseGameIfMarked();
        }
        CheckSideApp();
        // Juegos sin bordes forzados: si el juego vuelve a ponerse la barra de título (al cambiar de resolución), se le quita otra vez.
        if (tracker != null && tracker.Active && cfg.Borderless.Contains(tracker.AppId) && borderlessFixes < 20)
        {
            IntPtr gw = tracker.Window != IntPtr.Zero ? tracker.Window : tracker.Candidate;
            if (Compat.HasBorders(gw) && Native.Area(gw) > 200_000 && Compat.MakeBorderless(gw)) borderlessFixes++;
        }
        else if (tracker == null || !tracker.Active) borderlessFixes = 0;
        if (!GameFreezer.IsFrozen) CheckHung(front);   // congelado, Windows lo marca "no responde": es normal
        CheckTouchBlocked();
        CheckBattery();
        // Con la conexión directa, el progreso llega en vivo (DownloadLoop); si no, de los archivos de Steam cada 3 s.
        if (DateTime.UtcNow - cdpLiveAt > TimeSpan.FromSeconds(6))
            try { steam.PollDownloads(); } catch (Exception ex) { Log.Error("descargas", ex); }
        if (slowTicks % 20 == 0) _ = steam.PollRunningAchievementsAsync();   // cada minuto mientras juegas
        PerfTick();   // rendimiento mientras juegas (antes tenía su propio reloj; ahora va en este mismo ciclo)
    }

}
