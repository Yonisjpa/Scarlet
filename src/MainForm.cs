using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace XboxScarlet;

/// <summary>
/// Ventana a pantalla completa con la interfaz (WebView2). Recibe los mensajes de la interfaz
/// (window.chrome.webview.postMessage) y le contesta / le manda eventos.
/// </summary>
sealed class MainForm : Form
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
            }
            catch (Exception ex) { Log.Error("menú rápido", ex); quick = null; }

            hMain = Handle; hQuick = quick?.Handle ?? IntPtr.Zero;

            // Eventos de los servicios → interfaz
            steam.LibraryChanged += () => Emit("library.changed", null);
            xbox.ArtChanged += () => Emit("library.changed", null);
            steam.FriendOnline += f => Emit("friend.online", f);
            steam.FriendsChanged += () => Emit("friends.changed", steam.Friends);
            steam.AchievementUnlocked += async (id, name, desc, icon) =>
            {
                string file = null;
                try { file = await art.CacheFileAsync(icon, "ach"); } catch { }
                Emit("achievement.unlocked", new { appid = id, name, desc, icon = file != null ? ArtCache.LocalUrl(file) : null, iconPath = file });
            };
            steam.DownloadProgress += (id, p, kind) => Emit("download.progress", new { appid = id, progress = p, kind });
            steam.DownloadDone += id => Emit("download.done", new { appid = id });
            _ = Task.Run(DownloadLoop);
            pads.Connected += d => Emit("pad.connected", d);
            pads.Disconnected += d => Emit("pad.disconnected", d);
            pads.Found += (id, name) => Emit("bt.found", new { id, name });
            album.NewCapture += d => Emit("capture.saved", d);
            album.Watch();

            xin.GuideSingle += () => Ui(OnGuideSingle);
            xin.GuideDouble += () => Ui(OnGuideDouble);
            xin.GuideHold += () => Ui(OnGuideHold);
            xin.GuideDown += () => Ui(OnGuideDown);
            // Con el menú rápido abierto, los botones del mando le llegan aunque el juego se quede con el foco.
            xin.PadKey += k => Ui(() => quick?.SendKey(k));
            if (quick != null) quick.Hidden += () => xin.ForwardKeys = false;
            xin.Start();

            media.Changed += st => EmitAll("media.changed", st);
            _ = media.StartAsync();

            SystemEvents.PowerModeChanged += OnPower;
            SystemEvents.DisplaySettingsChanged += OnDisplay;

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
        if (front != Handle && !(quick != null && front == quick.Handle) && !sideFront)
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
    }

    // ───────────────────────────── Puente con la interfaz ─────────────────────────────

    void Ui(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) BeginInvoke(a);
        else a();
    }

    void Send(object message) => SendTo(null, message);

    /// <summary>Manda un mensaje a una vista web (null = la principal).</summary>
    void SendTo(CoreWebView2 target, object message)
    {
        string json;
        try { json = JsonSerializer.Serialize(message, J); }
        catch (Exception ex) { Log.Error("json", ex); return; }
        Ui(() =>
        {
            try { (target ?? web.CoreWebView2)?.PostWebMessageAsJson(json); }
            catch (Exception ex) { Log.Error("enviar", ex); }
        });
    }

    /// <summary>Marca de la interfaz: versión de la app + fecha de web\index.html.</summary>
    static string UiStamp()
    {
        long t = 0;
        try { t = File.GetLastWriteTimeUtc(Path.Combine(Paths.Web, "index.html")).Ticks; } catch { }
        return Application.ProductVersion + "-" + t.ToString("x");
    }

    void Emit(string ev, object data) => Send(new Dictionary<string, object> { ["event"] = ev, ["data"] = data });

    /// <summary>A la interfaz y también al menú rápido que se abre encima de los juegos.</summary>
    void EmitAll(string ev, object data)
    {
        var msg = new Dictionary<string, object> { ["event"] = ev, ["data"] = data };
        Send(msg);
        Ui(() => { var v = quick?.View; if (v != null) SendTo(v, msg); });
    }

    async void OnMessage(string json, CoreWebView2 from)
    {
        long id = 0;
        string type = "";
        try
        {
            JsonElement payload;
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var idv) && idv.ValueKind == JsonValueKind.Number) id = idv.GetInt64();
                if (root.TryGetProperty("type", out var tv)) type = tv.GetString() ?? "";
                payload = root.TryGetProperty("payload", out var pv) ? pv.Clone() : default;
            }
            object data = await HandleMessage(type, payload, from);
            SendTo(from, new Dictionary<string, object> { ["id"] = id, ["ok"] = true, ["data"] = data });
        }
        catch (Exception ex)
        {
            Log.Error("mensaje " + type, ex);
            SendTo(from, new Dictionary<string, object> { ["id"] = id, ["ok"] = false, ["error"] = ex.Message });
        }
    }

    async Task<object> HandleMessage(string type, JsonElement p, CoreWebView2 from)
    {
        switch (type)
        {
            // ── Steam
            case "steam.profile": return await Profile();
            case "steam.games":
            {
                lastGames = await steam.GamesAsync(apps, cfg.AppLastUsed);
                foreach (var e in lastGames.Where(e => (string)e["source"] == "app"))
                {
                    var a = apps.FirstOrDefault(x => x.AppId == (long)e["appid"]);
                    if (a != null) e["hours"] = Hours(a.Id ?? a.Name ?? "");
                }
                lastGames = await WithXboxGamesAsync(lastGames);
                foreach (var e in lastGames) { e["pause"] = !cfg.NoPauseOnHome.Contains((long)e["appid"]); e["keepFocus"] = cfg.MenuKeepsFocus.Contains((long)e["appid"]); e["borderless"] = cfg.Borderless.Contains((long)e["appid"]); e["fullMenu"] = cfg.FullMenu.Contains((long)e["appid"]); }
                if (!achKicked)
                {
                    achKicked = true;
                    var recent = lastGames.Where(g => (string)g["source"] == "steam" && (long)g["lastTs"] > 0).Take(25).Select(g => (long)g["appid"]).ToList();
                    _ = steam.RefreshAchievementCountsAsync(recent);
                }
                return lastGames;
            }
            case "steam.friends":
            {
                var list = await steam.FriendsAsync(false);
                if (!friendsTimer.Enabled) friendsTimer.Start();
                return list;
            }
            case "steam.friendProfile": return await steam.FriendProfileAsync(P.S(p, "steamid"));
            case "steam.achievements": return await steam.AchievementsAsync(P.L(p, "appid"));
            case "steam.news":
            {
                var top = lastGames.Where(g => (string)g["source"] == "steam" && ((bool)g["installed"] || (long)g["lastTs"] > 0))
                                   .Take(6).Select(g => ((long)g["appid"], (string)g["name"])).ToList();
                return await steam.NewsAsync(top);
            }
            case "steam.open": await OpenSystemAsync(P.S(p, "url")); return true;
            case "steam.join":
            {
                long appid = P.L(p, "appid");
                await CloseBigPictureAsync();
                bool ok = steam.Join(P.S(p, "steamid"), appid);
                if (ok) BeginGame(appid, true);
                return new { ok };
            }

            // ── Imágenes
            case "sgdb": return await art.SgdbAsync(P.S(p, "path"));
            case "cache.image": return await art.CacheAsync(P.S(p, "url"));

            // ── Álbum
            case "album.list": return await Task.Run(() => album.List());
            case "album.delete": return album.Delete(P.S(p, "path"));
            case "album.trim": return await album.TrimAsync(P.S(p, "path"), P.D(p, "start"), P.D(p, "end"));
            case "album.share":
            {
                var list = p.TryGetProperty("paths", out var arr) && arr.ValueKind == JsonValueKind.Array
                    ? arr.EnumerateArray().Select(x => x.GetString()).Where(album.IsAlbumFile).ToList() : new List<string>();
                var r = share.Start(list);
                _ = BringFirewallPromptAsync();
                return r;
            }
            case "album.shareStop": share.Stop(); return true;
            case "album.copy": return CopyToClipboard(P.S(p, "path"));
            case "capture.take": return false;   // en la app real las capturas vienen de la Game Bar / Steam

            // ── Juegos
            case "game.launch":
            {
                long appid = P.L(p, "appid");
                try { await LaunchAsync(appid, P.S(p, "source")); }
                catch
                {
                    TopMost = false;
                    tracker.Stop();
                    Emit("game.failed", new { appid });
                    throw;
                }
                return true;
            }
            case "game.focus": FocusGame(); return true;
            case "app.open": await OpenAlongsideAsync(P.L(p, "appid"), P.S(p, "source")); return true;
            case "compat.fix":
            {
                long id = P.L(p, "appid");
                string exe = cfg.GameExes.TryGetValue(id.ToString(), out var ge) ? ge : null;
                var r = await Task.Run(() => Compat.Fix(exe));
                if (P.B(p, "borderless") && !cfg.Borderless.Contains(id)) { cfg.Borderless.Add(id); cfg.Save(); }
                if (tracker.Active && tracker.AppId == id && cfg.Borderless.Contains(id)) Compat.MakeBorderless(tracker.Window != IntPtr.Zero ? tracker.Window : tracker.Candidate);
                return new { r.fso, r.windowedOpt, exe = r.exe != null, r.message };
            }
            case "compat.set":
            {
                long id = P.L(p, "appid");
                var list = P.S(p, "what") == "fullMenu" ? cfg.FullMenu : cfg.Borderless;
                list.Remove(id);
                if (P.B(p, "on")) list.Add(id);
                cfg.Save();
                if (list == cfg.Borderless && P.B(p, "on") && tracker.Active && tracker.AppId == id) Compat.MakeBorderless(tracker.Window != IntPtr.Zero ? tracker.Window : tracker.Candidate);
                return true;
            }
            case "media.state": return media.State;
            case "app.icon": return await AppIconAsync(P.L(p, "appid"));
            case "app.info": return new { startup, updated, rolledBack = UpdateService.TakeRolledBackNotice(), version = Application.ProductVersion, dolby = DolbyInstalled() };
            case "update.check": return await UpdateService.CheckAsync();
            case "update.install":
                return await UpdateService.InstallAsync(pr => Emit("update.progress", new { p = pr }), () => BeginInvoke(new Action(() => { Log.Write("cerrando para actualizar"); Close(); })));
            case "game.pausepref":
            {
                long id = P.L(p, "appid");
                cfg.NoPauseOnHome.Remove(id);
                if (!P.B(p, "on")) cfg.NoPauseOnHome.Add(id);
                cfg.Save();
                return true;
            }
            case "media.cmd": return await media.CommandAsync(P.S(p, "cmd"), P.D(p, "value"));
            case "cursor.hide":
                cfg.HideCursor = P.B(p, "on"); cfg.Save();
                if (cfg.HideCursor) CursorHider.Hide(); else CursorHider.Restore();
                return true;
            case "audio.mutebehind":
                cfg.MuteBehind = P.B(p, "on"); cfg.Save();
                if (!cfg.MuteBehind) AudioFocus.RestoreAll();
                return true;
            case "game.state": return new { appid = tracker.Active ? tracker.AppId : 0, ready = tracker.IsReady };
            case "game.detach":   // dejar la app abierta en segundo plano (música, YouTube) y dejar de seguirla
                GameFreezer.Resume(); FlushPlay(); playKey = null;
                detachedApp = tracker.AppId;
                Log.Write($"{tracker.AppId} queda abierta en segundo plano");
                tracker.Stop(); steam.SetRunning(0); AudioFocus.RestoreAll();
                return true;
            case "game.close": GameFreezer.Resume(); FlushPlay(); playKey = null; tracker.Close(); steam.SetRunning(0); AudioFocus.RestoreAll(); return true;   // congelado no podría cerrarse bien
            case "game.uninstall": _ = SteamDialogAsync("steam://uninstall/" + P.L(p, "appid"), "desinstalar"); return true;
            // ── Instalar directo con Steam (sin sus ventanas)
            case "install.prepare": return await InstallPrepare(P.L(p, "appid"));
            case "install.folder": return await InstallFolder(P.L(p, "appid"), (int)P.L(p, "folder"));
            case "install.go": return await InstallStep(P.L(p, "appid"), SteamCdp.ContinueAsync(P.L(p, "appid"), (int)P.L(p, "folder")));
            case "install.eula": return await InstallStep(P.L(p, "appid"), SteamCdp.EulaAsync(P.L(p, "appid"), Flag(p, "accept"), EulaIds(p)));
            case "install.cancel": await SteamCdp.CancelAsync(); GuardSteam(3); return true;
            // ── Descargas: pausar, reanudar, cancelar instalación (= desinstalar en Steam)
            case "download.pause": return new { ok = await SteamCdp.PauseAsync(P.L(p, "appid")) };
            case "download.resume": return new { ok = await SteamCdp.ResumeAsync(P.L(p, "appid")) };
            case "download.cancel": return await CancelDownload(P.L(p, "appid"));
            case "install.enable": return InstallEnable();
            // ── Red y VPN
            case "net.state": return new { internet = NetCheck.Internet, steam = NetCheck.SteamReachable };
            case "vpn.state": return await Vpn.StatusAsync(cfg);
            case "vpn.set":
            {
                bool on = P.B(p, "on");
                var r = await Vpn.SetAsync(cfg, on);
                // Steam que arrancó sin conexión no se reconecta solo: con la VPN prendida se reinicia desde cero.
                if (on && r.TryGetValue("status", out var stv) && (string)stv == "on") _ = ReconnectSteamAsync("se prendió la VPN", true);
                return r;
            }
            case "vpn.setup": { var (ok, error) = await Vpn.SetupWireGuardAsync(); ShowUi(); return new { ok, error }; }
            case "vpn.import":
            {
                var (ok, name, error) = await Task.Run(Vpn.ImportWireGuard);
                if (ok) { cfg.VpnProfileName = name; cfg.VpnProvider = "wireguard"; cfg.Save(); }
                return new { ok, name, error };
            }
            case "vpn.remove":
            {
                if (cfg.VpnProvider == "wireguard" || Vpn.WarpCli == null) await Vpn.SetAsync(cfg, false);
                Vpn.RemoveWireGuard(); cfg.VpnProfileName = null; if (cfg.VpnProvider == "wireguard") cfg.VpnProvider = ""; cfg.Save();
                return await Vpn.StatusAsync(cfg);
            }
            case "vpn.provider": { string pv = P.S(p, "provider"); if (pv is "warp" or "wireguard") { cfg.VpnProvider = pv; cfg.Save(); } return await Vpn.StatusAsync(cfg); }
            case "web.open":   // páginas de descarga (WARP, WireGuard) en el navegador de Windows
            {
                string url = P.S(p, "url");
                if (url != null && Regex.IsMatch(url, @"^https://(one\.one\.one\.one|1\.1\.1\.1|www\.wireguard\.com|protonvpn\.com|account\.protonvpn\.com)(/|$)")) { TopMost = false; Shell.Open(url); return true; }
                return false;
            }

            // ── Ventana y notificaciones
            case "ui.show": ShowUi(); return true;
            case "quick.close": quick?.HideNow(true, P.S(p, "why")); return true;
            case "quick.shown": quick?.Ack(); return true;
            case "game.kill": tracker.Kill(); return true;
            case "game.wait": hungSnooze = DateTime.Now.AddMinutes(2); hungAsked = false; FocusGame(); return true;
            case "quick.action":
            {
                // Botones del menú rápido encima del juego que necesitan la pantalla principal.
                quick?.HideNow(false, "botón " + P.S(p, "action"));
                string action = P.S(p, "action");
                if (action == "settings") { BringUiToFront(); Emit("ui.settings", null); }
                else if (action == "sleep") { BringUiToFront(); Emit("ui.sleep", null); }
                return true;
            }
            case "overlay.notify":
                ToastForm.Pop(P.S(p, "title"), P.S(p, "text"), P.S(p, "letter"), P.S(p, "color"), P.S(p, "image"));
                return true;
            case "rumble": xin.Rumble(P.D(p, "strong"), P.D(p, "weak"), (int)P.L(p, "ms")); return true;

            // ── Sistema
            case "system.sleep":
                _ = Task.Delay(500).ContinueWith(_ => Application.SetSuspendState(PowerState.Suspend, false, false));
                return true;
            case "system.hibernate":
                _ = Task.Delay(600).ContinueWith(_ =>
                {
                    // Si la hibernación está desactivada en Windows, se suspende normal.
                    if (!Application.SetSuspendState(PowerState.Hibernate, false, false))
                    {
                        Log.Write("hibernar no está disponible; se suspende");
                        Application.SetSuspendState(PowerState.Suspend, false, false);
                    }
                });
                return true;
            case "system.shutdown":
                Log.Write("apagando el equipo");
                Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });
                return true;
            case "system.open": await OpenSystemAsync(P.S(p, "uri")); return true;
            case "system.armoury": return await sys.OpenArmouryAsync();
            case "system.state":
            {
                return await sys.StateAsync();
            }
            case "audio.atmos": return await Task.Run(AtmosActive);
            // Menú rápido en vivo: cada dato por separado (si uno falla, los demás sí llegan); se pide cada 2 s.
            // Al abrir (full) se vuelve a leer todo desde cero; cada dato tiene un tiempo límite y, si no llega, la interfaz
            // se queda con lo último que sabía (antes una lectura atorada dejaba el menú sin barras ni Salida).
            case "quick.live":
            {
                bool full = P.B(p, "full");
                IntPtr hint = from == quick?.View ? quick.ReturnTo : lastForeign;
                var r = new Dictionary<string, object>();
                var mix = Timed("mezclador", () => MixerState(hint), 1500);
                var atm = Timed("Dolby", () => AtmosActive() && AudioMixer.Sessions().Any(x => x.active && !NotApps.Contains(x.exe)), 1500);

                var bat = full ? Timed("batería", BatteryInfo, 2500) : null;
                var med = full ? Timed("música", () => media.State, 800) : null;
                var stt = full ? sys.StateAsync() : null;
                // Siempre (cada 2 s): volumen y brillo reales, por si los cambias con los botones de la consola con el menú abierto.
                var vol = !full ? Timed("volumen", AudioMixer.GetMaster, 800) : null;
                var bri = !full ? Timed("brillo", SystemService.GetBrightness, 1200) : null;
                if (await mix is (true, var mv) && mv != null) r["mixer"] = mv;
                if (await atm is (true, var av)) r["atmos"] = av;
                if (bat != null && await bat is (true, var bv)) r["battery"] = bv;
                if (med != null && await med is (true, var mdv)) r["media"] = mdv;
                if (stt != null) try { if (await Task.WhenAny(stt, Task.Delay(1500)) == stt) r["system"] = await stt; } catch (Exception ex) { Log.Error("menú (sistema)", ex); }
                if (vol != null)
                {
                    var live = new Dictionary<string, object>();
                    if (await vol is (true, int vv)) live["volume"] = vv;
                    if (await bri is (true, int briLive)) live["brightness"] = briLive;
                    if (live.Count > 0) r["system"] = live;
                }
                return r;
            }
            case "audio.outputs": return await Task.Run(() => AudioMixer.Outputs());
            case "battery.info": return await Task.Run(BatteryInfo);
            case "diag": return await Task.Run(Diagnostics);
            case "audio.reset": return await Task.Run(() => { int n = AudioMixer.ResetAll(); AudioFocus.ForgetMuted(); return n; });
            case "mixer.set": { string exe = P.S(p, "exe"), dir = AppDirOf(exe); AudioMixer.Queue("app:" + exe, (int)P.L(p, "value"), v => AudioMixer.SetVolume(exe, dir, v)); return true; }
            case "audio.output": return await Task.Run(() => AudioMixer.SetOutput(P.S(p, "id")));
            case "game.menufocus":
            {
                long id = P.L(p, "appid");
                cfg.MenuKeepsFocus.Remove(id);
                if (P.B(p, "on")) cfg.MenuKeepsFocus.Add(id);
                cfg.Save();
                return true;
            }
            case "system.brightness": AudioMixer.Queue("brillo", (int)P.L(p, "value"), SystemService.SetBrightness); return true;   // solo el último valor
            case "system.volume": AudioMixer.Queue("sistema", (int)P.L(p, "value"), AudioMixer.SetMaster); return true;   // todas las salidas que suenan; solo el último valor
            case "system.power": SystemService.SetPower(P.S(p, "profile")); return true;
            case "system.radio": return await sys.SetRadiosAsync(P.B(p, "wifi"), P.B(p, "airplane"));
            case "storage.info": return SystemService.Storage(steam.SteamPath);

            // ── Mandos y Bluetooth
            case "pads.list": return pads.List();
            case "pad.player":
                cfg.PadOrder[P.S(p, "key") ?? ""] = (int)P.L(p, "player");
                cfg.Save();
                return true;
            case "pad.rumble":
            {
                string key = P.S(p, "key");
                if (!pads.Rumble(key)) xin.Rumble(.7, .7, 600);
                return true;
            }
            case "pad.disconnect": return await pads.UnpairAsync(P.S(p, "key"));
            case "bt.scan": pads.Scan(); return true;
            case "bt.pair": return await pads.PairAsync(P.S(p, "id"));

            // ── App
            case "app.setup": OpenSetup(); return true;
            case "setup.get": return SetupState();
            case "setup.set": return SetupSet(P.S(p, "field"), p);
            case "sounds.list": return SoundsList();
            case "sounds.info": return new { pack = SoundPack.Current() };
            case "sounds.import":
            {
                var r = await Task.Run(SoundPack.Import);
                if (r.ok) EmitAll("sounds.changed", SoundsList());
                return r;
            }
            case "sounds.reset": SoundPack.Remove(); EmitAll("sounds.changed", SoundsList()); return true;
            case "clipboard.get": return Clipboard.ContainsText() ? Clipboard.GetText() : "";
            case "app.reload": steam.Reset(); web.CoreWebView2?.Reload(); return true;
            case "app.exit": BeginInvoke(new Action(Close)); return true;

            default: throw new NotSupportedException("Mensaje desconocido: " + type);
        }
    }

    /// <summary>Agrega los juegos de la Xbox Store a la biblioteca (se vuelven a buscar cada 5 minutos como máximo).</summary>
    async Task<List<Dictionary<string, object>>> WithXboxGamesAsync(List<Dictionary<string, object>> list)
    {
        try
        {
            if (DateTime.Now - xboxScanned > TimeSpan.FromMinutes(5))
            {
                xboxScanned = DateTime.Now;
                await Task.Run(() => xbox.ScanAsync());
            }
            foreach (var g in xbox.Games)
            {
                long last = cfg.AppLastUsed.TryGetValue("xbox:" + g.Pfn, out var t) ? t : 0;
                var (grid, hero) = xbox.ArtFor(g.AppId);
                list.Add(new Dictionary<string, object>
                {
                    ["appid"] = g.AppId, ["name"] = g.Name, ["source"] = "xbox", ["kind"] = g.IsApp ? "app" : "game", ["installed"] = true,
                    ["hours"] = Hours("xbox:" + g.Pfn), ["lastTs"] = last, ["last"] = SteamService.Rel(last), ["ach"] = new[] { 0, 0 },
                    ["cols"] = g.IsApp ? new List<string>() : new List<string> { "Xbox" }, ["size"] = g.SizeGb, ["art"] = grid, ["hero"] = hero, ["storeId"] = g.StoreId
                });
            }
            return list.OrderByDescending(e => (long)e["lastTs"]).ToList();
        }
        catch (Exception ex) { Log.Error("xbox: biblioteca", ex); return list; }
    }

    async Task<object> Profile()
    {
        var cached = SteamService.CachedProfile();
        var fresh = steam.FetchProfileAsync(art);
        if (cached == null) { var r = await fresh; RetryProfileIfOffline(r); return r; }
        _ = fresh.ContinueWith(t =>
        {
            if (t.Status != TaskStatus.RanToCompletion || t.Result == null) return;
            if (JsonSerializer.Serialize(t.Result, J) != JsonSerializer.Serialize(cached, J)) Emit("profile.changed", t.Result);
            RetryProfileIfOffline(t.Result);
        });
        return cached;
    }

    int profileRetries;

    /// <summary>Al prender la consola el Wi-Fi tarda: si no hubo red, se vuelve a intentar al rato.</summary>
    async void RetryProfileIfOffline(Dictionary<string, object> shown)
    {
        if (steam.ProfileOnline || profileRetries >= 5) return;
        profileRetries++;
        await Task.Delay(TimeSpan.FromSeconds(30 * profileRetries));
        try
        {
            var p = await steam.FetchProfileAsync(art);
            if (p != null && JsonSerializer.Serialize(p, J) != JsonSerializer.Serialize(shown, J)) Emit("profile.changed", p);
            RetryProfileIfOffline(p);
        }
        catch (Exception ex) { Log.Error("perfil (reintento)", ex); }
    }

    /// <summary>Sirve archivos locales (portadas en caché, avatar, capturas) a la interfaz en https://files.scarlet/f?p=RUTA</summary>
    void ServeFile(object sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            var uri = new Uri(e.Request.Uri);
            string path = System.Web.HttpUtility.ParseQueryString(uri.Query)["p"];
            // Direcciones guardadas antes del cambio de nombre (…\XboxScarlet\…): la carpeta ahora se llama Scarlet.
            if (!string.IsNullOrEmpty(path) && !File.Exists(path) && path.Contains(@"\XboxScarlet\", StringComparison.OrdinalIgnoreCase))
                path = Regex.Replace(path, @"\\XboxScarlet\\", @"\Scarlet\", RegexOptions.IgnoreCase);
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || !Allowed(path))
            {
                e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long len = fs.Length;
            string range = e.Request.Headers.Contains("Range") ? e.Request.Headers.GetHeader("Range") : null;
            var m = range == null ? null : Regex.Match(range, @"bytes=(\d*)-(\d*)");
            if (m != null && m.Success && len > 0)
            {
                // Los videos se piden por pedazos (para empezar rápido y poder adelantar): se contesta 206 con ese pedazo.
                long start, end;
                if (m.Groups[1].Value.Length == 0) { start = Math.Max(0, len - long.Parse(m.Groups[2].Value)); end = len - 1; }
                else { start = long.Parse(m.Groups[1].Value); end = m.Groups[2].Value.Length > 0 ? Math.Min(long.Parse(m.Groups[2].Value), len - 1) : len - 1; }
                if (start >= len) { fs.Dispose(); e.Response = env.CreateWebResourceResponse(null, 416, "Range Not Satisfiable", $"Content-Range: bytes */{len}"); return; }
                end = Math.Min(end, start + 4 * 1024 * 1024 - 1);   // como mucho 4 MB por pedazo; el reproductor pide el siguiente
                var buf = new byte[end - start + 1];
                fs.Seek(start, SeekOrigin.Begin);
                int read = 0;
                while (read < buf.Length) { int n = fs.Read(buf, read, buf.Length - read); if (n <= 0) break; read += n; }
                fs.Dispose();
                e.Response = env.CreateWebResourceResponse(new MemoryStream(buf, 0, read), 206, "Partial Content",
                    $"Content-Type: {Mime(path)}\r\nContent-Length: {read}\r\nContent-Range: bytes {start}-{start + read - 1}/{len}\r\nAccept-Ranges: bytes\r\nAccess-Control-Allow-Origin: *");
                return;
            }
            e.Response = env.CreateWebResourceResponse(fs, 200, "OK", $"Content-Type: {Mime(path)}\r\nContent-Length: {len}\r\nAccept-Ranges: bytes\r\nCache-Control: max-age=604800\r\nAccess-Control-Allow-Origin: *");
        }
        catch (Exception ex) { Log.Error("servir archivo", ex); }
    }

    static string SoundsDir => Path.Combine(AppContext.BaseDirectory, "sounds");

    /// <summary>
    /// Sonidos propios (nombre sin extensión → dirección local): los de la carpeta "sounds" junto a la app y, encima,
    /// los del paquete importado (en tus datos de Scarlet, así las actualizaciones no lo borran).
    /// </summary>
    static Dictionary<string, string> SoundsList()
    {
        var map = new Dictionary<string, string>();
        try
        {
            foreach (var dir in new[] { SoundsDir, SoundPack.Dir })
                if (Directory.Exists(dir))
                    foreach (var f in Directory.EnumerateFiles(dir))
                        if (Regex.IsMatch(Path.GetExtension(f), @"^\.(wav|mp3|ogg|m4a)$", RegexOptions.IgnoreCase))
                            map[Path.GetFileNameWithoutExtension(f).ToLowerInvariant()] = ArtCache.LocalUrl(f);
            if (map.Count > 0) Log.Write("sonidos propios: " + string.Join(", ", map.Keys));
        }
        catch (Exception ex) { Log.Error("sonidos", ex); }
        return map;
    }

    bool Allowed(string path) =>
        Paths.Under(path, SoundsDir) || Paths.Under(path, SoundPack.Dir) || Paths.Under(path, Paths.Cache) || Paths.Under(path, steam.SteamPath) || album.Roots().Any(r => Paths.Under(path, r));

    static string Mime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".svg" => "image/svg+xml",
        ".wav" => "audio/wav",
        ".mp3" => "audio/mpeg",
        ".ogg" => "audio/ogg",
        ".m4a" => "audio/mp4",
        ".img" => Sniff(path),
        _ => "application/octet-stream"
    };

    /// <summary>Imágenes guardadas sin extensión (íconos de sitios web): el tipo se ve en los primeros bytes.</summary>
    static string Sniff(string path)
    {
        try
        {
            var b = new byte[4];
            using (var f = File.OpenRead(path)) f.Read(b, 0, 4);
            return b[0] == 0x89 && b[1] == 0x50 ? "image/png" : b[0] == 0xFF && b[1] == 0xD8 ? "image/jpeg" : b[0] == 'G' && b[1] == 'I' ? "image/gif"
                : b[0] == 'R' && b[1] == 'I' ? "image/webp" : b[0] == 0 && b[1] == 0 && b[2] == 1 ? "image/x-icon" : "application/octet-stream";
        }
        catch { return "application/octet-stream"; }
    }

    // ───────────────────────────── Juegos ─────────────────────────────

    async Task LaunchAsync(long appid, string source)
    {
        // Si ese mismo juego ya está abierto, se reanuda: nunca se cierra para volver a lanzarlo.
        if (tracker.Active && tracker.AppId == appid)
        {
            Log.Write($"{appid} ya está abierto: se reanuda");
            FocusGame();
            Emit("game.ready", new { appid });
            return;
        }
        if (!tracker.Active && TryAdoptRunning(appid, source)) { FocusGame(); Emit("game.ready", new { appid }); return; }
        if (tracker.Active) { GameFreezer.Resume(); FlushPlay(); tracker.Close(); }
        playKey = null;
        AudioFocus.RestoreAll();
        TopMost = true;   // la pantalla de carga tapa las ventanitas que abren algunos juegos al arrancar
        ShowUi();
        if (source == "app")
        {
            var a = apps.FirstOrDefault(x => x.AppId == appid) ?? throw new InvalidOperationException("No encontré esa app en apps.json");
            cfg.AppLastUsed[a.Id ?? a.Name ?? ""] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            cfg.Save();
            playKey = a.Id ?? a.Name ?? "";
            Native.AllowAnyForeground();
            a.Start();
            BeginGame(appid, false);
        }
        else if (source == "xbox")
        {
            var g = xbox.Find(appid) ?? throw new InvalidOperationException("No encontré ese juego de Xbox");
            cfg.AppLastUsed["xbox:" + g.Pfn] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            cfg.Save();
            playKey = "xbox:" + g.Pfn;
            Native.AllowAnyForeground();
            xbox.Launch(g);
            // ZZZ y otros juegos de la Xbox Store corren desde XboxGames, no desde la carpeta del paquete: se detecta por su ventana.
            tracker.Begin(appid, false, null, slowStart: true);
        }
        else
        {
            await CloseBigPictureAsync();   // con Big Picture abierto, Steam lo pone al frente al lanzar el juego
            Shell.Open("steam://rungameid/" + appid);
            BeginGame(appid, true);
        }
    }

    /// <summary>¿El juego ya corre aunque Scarlet no lo esté siguiendo (lo perdió, o se abrió por fuera)? Se toma y se reanuda.</summary>
    bool TryAdoptRunning(long appid, string source)
    {
        try
        {
            string dir = source == "xbox" ? xbox.Find(appid)?.Dir : source == "app" ? null : steam.InstallDirOf(appid);
            IntPtr w = GameTracker.RunningWindowIn(dir);
            if (w == IntPtr.Zero && source != "xbox" && source != "app" && steam.RunningAppId() == appid && cfg.GameExes.TryGetValue(appid.ToString(), out var exe))
                w = GameTracker.RunningWindowIn(Path.GetDirectoryName(exe));   // juegos que corren desde otra carpeta
            if (w == IntPtr.Zero || !tracker.AdoptRunning(appid, source != "xbox" && source != "app", dir, w)) return false;
            if (source != "xbox" && source != "app") steam.SetRunning(appid);
            return true;
        }
        catch (Exception ex) { Log.Error("juego ya abierto", ex); return false; }
    }

    void BeginGame(long appid, bool isSteam)
    {
        tracker.Begin(appid, isSteam, isSteam ? steam.InstallDirOf(appid) : null);
        if (isSteam) steam.SetRunning(appid);
    }

    async void OnGameReady(long appid)
    {
        if (playKey != null) playStart = DateTime.UtcNow;
        try
        {
            IntPtr w = tracker.Window;
            string exe = w != IntPtr.Zero ? Native.WindowProcessPath(w, out _) : null;
            if (!string.IsNullOrEmpty(exe) && (!cfg.GameExes.TryGetValue(appid.ToString(), out var old) || old != exe)) { cfg.GameExes[appid.ToString()] = exe; cfg.Save(); }
        }
        catch { }
        Emit("game.ready", new { appid });
        await Task.Delay(900);   // la interfaz desvanece la pantalla de carga
        TopMost = false;
        if (!tracker.Focus()) Native.SendToBottom(Handle);
        HideSoon();
        // El juego suele cambiar la pantalla al arrancar: el menú se prepara de nuevo para que no salga en blanco.
        quick?.Refresh("el juego ya está en pantalla");
        if (quickPending)
        {
            quickPending = false;
            await Task.Delay(1500);   // que termine de recargarse
            if (tracker.Active && quick != null && !quick.IsOpen)
            {
                var fg = Native.GetForegroundWindow();
                bool keep = cfg.MenuKeepsFocus.Contains(tracker.AppId);
                Log.Write("el juego ya cargó: se abre el menú que pediste");
                if (quick.ShowOver(fg, keep)) { xin.ForwardKeys = true; if (!keep) WarnExclusive(); }
            }
        }
    }

    void OnGameExited(long appid)
    {
        quickPending = false;
        GameFreezer.Forget();
        FlushPlay(); playKey = null;
        steam.SetRunning(0);
        TopMost = false;
        ShowUi();
        Emit("game.exited", new { appid });
        if (reconnectPending) _ = Task.Delay(3000).ContinueWith(_ => ReconnectSteamAsync("ya se cerró el juego", true));
    }

    long detachedApp;   // la última app que dejaste abierta en segundo plano (para ponerle nombre e ícono en el mezclador)

    static readonly HashSet<string> NotApps = new(StringComparer.OrdinalIgnoreCase)
    { "Scarlet", "msedgewebview2", "steam", "steamwebhelper", "explorer", "ShellExperienceHost", "SystemSettings", "audiodg", "GameBar", "ArmouryCrate", "ArmouryCrateSE", "RuntimeBroker" };

    /// <summary>Una entrada de la biblioteca (nombre, ícono) para un id; si no está, un nombre suelto.</summary>
    Dictionary<string, object> Entry(long appid, string exe, string fallbackName, int volume, string key)
    {
        var g = appid != 0 ? lastGames.FirstOrDefault(e => (long)e["appid"] == appid) : null;
        return new Dictionary<string, object>
        {
            ["key"] = key, ["exe"] = exe, ["volume"] = volume, ["appid"] = appid,
            ["name"] = g?["name"] ?? fallbackName, ["source"] = g?["source"] ?? "app", ["kind"] = g?["kind"] ?? "app",
            ["art"] = g != null && g.TryGetValue("art", out var a) ? a : null
        };
    }

    // Lecturas del menú que se quedaron atoradas (Windows no contesta): no se lanza otra encima, se espera a esa.
    readonly Dictionary<string, Task> inflight = new();

    /// <summary>Corre una lectura con tiempo límite. ok=false si tardó o falló (la interfaz se queda con lo último que sabía).</summary>
    async Task<(bool ok, T val)> Timed<T>(string what, Func<T> f, int ms)
    {
        Task<T> t;
        lock (inflight)
        {
            if (inflight.TryGetValue(what, out var prev) && !prev.IsCompleted && prev is Task<T> same) t = same;
            else { t = Task.Run(f); inflight[what] = t; }
        }
        if (await Task.WhenAny(t, Task.Delay(ms)) != t) { Log.Write($"menú: {what} tardó más de {ms} ms; se queda lo último"); return (false, default); }
        if (t.IsFaulted) { Log.Error("menú: " + what, t.Exception?.InnerException ?? t.Exception); return (false, default); }
        return (true, t.Result);
    }

    /// <summary>
    /// El proceso del juego (o app) que está detrás de Scarlet. Se busca en orden: su proceso, su ventana (también la
    /// que está cargando), lo que suene desde su carpeta y, si Scarlet no lo está siguiendo, la ventana sobre la que
    /// abriste el menú. Así el menú lo reconoce desde el primer momento, sin esperar a que suene.
    /// </summary>
    (string exe, uint pid, bool tracked) GameProcess(IntPtr hint, List<AudioMixer.Session> sessions)
    {
        string Name(uint id) { try { using var gp = Process.GetProcessById((int)id); return gp.HasExited ? null : gp.ProcessName; } catch { return null; } }
        uint WinPid(IntPtr h) { if (h == IntPtr.Zero || !Native.IsWindow(h)) return 0; Native.GetWindowThreadProcessId(h, out uint p); return p; }
        // Carpeta del juego a partir de su .exe (subiendo de bin\x64, Binaries\Win64…): ahí suelen estar también los
        // procesos que suenan cuando no es el mismo que dibuja la ventana (juegos de Xbox Store, de HoYoverse…).
        string GameDir(uint p)
        {
            string dir = Path.GetDirectoryName(Native.ProcessPath(p) ?? "");
            for (int i = 0; i < 3 && dir != null && Regex.IsMatch(Path.GetFileName(dir), @"^(bin|binaries|win64|win32|x64|x86|shipping)$", RegexOptions.IgnoreCase); i++) dir = Path.GetDirectoryName(dir);
            return dir;
        }
        // De lo que se sabe del juego, el proceso que de verdad suena (o, si nada suena aún, el de su ventana).
        (string, uint)? Pick(uint wpid, string dir)
        {
            if (sessions != null)
            {
                if (wpid != 0 && sessions.FirstOrDefault(x => x.pid == wpid) is { } own) return (own.exe, own.pid);
                foreach (var d in new[] { dir, wpid != 0 ? GameDir(wpid) : null })
                    if (!string.IsNullOrEmpty(d) && sessions.FirstOrDefault(x => !NotApps.Contains(x.exe) && !AudioFocus.IsMusic(x.exe) && Paths.Under(Native.ProcessPath(x.pid) ?? "", d)) is { } s) return (s.exe, s.pid);
            }
            if (wpid != 0 && Name(wpid) is string n) return (n, wpid);
            return null;
        }
        if (tracker != null && tracker.Active)
        {
            string dir = tracker.InstallDir ?? steam.InstallDirOf(tracker.AppId);
            if (string.IsNullOrEmpty(dir) && xbox.Find(tracker.AppId) is { } xg) dir = xg.Dir;
            foreach (uint wp in new[] { tracker.Pid, WinPid(tracker.Window), WinPid(tracker.Candidate) })
                if (wp != 0 && Name(wp) != null && Pick(wp, dir) is { } r) return (r.Item1, r.Item2, true);
            if (Pick(0, dir) is { } rd) return (rd.Item1, rd.Item2, true);
        }
        if (hint != IntPtr.Zero && hint != hMain && hint != hQuick && Native.IsWindow(hint))
        {
            uint hp = WinPid(hint);
            if (hp != 0 && Name(hp) is string hn && !NotApps.Contains(hn) && !AudioFocus.IsMusic(hn) && Pick(hp, null) is { } rh)
            {
                // Es el juego que Scarlet sigue (su ventana cambió): se le pone su nombre. Si no, es otra app suelta.
                bool same = tracker != null && tracker.Active && IsGameWindow(hint);
                return (rh.Item1, rh.Item2, same);
            }
        }
        return (null, 0, false);
    }

    string lastOtherExe; DateTime lastOtherAt;

    /// <summary>
    /// Barras de volumen del menú: la de Apple Music / Spotify siempre que esté abierta (se quita al cerrarla); la del
    /// juego si hay uno; y la de otra app (YouTube…) mientras suena. Solo el juego, sin nada más, no lleva barra (para eso
    /// está el volumen general). Lista vacía = nada que mostrar; null = no se pudo leer (la interfaz se queda con lo último).
    /// </summary>
    List<Dictionary<string, object>> MixerState(IntPtr hint)
    {
        var list = new List<Dictionary<string, object>>();
        var sessions = AudioMixer.Sessions(strict: true);
        var (gameExe, _, tracked) = GameProcess(hint, sessions);
        bool Is(AudioMixer.Session x, string exe) => exe != null && x.exe.Equals(exe, StringComparison.OrdinalIgnoreCase);
        int VolOf(string exe) => sessions.Where(x => Is(x, exe)).Select(x => x.volume).DefaultIfEmpty(100).Max();

        if (gameExe != null)
        {
            long gameId = tracked ? tracker.AppId : 0;   // si se detectó por la ventana del frente, no se le pone el nombre de otro juego
            string gameName = gameExe;
            if (gameId == 0) try { var gp = Native.ProcessPath(sessions.FirstOrDefault(x => Is(x, gameExe))?.pid ?? 0); if (gp != null && FileVersionInfo.GetVersionInfo(gp).FileDescription is string gd && gd.Length > 0) gameName = gd; } catch { }
            list.Add(Entry(gameId, gameExe, gameName, VolOf(gameExe), "game"));
        }
        // Música: siempre que la app tenga su sesión de sonido (abierta, aunque esté en pausa).
        bool MusicSession(AudioMixer.Session x) => AudioFocus.IsMusic(x.exe) || (x.path != null && Regex.IsMatch(x.path, @"\\WindowsApps\\[^\\]*(AppleMusic|Spotify)", RegexOptions.IgnoreCase));
        // Una barra por app (no por programa): Apple Music puede tener varios programas con sonido en su carpeta.
        string Family(AudioMixer.Session x) => xbox.Games.FirstOrDefault(g => !string.IsNullOrEmpty(g.Dir) && x.path != null && Paths.Under(x.path, g.Dir))?.Dir ?? x.exe;
        foreach (var m in sessions.Where(x => MusicSession(x) && !Is(x, gameExe)).GroupBy(Family, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(x => x.active).First()))
        {
            var (id, name) = AppInfo(m);
            list.Add(Entry(id, m.exe, name, VolOf(m.exe), "music"));
        }
        // Otra app que suena (YouTube, Netflix…); entre video y video (unos segundos sin sonar) se sigue mostrando.
        bool sticky = lastOtherExe != null && DateTime.Now - lastOtherAt < TimeSpan.FromSeconds(10);
        var other = sessions.Where(x => !Is(x, gameExe) && !NotApps.Contains(x.exe) && !MusicSession(x)
                                        && (x.active || (sticky && Is(x, lastOtherExe))))
                            .OrderByDescending(x => x.active).FirstOrDefault();
        if (other != null)
        {
            if (other.active) { lastOtherExe = other.exe; lastOtherAt = DateTime.Now; }
            var (id, name) = AppInfo(other);
            list.Add(Entry(id, other.exe, name, VolOf(other.exe), "app"));
        }
        if (list.Count == 1 && (string)list[0]["key"] == "game") list.Clear();
        return list;
    }

    /// <summary>Carpeta de una app de la Store (Apple Music…) a partir del nombre de su programa: sus otros programas también cuentan.</summary>
    string AppDirOf(string exe)
    {
        try
        {
            foreach (var pr in Process.GetProcessesByName(exe))
                using (pr)
                {
                    string path = Native.ProcessPath((uint)pr.Id) ?? "";
                    var store = xbox.Games.FirstOrDefault(g => !string.IsNullOrEmpty(g.Dir) && Paths.Under(path, g.Dir));
                    if (store != null) return store.Dir;
                }
        }
        catch { }
        return null;
    }

    /// <summary>¿Qué app es? Apps de la Store por su carpeta; Edge/YouTube por la que dejaste abierta; si no, el nombre del programa.</summary>
    (long id, string name) AppInfo(AudioMixer.Session x)
    {
        string path = Native.ProcessPath(x.pid) ?? "";
        var store = xbox.Games.FirstOrDefault(g => g.IsApp && !string.IsNullOrEmpty(g.Dir) && Paths.Under(path, g.Dir));
        if (store != null) return (store.AppId, store.Name);
        var app = apps.FirstOrDefault(a => a.AppId == detachedApp) is AppEntry d && Path.GetFileNameWithoutExtension(d.Exec ?? "").Equals(x.exe, StringComparison.OrdinalIgnoreCase)
            ? d : apps.FirstOrDefault(a => Path.GetFileNameWithoutExtension(a.Exec ?? "").Equals(x.exe, StringComparison.OrdinalIgnoreCase));
        if (app != null) return (app.AppId, app.Name);
        try { if (FileVersionInfo.GetVersionInfo(path).FileDescription is string fd && fd.Length > 0) return (0, fd); } catch { }
        return (0, x.exe);
    }

    bool muting;
    IntPtr hMain, hQuick;   // las ventanas de Scarlet, para usarlas fuera del hilo de la pantalla

    /// <summary>
    /// Al entrar a Scarlet el juego se calla siempre (por su proceso, aunque al frente estuviera el menú rápido u otra
    /// ventana); la música no. Recupera su sonido al volver al juego. Se hace aparte para no trabar la pantalla.
    /// </summary>
    void MuteGameBehind(IntPtr hint)
    {
        if (!cfg.MuteBehind || muting) return;
        muting = true;
        _ = Task.Run(() =>
        {
            try
            {
                var (exe, _, _) = GameProcess(hint, AudioMixer.Sessions());
                if (exe != null) AudioFocus.MuteExe(exe);
                // Lo que estaba al frente, si era otra cosa (YouTube encima del juego, por ejemplo).
                if (hint != IntPtr.Zero && hint != hMain && hint != hQuick && Native.IsWindow(hint)) AudioFocus.MuteWindow(hint);
            }
            catch (Exception ex) { Log.Error("silenciar juego", ex); }
            finally { muting = false; }
        });
    }

    // ───── Batería baja (la interfaz tenía el aviso, pero la app nunca lo mandaba) ─────
    bool battWarned;
    void CheckBattery()
    {
        try
        {
            var ps = SystemInformation.PowerStatus;
            if (ps.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery)) return;
            float lvl = ps.BatteryLifePercent;
            bool charging = ps.PowerLineStatus == PowerLineStatus.Online;
            if (charging || lvl > 0.20f) { battWarned = false; return; }
            if (battWarned || lvl > 0.15f) return;
            battWarned = true;
            Log.Write($"batería baja: {Math.Round(lvl * 100)} %");
            Emit("battery.low", new { level = lvl });   // la interfaz lo muestra (encima del juego si estás jugando)
        }
        catch { }
    }

    // ───── Juego que no responde ─────
    int hungTicks; bool hungAsked; DateTime hungSnooze = DateTime.MinValue;

    /// <summary>Si el juego deja de responder 20 s mientras lo tienes enfrente, Scarlet te pregunta si esperar o cerrarlo.</summary>
    void CheckHung(IntPtr front)
    {
        if (tracker == null || !tracker.Active) { hungTicks = 0; hungAsked = false; return; }
        IntPtr h = tracker.Window != IntPtr.Zero ? tracker.Window : tracker.Candidate;
        bool hung = h != IntPtr.Zero && Native.IsWindow(h) && Native.IsHungAppWindow(h);
        if (!hung) { if (hungTicks >= 2) Log.Write($"el juego volvió a responder (estuvo ~{hungTicks * 3} s sin responder)"); hungTicks = 0; hungAsked = false; return; }
        if (++hungTicks == 2) Log.Write($"el juego no responde: {Native.Describe(h)} · {MemoryReport(h)}");
        bool inScarlet = front == Handle && web.Visible;
        // Ya no se le quita la pantalla a un juego congelado (eso pudo ser lo que terminó de tumbar a Stellar Blade):
        // a los ~20 s solo un aviso chiquito encima; si quieres cerrarlo, presionas el botón Xbox y ahí se pregunta.
        if (hungTicks < 7 || hungAsked || inScarlet || DateTime.Now < hungSnooze) return;
        hungAsked = true;
        Log.Write("el juego lleva 20 s sin responder: aviso encima (sin quitarle la pantalla)");
        ToastForm.Pop(NameOfApp(tracker.AppId) + " no responde", "Espera un momento. Si quieres cerrarlo, presiona el botón Xbox", "!", "#f2c230", null);
    }

    /// <summary>¿El juego está congelado ahora mismo (para que el botón Xbox ofrezca cerrarlo)?</summary>
    bool GameHung => tracker != null && tracker.Active && hungTicks >= 3;

    /// <summary>Al presionar el botón Xbox con el juego congelado: Scarlet al frente y la pregunta de esperar o cerrarlo.</summary>
    void AskAboutHungGame()
    {
        Log.Write("botón Xbox con el juego congelado: se pregunta si esperar o cerrarlo");
        TopMost = false;
        ShowUi();
        Emit("game.hung", new { appid = tracker.AppId, name = NameOfApp(tracker.AppId) });
    }

    [StructLayout(LayoutKind.Sequential)] struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    /// <summary>Para el log: memoria libre del equipo y cuánta usa el juego (para saber si se congela por falta de memoria).</summary>
    static string MemoryReport(IntPtr gameWindow)
    {
        try
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            GlobalMemoryStatusEx(ref m);
            string game = "";
            Native.GetWindowThreadProcessId(gameWindow, out uint pid);
            try { using var p = Process.GetProcessById((int)pid); game = $" · el juego usa {p.WorkingSet64 / 1048576} MB ({p.PrivateMemorySize64 / 1048576} MB propios)"; } catch { }
            return $"memoria libre {m.ullAvailPhys / 1048576} de {m.ullTotalPhys / 1048576} MB ({m.dwMemoryLoad} % en uso), archivo de paginación libre {m.ullAvailPageFile / 1048576} MB{game}";
        }
        catch { return "memoria: sin datos"; }
    }

    // ───── Toques bloqueados (el sonido de error de Windows al tocar la pantalla) ─────
    IntPtr lastBlocked;

    /// <summary>
    /// Windows suena "error" cuando tocas una ventana bloqueada por una ventanita que espera respuesta. Si esa ventanita
    /// está escondida (casi siempre un aviso de Steam que se escondió), no hay forma de contestarla: se muestra al frente.
    /// </summary>
    void CheckTouchBlocked()
    {
        try
        {
            var b = Screen.PrimaryScreen.Bounds;
            IntPtr root = Native.RootAt(new Point(b.Left + b.Width / 2, b.Top + b.Height / 2));
            if (root == IntPtr.Zero || Native.IsWindowEnabled(root)) { lastBlocked = IntPtr.Zero; return; }
            IntPtr popup = Native.BlockingPopup(root);
            bool visible = popup != IntPtr.Zero && Native.IsWindowVisible(popup);
            if (root != lastBlocked)
            {
                lastBlocked = root;
                Log.Write($"toque bloqueado: {Native.Describe(root)} espera a {(popup == IntPtr.Zero ? "(ninguna ventana)" : Native.Describe(popup))}{(visible ? "" : " (escondida)")}");
            }
            bool steamPopup = popup != IntPtr.Zero && (hiddenSteam.Contains(popup) || Path.GetFileName(Native.WindowProcessPath(popup, out _) ?? "").StartsWith("steam", StringComparison.OrdinalIgnoreCase));
            if (popup != IntPtr.Zero && !visible && steamPopup)
            {
                steamDialogs.Add(popup);   // que el guardián de Steam no la vuelva a esconder
                TopMost = false;
                Native.ShowWindowAsync(popup, 5 /* SW_SHOW */);
                Native.ForceForeground(popup);
                Log.Write("toque bloqueado: se muestra la ventanita para que la contestes");
            }
            else if (popup == IntPtr.Zero && root == Handle)
            {
                Native.EnableWindow(Handle, true);   // Scarlet quedó bloqueada sin ninguna ventanita: se desbloquea
                Log.Write("toque bloqueado: Scarlet se desbloqueó");
            }
        }
        catch (Exception ex) { Log.Error("toque bloqueado", ex); }
    }

    /// <summary>Batería con datos reales de Windows: si carga, cuánto falta para llenarse; si no, cuánto queda.</summary>
    static object BatteryInfo()
    {
        try
        {
            long rate = 0, remaining = 0, full = 0; bool charging = false, any = false;
            using (var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT ChargeRate, DischargeRate, RemainingCapacity, Charging FROM BatteryStatus"))
                foreach (System.Management.ManagementObject o in s.Get())
                    using (o)
                    {
                        any = true;
                        charging |= o["Charging"] is bool c && c;
                        remaining += Convert.ToInt64(o["RemainingCapacity"] ?? 0);
                        rate += charging ? Convert.ToInt64(o["ChargeRate"] ?? 0) : Convert.ToInt64(o["DischargeRate"] ?? 0);
                    }
            using (var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
                foreach (System.Management.ManagementObject o in s.Get()) using (o) full += Convert.ToInt64(o["FullChargedCapacity"] ?? 0);
            if (!any) return null;
            var ps = SystemInformation.PowerStatus;
            bool plugged = ps.PowerLineStatus == PowerLineStatus.Online;
            int? toFull = charging && rate > 0 && full > remaining ? (int)Math.Round((full - remaining) * 60.0 / rate) : null;
            int? left = !plugged && rate > 0 ? (int)Math.Round(remaining * 60.0 / rate) : ps.BatteryLifeRemaining > 0 ? ps.BatteryLifeRemaining / 60 : null;
            return new { charging, plugged, minutesToFull = toFull, minutesLeft = plugged ? null : left };
        }
        catch (Exception ex) { Log.Error("batería", ex); return null; }
    }

    /// <summary>Pantalla de diagnóstico (Ajustes): lo que Scarlet está detectando ahora mismo.</summary>
    object Diagnostics()
    {
        string spatial = "—";
        try
        {
            string id = Windows.Media.Devices.MediaDevice.GetDefaultAudioRenderId(Windows.Media.Devices.AudioDeviceRole.Default);
            var f = Windows.Media.Audio.SpatialAudioDeviceConfiguration.GetForDeviceId(id)?.ActiveSpatialAudioFormat ?? "";
            spatial = f.Length == 0 ? "ninguno" :
                f.Equals(Windows.Media.Audio.SpatialAudioFormatSubtype.DolbyAtmosForHeadphones, StringComparison.OrdinalIgnoreCase) ? "Dolby Atmos para audífonos" :
                f.Equals(Windows.Media.Audio.SpatialAudioFormatSubtype.DolbyAtmosForHomeTheater, StringComparison.OrdinalIgnoreCase) ? "Dolby Atmos para cine en casa" :
                f.Equals(Windows.Media.Audio.SpatialAudioFormatSubtype.DolbyAtmosForSpeakers, StringComparison.OrdinalIgnoreCase) ? "Dolby Atmos para bocinas" :
                f.Equals(Windows.Media.Audio.SpatialAudioFormatSubtype.WindowsSonic, StringComparison.OrdinalIgnoreCase) ? "Windows Sonic" : f;
        }
        catch (Exception ex) { spatial = "error: " + ex.Message; }
        string gameExe = null;
        try { gameExe = GameProcess(IntPtr.Zero, AudioMixer.Sessions()).exe; } catch { }
        var outs = AudioMixer.Outputs();
        var diag = new Dictionary<string, object>
        {
            ["Versión"] = "alpha " + Application.ProductVersion,
            ["Juego en curso"] = tracker != null && tracker.Active ? $"{NameOfApp(tracker.AppId)} · {(tracker.IsReady ? "listo" : "abriendo")} · {gameExe ?? "proceso sin detectar"}" : "ninguno",
            ["Apps con sonido"] = AudioMixer.Describe() is string d && d.Length > 0 ? d : "ninguna",
            ["Audio espacial"] = spatial,
            ["Salida de audio"] = outs.FirstOrDefault(o => o.current)?.name ?? "—",
            ["Música (Windows)"] = media.Describe(),
            ["Steam"] = $"{(steamUiAllowed ? "visible (tienda/chat)" : "escondido")}{(BigPictureWindow() != IntPtr.Zero ? " · Big Picture abierto" : "")}{(AudioFocus.SteamMuted ? " · en silencio" : "")}",
            ["Puntero"] = CursorHider.Hidden ? "escondido" : "visible",
            ["Silenciadas por Scarlet"] = AudioFocus.MutedList() is string m && m.Length > 0 ? m : "ninguna",
            ["Juego callado"] = gameExe == null ? "—" : AudioFocus.IsMuted(gameExe) switch { true => "sí", false => "no (está sonando)", _ => "no tiene sonido" },
            ["Juego congelado"] = GameFreezer.IsFrozen ? "sí" : "no",
            ["Toque en pantalla"] = lastBlocked != IntPtr.Zero && Native.IsWindow(lastBlocked) ? "bloqueado por " + Native.Describe(lastBlocked) : "normal"
        };
        int? briNow = SystemService.GetBrightness();
        diag["Brillo"] = briNow is int bn ? bn + " %" : "esta pantalla no deja cambiarlo";
        diag["Volumen general"] = AudioMixer.GetMaster() is int vn ? vn + " %" : "no se pudo leer";
        diag["Wi-Fi (permiso)"] = SystemService.RadioAccess;
        // Cada salida de audio con su volumen y qué apps suenan por ella: para ver por dónde sale de verdad el sonido.
        int n = 0;
        foreach (var (device, detail) in AudioMixer.Inventory()) diag[$"Salida {++n}: {device}"] = detail;
        return diag;
    }

    string NameOfApp(long appid) => lastGames.FirstOrDefault(e => (long)e["appid"] == appid)?["name"] as string ?? appid.ToString();

    /// <summary>¿La salida de audio actual está usando Dolby Atmos (Configuración › Sonido › Audio espacial)?</summary>
    static bool AtmosActive()
    {
        try
        {
            string id = Windows.Media.Devices.MediaDevice.GetDefaultAudioRenderId(Windows.Media.Devices.AudioDeviceRole.Default);
            if (string.IsNullOrEmpty(id)) return false;
            var conf = Windows.Media.Audio.SpatialAudioDeviceConfiguration.GetForDeviceId(id);
            string f = conf?.ActiveSpatialAudioFormat ?? "";
            if (f.Length == 0) return false;
            return new[] { Windows.Media.Audio.SpatialAudioFormatSubtype.DolbyAtmosForHeadphones, Windows.Media.Audio.SpatialAudioFormatSubtype.DolbyAtmosForHomeTheater, Windows.Media.Audio.SpatialAudioFormatSubtype.DolbyAtmosForSpeakers }
                .Any(d => string.Equals(d, f, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) { Log.Error("Dolby Atmos (detectar)", ex); return false; }
    }

    static bool? dolby;
    /// <summary>¿La consola tiene Dolby Atmos (Dolby Access o el audio Dolby de ASUS)?</summary>
    static bool DolbyInstalled()
    {
        if (dolby != null) return dolby.Value;
        try
        {
            var pm = new Windows.Management.Deployment.PackageManager();
            dolby = pm.FindPackagesForUser("").Any(p => (p.Id.FamilyName ?? "").Contains("Dolby", StringComparison.OrdinalIgnoreCase));
            if (dolby == false)
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Dolby")) dolby = k != null;
            if (dolby == false)
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                    dolby = k?.GetSubKeyNames().Any(n => n.Contains("Dolby", StringComparison.OrdinalIgnoreCase)) == true;
            if (dolby == false)
            {
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                dolby = Directory.EnumerateFiles(sys, "dax*.dll").Any() || Directory.EnumerateFiles(Path.Combine(sys, "DriverStore", "FileRepository"), "*dolby*", new EnumerationOptions { RecurseSubdirectories = false }).Any()
                    || Directory.EnumerateDirectories(Path.Combine(sys, "DriverStore", "FileRepository"), "*dax*").Any();
            }
        }
        catch { dolby = false; }
        Log.Write("Dolby Atmos: " + (dolby == true ? "sí" : "no encontrado"));
        return dolby.Value;
    }

    static readonly HttpClient web2 = new() { Timeout = TimeSpan.FromSeconds(12) };
    static MainForm() { web2.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36 Edg/130.0"); }

    /// <summary>
    /// Ícono oficial de una app web (YouTube…): el que el sitio declara para instalarse como app (su manifest),
    /// o su apple-touch-icon. Es el mismo ícono que usaría Edge o Chrome al instalarla.
    /// </summary>
    async Task<string> AppIconAsync(long appid)
    {
        var a = apps.FirstOrDefault(x => x.AppId == appid);
        if (a == null) return null;
        var m = Regex.Match((a.Args ?? "") + " " + (a.Exec ?? ""), @"https?://[^\s""']+");
        if (!m.Success) return null;
        try
        {
            var site = new Uri(m.Value);
            var home = new Uri(site.GetLeftPart(UriPartial.Authority) + "/");
            string html = await web2.GetStringAsync(home);
            var candidates = new List<(string url, int size)>();

            var man = Regex.Match(html, @"<link[^>]+rel=[""']manifest[""'][^>]*>", RegexOptions.IgnoreCase);
            var href = man.Success ? Regex.Match(man.Value, @"href=[""']([^""']+)[""']", RegexOptions.IgnoreCase) : null;
            if (href != null && href.Success)
            {
                var manUrl = new Uri(home, System.Net.WebUtility.HtmlDecode(href.Groups[1].Value));
                using var d = JsonDocument.Parse(await web2.GetStringAsync(manUrl));
                if (d.RootElement.TryGetProperty("icons", out var icons))
                    foreach (var i in icons.EnumerateArray())
                    {
                        string purpose = i.TryGetProperty("purpose", out var pu) ? pu.GetString() ?? "" : "";
                        if (purpose.Contains("monochrome")) continue;
                        string src = i.TryGetProperty("src", out var s) ? s.GetString() : null;
                        if (string.IsNullOrEmpty(src)) continue;
                        int size = 0;
                        var sm = Regex.Match(i.TryGetProperty("sizes", out var sz) ? sz.GetString() ?? "" : "", @"(\d+)x\d+");
                        if (sm.Success) size = int.Parse(sm.Groups[1].Value);
                        if (purpose.Contains("maskable") && !purpose.Contains("any")) size /= 2;   // los "maskable" traen mucho margen
                        candidates.Add((new Uri(manUrl, src).ToString(), size));
                    }
            }
            foreach (Match t in Regex.Matches(html, @"<link[^>]+rel=[""']apple-touch-icon[^""']*[""'][^>]*>", RegexOptions.IgnoreCase))
            {
                var h = Regex.Match(t.Value, @"href=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                if (h.Success) candidates.Add((new Uri(home, System.Net.WebUtility.HtmlDecode(h.Groups[1].Value)).ToString(), 180));
            }
            foreach (Match t in Regex.Matches(html, @"<link[^>]+rel=[""'](?:shortcut )?icon[""'][^>]*>", RegexOptions.IgnoreCase))
            {
                var h = Regex.Match(t.Value, @"href=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                var sz = Regex.Match(t.Value, @"sizes=[""'](\d+)x", RegexOptions.IgnoreCase);
                if (h.Success) candidates.Add((new Uri(home, System.Net.WebUtility.HtmlDecode(h.Groups[1].Value)).ToString(), sz.Success ? int.Parse(sz.Groups[1].Value) : 32));
            }
            var best = candidates.Where(c => !c.url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) && c.size >= 96).OrderByDescending(c => c.size).FirstOrDefault();
            // Respaldo: el servicio de íconos de Google da el ícono más grande que tenga el sitio.
            if (best.url == null) best = ($"https://www.google.com/s2/favicons?domain={home.Host}&sz=256", 256);
            Log.Write($"ícono de {a.Name}: {best.url}");
            return await art.CacheAsync(best.url, "appicon");
        }
        catch (Exception ex)
        {
            Log.Error("ícono de " + a.Name, ex);
            try { return await art.CacheAsync($"https://www.google.com/s2/favicons?domain={new Uri(m.Value).Host}&sz=256", "appicon"); }
            catch { return null; }
        }
    }

    /// <summary>Copia la captura al portapapeles: como imagen (para pegarla en Discord / WhatsApp Web) y como archivo.</summary>
    bool CopyToClipboard(string path)
    {
        if (!album.IsAlbumFile(path)) return false;
        try
        {
            var data = new DataObject();
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { path });
            Image img = null;
            if (!Regex.IsMatch(path, @"\.(mp4|webm)$", RegexOptions.IgnoreCase))
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var tmp = Image.FromStream(fs);
                img = new Bitmap(tmp);
                data.SetImage(img);
            }
            Clipboard.SetDataObject(data, true);
            img?.Dispose();
            return true;
        }
        catch (Exception ex) { Log.Error("copiar captura", ex); return false; }
    }

    /// <summary>
    /// La primera vez que se comparte, Windows pregunta si deja que Scarlet reciba conexiones en tu red.
    /// Esa ventanita quedaría detrás de Scarlet, así que se trae al frente.
    /// </summary>
    async Task BringFirewallPromptAsync()
    {
        for (int i = 0; i < 12; i++)
        {
            await Task.Delay(500);
            foreach (var h in Native.TopWindows())
            {
                string t = Native.Title(h);
                if (t.Contains("Firewall", StringComparison.OrdinalIgnoreCase) || t.Contains("seguridad de Windows", StringComparison.OrdinalIgnoreCase) || t.Contains("Windows Security", StringComparison.OrdinalIgnoreCase))
                {
                    TopMost = false;
                    Native.ForceForeground(h);
                    Log.Write("compartir: Windows pide permiso de red: " + t);
                    return;
                }
            }
        }
    }

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
        if (lastForeign != IntPtr.Zero && Native.IsWindow(lastForeign)) { Native.ForceForeground(lastForeign); return; }
        Native.SendToBottom(Handle);
    }

    /// <summary>Con el juego al frente, la interfaz se oculta para no gastar batería.</summary>
    async void HideSoon()
    {
        await Task.Delay(600);
        if (Native.GetForegroundWindow() != Handle) web.Visible = false;
    }

    void ShowUi()
    {
        steamUiAllowed = false;   // de regreso en Scarlet: Steam vuelve a esconderse
        var fg = Native.GetForegroundWindow();
        if (fg != IntPtr.Zero && fg != Handle) lastForeign = fg;
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
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
        bool needsBigPicture = Regex.IsMatch(uri, @"^steam://(store|openurl|controllerconfig|nav|url|open/(news|friends))", RegexOptions.IgnoreCase);
        if (needsBigPicture) steamUiAllowed = true;
        if (needsBigPicture && BigPictureWindow() == IntPtr.Zero)
        {
            Shell.Open("steam://open/bigpicture");
            for (int i = 0; i < 40 && BigPictureWindow() == IntPtr.Zero; i++) await Task.Delay(150);
            await Task.Delay(800);
        }
        Shell.Open(uri);
    }

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

    // ───────────────────────────── Instalar directo con Steam ─────────────────────────────
    // Steam abre su asistente de instalación escondido (el guardián de ventanas lo oculta al aparecer) y Scarlet muestra
    // sus propios diálogos: disco, espacio y contrato de licencia. Si algo no se puede (clave de producto, contraseña,
    // la conexión no está activa), la interfaz regresa a la ventanita de Steam de siempre.

    DateTime cdpLiveAt = DateTime.MinValue;
    bool enabling;

    static bool Num(JsonElement r, string k, out double v)
    {
        v = 0;
        if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty(k, out var x) || x.ValueKind != JsonValueKind.Number) return false;
        v = x.GetDouble(); return true;
    }
    static int State(JsonElement r) => Num(r, "s", out var v) ? (int)v : 0;
    static string Str(JsonElement r, string k) => r.ValueKind == JsonValueKind.Object && r.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : "";
    static bool Flag(JsonElement r, string k) => r.ValueKind == JsonValueKind.Object && r.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.True;

    static List<(string id, int version)> EulaIds(JsonElement p)
    {
        var list = new List<(string, int)>();
        if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("eulas", out var a) && a.ValueKind == JsonValueKind.Array)
            foreach (var e in a.EnumerateArray())
            {
                string id = Str(e, "id");
                if (!string.IsNullOrEmpty(id)) list.Add((id, Num(e, "version", out var v) ? (int)v : 0));
            }
        return list;
    }

    async Task<object> InstallPrepare(long appid)
    {
        if (appid <= 0 || steam.SteamPath == null) return new { mode = "steam", why = "Steam no está en este equipo" };
        steam.Canceled.TryRemove(appid, out _);   // la vuelves a instalar: ya no está cancelada (y no se borra nada)
        if (!NetCheck.Internet) return new { mode = "error", error = "No hay internet: no se puede descargar ahora." };
        if (!NetCheck.SteamReachable) return new { mode = "error", error = "Esta red bloquea Steam. Prende la VPN en el menú rápido y vuelve a intentarlo." };
        if (!SteamCdp.MarkerPresent(steam.SteamPath))
            return new { mode = "setup", gameOpen = tracker != null && tracker.Active, millennium = SteamCdp.MillenniumInstalled(steam.SteamPath) };
        if (enabling) return new { mode = "wait" };
        var probe = await SteamCdp.ProbeAsync();
        if (probe == SteamCdp.Probe.NoApi && SteamUptime() > TimeSpan.FromSeconds(60))
        {
            Log.Write("Steam: hay conexión pero Steam cambió su interfaz interna; se usa su ventanita");
            return new { mode = "steam", why = "Steam cambió su interfaz interna" };
        }
        if (probe != SteamCdp.Probe.Ready)
        {
            if (!SteamCdp.SteamRunning) { StartSteamSilently(); return new { mode = "wait" }; }
            // Steam ya estaba abierto desde antes de crear el archivo: necesita reiniciarse una vez.
            if (SteamUptime() < TimeSpan.FromSeconds(90)) return new { mode = "wait" };
            return new { mode = "setup", restart = true, gameOpen = tracker != null && tracker.Active, millennium = SteamCdp.MillenniumInstalled(steam.SteamPath) };
        }
        JsonElement r;
        GuardSteam(600);   // la ventanita de Steam se esconde en cuanto asome y Scarlet se queda al frente (si no, el mando deja de llegar)
        try { r = await SteamCdp.PrepareAsync(appid); }
        catch (Exception ex) { Log.Write("Steam: preparar instalación: " + ex.Message); await SteamCdp.CancelAsync(); GuardSteam(3); return new { mode = "steam", why = ex.Message }; }
        int s = State(r);
        Num(r, "need", out var need); Num(r, "folder", out var cur); Num(r, "err", out var err);
        Log.Write($"Steam: instalar {appid}: asistente en estado {s} (abierto con: {Str(r, "how")}) · {Str(r, "raw")}");
        bool fresh = Flag(r, "fresh");
        if (!fresh && !(s == 15 && err > 0))   // nunca se vio al asistente nuevo (Steam sin conexión, o no hizo caso)
        {
            await SteamCdp.CancelAsync(); GuardSteam(3);
            if (!NetCheck.Internet || !NetCheck.SteamReachable) return new { mode = "error", error = "Steam no tiene conexión ahora mismo. Prende la VPN o revisa el internet y vuelve a intentarlo." };
            // (Aquí ya no se reinicia Steam: Steam estaba bien y el reinicio solo lo metía en un círculo.)
            return new { mode = "error", error = "Steam no abrió su asistente de instalación. Vuelve a intentarlo en unos segundos." };
        }
        if (!Flag(r, "mine")) { await SteamCdp.CancelAsync(); GuardSteam(3); return new { mode = "steam", why = "Steam no abrió su asistente" }; }
        if (s == 15 || (err > 0 && s != 7 && need <= 0)) { await SteamCdp.CancelAsync(); GuardSteam(3); return new { mode = "error", error = SteamCdp.ErrorText(err > 0 ? (int)err : 1, Str(r, "detail")) }; }
        if (s == 16) { GuardSteam(3); return new { mode = "error", error = "Steam canceló la instalación." }; }
        if (s == 14) { GuardSteam(5); return new { mode = "started" }; }
        if (s == 8) return new { mode = "eula", eulas = await EulaTexts(r) };
        // 7 = elegir disco; un estado desconocido que ya trae el tamaño también es esa pantalla (Steam pudo renumerar sus pasos)
        if (s != 7 && (need <= 0 || s == 0 || SteamCdp.NeedsSteamUi(s))) { await SteamCdp.CancelAsync(); GuardSteam(3); return new { mode = "steam", why = $"Steam necesita algo más (paso {s})" }; }
        return new { mode = "cdp", size = need, folder = (int)cur, canChange = Flag(r, "canChange"), folders = Folders(r) };
    }

    /// <summary>Discos de Steam con el espacio libre real de Windows (y el nombre como lo muestra el Explorador).</summary>
    static List<object> Folders(JsonElement r)
    {
        var list = new List<object>();
        if (!r.TryGetProperty("folders", out var fs) || fs.ValueKind != JsonValueKind.Array) return list;
        var rows = fs.EnumerateArray().ToList();
        var roots = rows.Select(f => (Path.GetPathRoot(Str(f, "path")) ?? "").ToUpperInvariant()).ToList();
        for (int k = 0; k < rows.Count; k++)
        {
            var f = rows[k];
            string path = Str(f, "path"), root = Path.GetPathRoot(path) ?? "", user = Str(f, "label");
            long free = -1; string vol = "";
            try { var di = new DriveInfo(root); if (di.IsReady) { free = di.AvailableFreeSpace; vol = di.VolumeLabel; } } catch { }
            if (free < 0 && long.TryParse(Str(f, "free"), out var sf)) free = sf;
            string letter = root.TrimEnd('\\', '/').TrimEnd(':');
            string name = !string.IsNullOrWhiteSpace(user) ? user : $"{(string.IsNullOrWhiteSpace(vol) ? "Disco local" : vol)} ({letter}:)";
            if (roots.Count(x => x == roots[k]) > 1) name += " · " + Path.GetFileName(path.TrimEnd('\\', '/'));
            bool mounted = !(f.TryGetProperty("mounted", out var m) && m.ValueKind == JsonValueKind.False);
            list.Add(new { i = Num(f, "i", out var ix) ? (int)ix : k, name, free, def = Flag(f, "def"), ok = mounted && free >= 0, path });
        }
        return list;
    }

    async Task<object> InstallFolder(long appid, int folder)
    {
        GuardSteam(600);
        try
        {
            var r = await SteamCdp.SetFolderAsync(appid, folder);
            Num(r, "need", out var need); Num(r, "folder", out var cur);
            return new { ok = !Flag(r, "stale"), size = need, folder = (int)cur };
        }
        catch (Exception ex) { return new { ok = false, error = ex.Message }; }
    }

    async Task<List<object>> EulaTexts(JsonElement r)
    {
        var list = new List<object>();
        if (!r.TryGetProperty("eulas", out var es) || es.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in es.EnumerateArray())
        {
            string url = Str(e, "url");
            list.Add(new { id = Str(e, "id"), version = Num(e, "version", out var v) ? (int)v : 0, url, html = await SteamCdp.EulaHtmlAsync(url) });
        }
        return list;
    }

    /// <summary>Resultado de "Instalar" o del contrato: qué sigue para la interfaz.</summary>
    async Task<object> InstallStep(long appid, Task<JsonElement> step)
    {
        JsonElement r;
        GuardSteam(600);
        try { r = await step; }
        catch (Exception ex) { Log.Write("Steam: instalación: " + ex.Message); await SteamCdp.CancelAsync(); GuardSteam(3); return new { s = "steam", why = ex.Message }; }
        int s = State(r);
        Log.Write($"Steam: instalar {appid}: paso {s}" + (Flag(r, "started") ? " (ya está en descargas)" : "") + (Flag(r, "stale") ? " (el asistente ya no estaba)" : "") + (Flag(r, "stuck") ? " (contrato atorado)" : "") + " · " + Str(r, "raw"));
        if (Flag(r, "started")) { GuardSteam(5); return new { s = "started" }; }
        if (s == 8 && !Flag(r, "stuck") && !Flag(r, "stale")) return new { s = "eula", eulas = await EulaTexts(r) };
        GuardSteam(s == 14 || s == 0 || s == 9 ? 5 : 3);
        if (Flag(r, "stale"))
        {
            if (s == 14) return new { s = "started" };   // ya iba en camino
            return new { s = "error", error = "Steam cerró el asistente. Intenta de nuevo." };
        }
        if (s == 8 && Flag(r, "stuck")) { await SteamCdp.CancelAsync(); return new { s = "steam", why = "el contrato no avanzó" }; }
        if (s == 8) return new { s = "eula", eulas = await EulaTexts(r) };
        if (s == 15) { await SteamCdp.CancelAsync(); return new { s = "error", error = SteamCdp.ErrorText(Num(r, "err", out var e) ? (int)e : 1, Str(r, "detail")) }; }
        if (s == 16) return new { s = "canceled" };
        if (s == 14 || s == 0 || s == 9) { Log.Write($"Steam: {appid} empezó a instalarse"); return new { s = "started" }; }
        await SteamCdp.CancelAsync();
        return new { s = "steam", why = $"Steam necesita algo más (paso {s})" };
    }

    /// <summary>Activa la conexión directa (archivo + reiniciar Steam). Contesta al instante; el resultado llega como evento.</summary>
    object InstallEnable()
    {
        if (tracker != null && tracker.Active) return new { ok = false, error = "Cierra el juego primero: Steam tiene que reiniciarse." };
        if (enabling) return new { ok = true };
        enabling = true;
        _ = Task.Run(async () =>
        {
            (bool ok, string error) r;
            try { r = await SteamCdp.EnableAsync(steam.SteamPath, () => Ui(StartSteamSilently)); }
            catch (Exception ex) { Log.Error("Steam: activar conexión", ex); r = (false, "No se pudo activar: " + ex.Message); }
            enabling = false;
            Emit("install.enabled", new { r.ok, r.error });
        });
        return new { ok = true };
    }

    static TimeSpan SteamUptime()
    {
        try
        {
            var ps = Process.GetProcessesByName("steam");
            try { return ps.Length == 0 ? TimeSpan.Zero : DateTime.Now - ps.Min(x => x.StartTime); }
            finally { foreach (var x in ps) x.Dispose(); }
        }
        catch { return TimeSpan.MaxValue; }
    }

    // ───── Guardia de instalación: mientras Scarlet maneja el asistente de Steam, ninguna ventana de Steam se deja ver ─────
    // El aviso de "ventana nueva" no basta: la ventanita de Steam a veces nace chiquita o reaparece al cambiar de paso, y si
    // se queda al frente (aunque sea invisible) el mando ya no le llega a Scarlet. Por eso se revisa cada 100 ms.
    readonly System.Windows.Forms.Timer installGuard = new() { Interval = 100 };
    bool guardWired;
    DateTime guardUntil = DateTime.MinValue, guardPidsAt = DateTime.MinValue;
    readonly HashSet<uint> guardPids = new();
    readonly HashSet<IntPtr> guardHidden = new();

    void GuardSteam(int seconds)
    {
        Ui(() =>
        {
            if (!guardWired) { guardWired = true; installGuard.Tick += (_, _) => GuardTick(); }
            guardUntil = DateTime.Now.AddSeconds(Math.Max(0, seconds));
            if (seconds > 0) { if (!installGuard.Enabled) installGuard.Start(); GuardTick(); }
            else { installGuard.Stop(); guardHidden.Clear(); }
        });
    }

    void GuardTick()
    {
        if (DateTime.Now > guardUntil || steamUiAllowed) { installGuard.Stop(); guardHidden.Clear(); return; }
        try
        {
            if (DateTime.Now - guardPidsAt > TimeSpan.FromSeconds(2))
            {
                guardPidsAt = DateTime.Now; guardPids.Clear();
                foreach (var n in new[] { "steam", "steamwebhelper" })
                    foreach (var pr in Process.GetProcessesByName(n)) using (pr) guardPids.Add((uint)pr.Id);
            }
            bool hid = false;
            Native.EnumWindows((h, lp) =>
            {
                if (!Native.IsWindowVisible(h)) return true;
                Native.GetWindowThreadProcessId(h, out uint pid);
                if (!guardPids.Contains(pid)) return true;
                Native.ShowWindowAsync(h, 0 /* SW_HIDE */);
                if (guardHidden.Add(h)) Log.Write($"instalar: se escondió una ventana de Steam «{Native.Title(h)}»");
                hid = true;
                return true;
            }, IntPtr.Zero);
            var fg = Native.GetForegroundWindow();
            bool steamFront = false;
            if (fg != IntPtr.Zero && fg != Handle) { Native.GetWindowThreadProcessId(fg, out uint fp); steamFront = guardPids.Contains(fp); }
            if ((hid || steamFront) && web.Visible && (tracker == null || !tracker.Active)) ShowUi();
        }
        catch (Exception ex) { Log.Error("guardia de instalación", ex); }
    }

    /// <summary>
    /// Cancelar instalación (= desinstalar en Steam). Primero se quita de sus descargas: eso la detiene al instante y la
    /// interfaz ya la da por cancelada. Luego se espera a que Steam borre lo descargado; si su desinstalador no hace nada
    /// (en la versión de escritorio a veces lo ignora, log-9), Scarlet lo borra: cierra Steam, quita los archivos a medias
    /// y lo vuelve a abrir. Solo con instalaciones que nunca terminaron; un juego ya instalado no se toca a mano.
    /// </summary>
    async Task<object> CancelDownload(long appid)
    {
        if (appid <= 0) return new { ok = false, error = "Juego no válido" };
        if (!await SteamCdp.ReadyAsync()) return new { ok = false, error = "Steam no está conectado con Scarlet ahora mismo." };
        var before = steam.LocalApps().FirstOrDefault(a => a.AppId == appid);
        GuardSteam(30);
        string how = await SteamCdp.CancelDownloadAsync(appid);
        Log.Write($"Steam: cancelar instalación de {appid} ({how ?? "falló"})");
        if (how == null) { GuardSteam(3); return new { ok = false, error = "Steam no contestó." }; }
        // ¿Ya no está en las descargas de Steam? (hasta ~6 s)
        bool stopped = false;
        for (int i = 0; i < 12 && !stopped; i++)
        {
            await Task.Delay(500);
            var (live, _, ok) = await SteamCdp.DownloadsAsync();
            stopped = ok && live != null && !live.Any(d => d.AppId == appid);
        }
        if (!stopped) { Log.Write($"Steam: {appid} sigue en sus descargas tras cancelar"); return new { ok = false, error = "Steam no la quitó de sus descargas. Intenta de nuevo en unos segundos." }; }
        steam.Canceled[appid] = true;
        Ui(() => steam.Installed.Remove(appid));
        Emit("download.removed", new { appid });
        Log.Write($"Steam: descarga de {appid} detenida y quitada de la lista");
        _ = WipeCanceledAsync(appid, before);
        return new { ok = true };
    }

    /// <summary>Espera a que Steam borre lo descargado; si no lo hace, Scarlet lo borra con Steam cerrado.</summary>
    async Task WipeCanceledAsync(long appid, LocalApp before)
    {
        try
        {
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(1000);
                if (!steam.Canceled.ContainsKey(appid)) return;   // la volviste a instalar
                if (!steam.LocalApps().Any(a => a.AppId == appid)) { steam.Canceled.TryRemove(appid, out _); Log.Write($"Steam: instalación de {appid} desinstalada por Steam"); Emit("download.cleanup", new { appid, state = "done" }); return; }
            }
            GuardSteam(3);
            var app = steam.LocalApps().FirstOrDefault(a => a.AppId == appid) ?? before;
            if (app == null) return;
            if (app.FullyInstalled)
            {
                // Un juego que ya estaba instalado (una actualización): no se borra a mano.
                Log.Write($"Steam: {appid} no se desinstaló (estaba instalado; la actualización quedó quitada de la lista)");
                steam.Canceled.TryRemove(appid, out _);
                Emit("download.cleanup", new { appid, state = "kept" });
                return;
            }
            // Si hay un juego abierto, cerrar Steam lo cerraría: se espera a que salgas de él.
            while (tracker != null && tracker.Active) await Task.Delay(3000);
            while (reconnecting || installGuard.Enabled) await Task.Delay(2000);
            if (!steam.Canceled.ContainsKey(appid)) return;
            Log.Write($"Steam: su desinstalador no borró {appid}; Scarlet borra lo descargado con Steam cerrado");
            Emit("download.cleanup", new { appid, state = "restarting" });
            reconnecting = true; lastReconnect = DateTime.Now;
            string gone = null;
            bool ok = false;
            try { ok = await SteamCdp.RestartSteamAsync(steam.SteamPath, () => Ui(StartSteamSilently), () => gone = WipeFiles(app)); }
            finally { reconnecting = false; }
            bool left = steam.LocalApps().Any(a => a.AppId == appid);
            Log.Write($"Steam: borrado de {appid}: {gone ?? "Steam no se cerró"} · Steam {(ok ? "de vuelta" : "no terminó de abrir")}");
            if (!left) steam.Canceled.TryRemove(appid, out _);
            Emit("download.cleanup", new { appid, state = left ? "failed" : "done" });
            _ = Task.Delay(4000).ContinueWith(_ => Emit("library.changed", new { }));
        }
        catch (Exception ex) { Log.Error("cancelar instalación: borrar", ex); Emit("download.cleanup", new { appid, state = "failed" }); }
    }

    /// <summary>Quita los archivos a medias de una instalación (con Steam cerrado). Devuelve qué se borró.</summary>
    static string WipeFiles(LocalApp app)
    {
        string steamapps = Path.Combine(app.LibraryPath, "steamapps");
        var did = new List<string>();
        void Del(string path, bool dir)
        {
            try
            {
                if (dir ? !Directory.Exists(path) : !File.Exists(path)) return;
                if (dir) Directory.Delete(path, true); else File.Delete(path);
                did.Add(Path.GetFileName(path));
            }
            catch (Exception ex) { Log.Write($"borrar {path}: {ex.Message}"); }
        }
        Del(Path.Combine(steamapps, "downloading", app.AppId.ToString()), true);
        Del(Path.Combine(steamapps, "temp", app.AppId.ToString()), true);
        try { foreach (var f in Directory.EnumerateFiles(Path.Combine(steamapps, "downloading"), $"state_{app.AppId}_*")) Del(f, false); } catch { }
        // La carpeta del juego, solo si es de verdad suya: nombre simple, dentro de common y que ningún otro juego la use.
        string dirName = app.InstallDir ?? "";
        if (dirName.Length > 0 && dirName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && dirName.Trim('.').Length > 0)
        {
            bool shared = false;
            try
            {
                foreach (var f in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                {
                    if (Path.GetFileName(f).Equals($"appmanifest_{app.AppId}.acf", StringComparison.OrdinalIgnoreCase)) continue;
                    var o = Vdf.Load(f)?.Obj("AppState");
                    if (o != null && string.Equals(o.Str("installdir"), dirName, StringComparison.OrdinalIgnoreCase)) shared = true;
                }
            }
            catch { shared = true; }
            if (!shared) Del(Path.Combine(steamapps, "common", dirName), true);
        }
        Del(Path.Combine(steamapps, $"appmanifest_{app.AppId}.acf"), false);
        return did.Count > 0 ? "se borró " + string.Join(", ", did) : "no había archivos";
    }

    /// <summary>Progreso de descargas en vivo desde Steam (cada segundo mientras hay conexión directa).</summary>
    async Task DownloadLoop()
    {
        var last = new Dictionary<long, (double p, string state, DateTime at)>();
        int tick = 0;
        List<LocalApp> acf = new();
        while (!IsDisposed)
        {
            await Task.Delay(1000);
            try
            {
                if (!SteamCdp.MarkerPresent(steam.SteamPath) || enabling) { await Task.Delay(4000); continue; }
                if (!SteamCdp.Connected && !SteamCdp.SteamRunning) { await Task.Delay(4000); continue; }
                var (live, done, ok) = await SteamCdp.DownloadsAsync();
                if (!ok) { await Task.Delay(4000); continue; }
                cdpLiveAt = DateTime.UtcNow;
                // Respaldo: lo que dicen los archivos de Steam (cada 3 s), por si su lista en vivo no trae algún juego.
                // Solo las que Steam marca como descargándose (256 en curso, 512 en pausa, 1024 empezada, 1048576 bajando).
                if (tick++ % 3 == 0) acf = steam.LocalApps().Where(a => a.BytesToDownload > 0 && a.BytesDownloaded < a.BytesToDownload && (a.StateFlags & (256 | 512 | 1024 | 1048576)) != 0).ToList();
                acf.RemoveAll(a => steam.Canceled.ContainsKey(a.AppId));
                live.RemoveAll(d => steam.Canceled.ContainsKey(d.AppId));
                foreach (var a in acf)
                    if (!live.Any(x => x.AppId == a.AppId) && !done.Contains(a.AppId))
                        live.Add(new SteamCdp.Dl(a.AppId, Math.Clamp((double)a.BytesDownloaded / a.BytesToDownload, 0, 1), a.FullyInstalled ? "Actualizando" : "Descargando",
                            SteamCdp.PausedAll ? "paused" : "active", a.BytesDownloaded, a.BytesToDownload, -1, 0, "download"));
                var now = new HashSet<long>();
                foreach (var d in live)
                {
                    now.Add(d.AppId);
                    // Se manda si cambió el avance o el estado, y cada 3 s de todos modos (tiempo restante y velocidad).
                    if (last.TryGetValue(d.AppId, out var prev) && Math.Abs(prev.p - d.Progress) < 0.001 && prev.state == d.State && DateTime.Now - prev.at < TimeSpan.FromSeconds(3)) continue;
                    last[d.AppId] = (d.Progress, d.State, DateTime.Now);
                    Emit("download.progress", new { appid = d.AppId, progress = d.Progress, kind = d.Kind, state = d.State, bytes = d.Bytes, total = d.Total, eta = d.Eta, rate = d.Rate, phase = d.Phase, peer = d.Peer });
                }
                foreach (long id in last.Keys.Where(k => !now.Contains(k)).ToList())
                {
                    last.Remove(id);
                    bool installed = done.Contains(id) || steam.LocalApps().Any(a => a.AppId == id && a.FullyInstalled && a.BytesToDownload == 0);
                    if (installed) { var fresh = steam.LocalApps().FirstOrDefault(a => a.AppId == id); if (fresh != null) Ui(() => steam.Installed[id] = fresh); }
                    Log.Write($"Steam: descarga de {id} " + (installed ? "terminada" : "quitada de la lista"));
                    Emit(installed ? "download.done" : "download.removed", new { appid = id });
                }
            }
            catch (Exception ex) { Log.Error("descargas en vivo", ex); await Task.Delay(5000); }
        }
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
    bool OfflinePrompt(IntPtr h)
    {
        if (NetCheck.Internet || h == IntPtr.Zero) return false;
        if (offlinePrompts.Contains(h)) return true;
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

    /// <summary>
    /// Big Picture escondido seguía corriendo detrás (con sus sonidos al navegar). Si tú no lo abriste (tienda, chat),
    /// se cierra de verdad. Steam sigue abierto en silencio para lanzar juegos, logros y amigos.
    /// </summary>
    void CloseStrayBigPicture()
    {
        if (steamUiAllowed || DateTime.Now - lastBpClose < TimeSpan.FromSeconds(15)) return;
        if (tracker != null && tracker.LaunchSeconds > 0 && tracker.LaunchSeconds < 25) return;   // Steam está arrancando un juego
        bool found = false;
        try
        {
            Native.EnumWindows((h, lp) =>
            {
                if (Native.Title(h).IndexOf("Big Picture", StringComparison.OrdinalIgnoreCase) < 0) return true;
                string exe = Path.GetFileName(Native.WindowProcessPath(h, out _) ?? "");
                if (exe.Equals("steamwebhelper.exe", StringComparison.OrdinalIgnoreCase) || exe.Equals("steam.exe", StringComparison.OrdinalIgnoreCase)) { found = true; return false; }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        if (!found) return;
        lastBpClose = DateTime.Now;
        Log.Write("Big Picture abierto sin que lo pidieras: se cierra");
        Shell.Open("steam://close/bigpicture");
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
        bool keepFocus = tracker != null && tracker.Active && cfg.MenuKeepsFocus.Contains(tracker.AppId);
        if (quick != null && quick.ShowOver(fg, keepFocus)) { xin.ForwardKeys = true; if (!keepFocus) WarnExclusive(); return; }
        Log.Write("toque: menú rápido en la interfaz (el panel no está listo)");
        BringUiToFront(mute: false); Emit("quick.toggle", null);
    }

    bool quickPending;

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

    // ───────────────────────────── Sistema ─────────────────────────────

    void OnPower(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) { Ui(FlushPlay); return; }   // el rato dormida no cuenta como juego
        if (e.Mode != PowerModes.Resume) return;
        Ui(() =>
        {
            if (playKey != null && tracker != null && tracker.IsReady && !GameFreezer.IsFrozen) playStart = DateTime.UtcNow;
            if (cfg.HideCursor) CursorHider.Hide(force: true);   // al despertar, Windows regresa los cursores
            TopMost = false;
            ShowUi();
            Emit("system.resumed", null);
        });
    }

    void OnDisplay(object sender, EventArgs e)
    {
        Ui(() =>
        {
            Bounds = Screen.PrimaryScreen.Bounds;
            if (cfg.HideCursor) CursorHider.Hide(force: true);
            quick?.Refresh("cambió la pantalla");
            bool tv = Screen.AllScreens.Length > 1 || Screen.PrimaryScreen.DeviceName != cfg.InternalDisplay;
            Emit("display.changed", new { tv });
        });
    }

    // ───── Cuentas y arranque desde la interfaz (la ventana de Windows se congelaba detrás de la pantalla completa) ─────

    object SetupState() => new
    {
        steamId = cfg.SteamId,
        steamKey = !string.IsNullOrEmpty(cfg.SteamKey),
        sgdbKey = !string.IsNullOrEmpty(cfg.SgdbKey),
        autostart = Startup.IsAutostart(),
        gamebar = Startup.IsGameBarButtonOff()
    };

    object SetupSet(string field, JsonElement p)
    {
        string text = (P.S(p, "value") ?? "").Trim();
        switch (field)
        {
            case "steamId":
                if (!Regex.IsMatch(text, @"^7656119\d{10}$")) throw new InvalidOperationException("El SteamID64 son 17 dígitos y empieza con 7656119.");
                cfg.SteamId = text;
                break;
            case "steamKey":
                text = text.ToUpperInvariant();
                if (!Regex.IsMatch(text, "^[0-9A-F]{32}$")) throw new InvalidOperationException("La API key de Steam son 32 caracteres (0-9 y A-F).");
                cfg.SteamKey = text;
                break;
            case "sgdbKey":
                if (!Regex.IsMatch(text, "^[0-9A-Za-z]{20,64}$")) throw new InvalidOperationException("Esa API key de SteamGridDB no parece válida.");
                cfg.SgdbKey = text;
                break;
            case "autostart":
                Startup.SetAutostart(P.B(p, "value"));
                break;
            case "gamebar":
                bool on = P.B(p, "value");
                if (on && !Startup.IsGameBarButtonOff()) Startup.SetGameBarButtonOff(true, cfg);
                else if (!on && cfg.GameBarNexusBackup != null) Startup.SetGameBarButtonOff(false, cfg);
                break;
            default: throw new NotSupportedException("Ajuste desconocido: " + field);
        }
        cfg.SetupDone = true;
        cfg.Save();
        Log.Write("ajuste guardado: " + field);
        return SetupState();
    }

    void OpenSetup()
    {
        BeginInvoke(new Action(() =>
        {
            TopMost = false;
            using var f = new SetupForm(cfg);
            if (f.ShowDialog(this) == DialogResult.OK)
            {
                steam.Reset();
                web.CoreWebView2?.Reload();
            }
            ShowUi();
        }));
    }
}
