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
            cw.Navigate("https://app.scarlet/index.html");

            // Menú rápido que se abre encima de los juegos (se prepara oculto).
            try
            {
                quick = new QuickOverlay(OnMessage);
                await quick.InitAsync(env, Paths.Web, cfg.DevTools);
            }
            catch (Exception ex) { Log.Error("menú rápido", ex); quick = null; }

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
        // Si saliste de Scarlet por otro lado (selector de ventanas, Alt+Tab), lo que estaba silenciado vuelve a sonar.
        var front = Native.GetForegroundWindow();
        if (front != Handle && !(quick != null && front == quick.Handle))
        {
            AudioFocus.RestoreAll();
            if (GameFreezer.IsFrozen && tracker != null && front == tracker.Window && GameFreezer.Resume()) playStart = DateTime.UtcNow;
        }
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
            object data = await HandleMessage(type, payload);
            SendTo(from, new Dictionary<string, object> { ["id"] = id, ["ok"] = true, ["data"] = data });
        }
        catch (Exception ex)
        {
            Log.Error("mensaje " + type, ex);
            SendTo(from, new Dictionary<string, object> { ["id"] = id, ["ok"] = false, ["error"] = ex.Message });
        }
    }

    async Task<object> HandleMessage(string type, JsonElement p)
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
                foreach (var e in lastGames) e["pause"] = cfg.PauseOnHome.Contains((long)e["appid"]);
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
            case "media.state": return media.State;
            case "app.info": return new { startup, updated, rolledBack = UpdateService.TakeRolledBackNotice(), version = Application.ProductVersion };
            case "update.check": return await UpdateService.CheckAsync();
            case "update.install":
                return await UpdateService.InstallAsync(pr => Emit("update.progress", new { p = pr }), () => BeginInvoke(new Action(() => { Log.Write("cerrando para actualizar"); Close(); })));
            case "game.pausepref":
            {
                long id = P.L(p, "appid");
                cfg.PauseOnHome.Remove(id);
                if (P.B(p, "on")) cfg.PauseOnHome.Add(id);
                cfg.Save();
                return true;
            }
            case "media.cmd": return await media.CommandAsync(P.S(p, "cmd"), P.D(p, "value"));
            case "audio.mutebehind":
                cfg.MuteBehind = P.B(p, "on"); cfg.Save();
                if (!cfg.MuteBehind) AudioFocus.RestoreAll();
                return true;
            case "game.state": return new { appid = tracker.Active ? tracker.AppId : 0, ready = tracker.IsReady };
            case "game.close": GameFreezer.Resume(); FlushPlay(); playKey = null; tracker.Close(); steam.SetRunning(0); AudioFocus.RestoreAll(); return true;   // congelado no podría cerrarse bien
            case "game.uninstall": Shell.Open("steam://uninstall/" + P.L(p, "appid")); return true;

            // ── Ventana y notificaciones
            case "ui.show": ShowUi(); return true;
            case "quick.close": quick?.HideNow(true, P.S(p, "why")); return true;
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
            case "system.state": return await sys.StateAsync();
            case "system.brightness": { int v = (int)P.L(p, "value"); _ = Task.Run(() => SystemService.SetBrightness(v)); return true; }
            case "system.volume": SystemService.SetVolume((int)P.L(p, "value")); return true;
            case "system.power": SystemService.SetPower(P.S(p, "profile")); return true;
            case "system.radio": await sys.SetRadiosAsync(P.B(p, "wifi"), P.B(p, "airplane")); return true;
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

    /// <summary>Sonidos propios que pusiste en la carpeta "sounds" (nombre sin extensión → dirección local).</summary>
    static Dictionary<string, string> SoundsList()
    {
        var map = new Dictionary<string, string>();
        try
        {
            if (!Directory.Exists(SoundsDir)) return map;
            foreach (var f in Directory.EnumerateFiles(SoundsDir))
                if (Regex.IsMatch(Path.GetExtension(f), @"^\.(wav|mp3|ogg|m4a)$", RegexOptions.IgnoreCase))
                    map[Path.GetFileNameWithoutExtension(f).ToLowerInvariant()] = ArtCache.LocalUrl(f);
            if (map.Count > 0) Log.Write("sonidos propios: " + string.Join(", ", map.Keys));
        }
        catch (Exception ex) { Log.Error("sonidos", ex); }
        return map;
    }

    bool Allowed(string path) =>
        Paths.Under(path, SoundsDir) || Paths.Under(path, Paths.Cache) || Paths.Under(path, steam.SteamPath) || album.Roots().Any(r => Paths.Under(path, r));

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
        _ => "application/octet-stream"
    };

    // ───────────────────────────── Juegos ─────────────────────────────

    async Task LaunchAsync(long appid, string source)
    {
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
            a.Start();
            BeginGame(appid, false);
        }
        else if (source == "xbox")
        {
            var g = xbox.Find(appid) ?? throw new InvalidOperationException("No encontré ese juego de Xbox");
            cfg.AppLastUsed["xbox:" + g.Pfn] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            cfg.Save();
            playKey = "xbox:" + g.Pfn;
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

    void BeginGame(long appid, bool isSteam)
    {
        tracker.Begin(appid, isSteam, isSteam ? steam.InstallDirOf(appid) : null);
        if (isSteam) steam.SetRunning(appid);
    }

    async void OnGameReady(long appid)
    {
        if (playKey != null) playStart = DateTime.UtcNow;
        Emit("game.ready", new { appid });
        await Task.Delay(900);   // la interfaz desvanece la pantalla de carga
        TopMost = false;
        if (!tracker.Focus()) Native.SendToBottom(Handle);
        HideSoon();
    }

    void OnGameExited(long appid)
    {
        GameFreezer.Forget();
        FlushPlay(); playKey = null;
        steam.SetRunning(0);
        TopMost = false;
        ShowUi();
        Emit("game.exited", new { appid });
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

    /// <summary>Si marcaste el juego para pausarse, se congela mientras estás en el Inicio (vuelve a correr al regresar).</summary>
    void PauseGameIfMarked()
    {
        if (tracker == null || !tracker.Active || !tracker.IsReady || !cfg.PauseOnHome.Contains(tracker.AppId)) return;
        if (GameFreezer.Suspend(tracker.Pid, tracker.AppId)) FlushPlay();
    }

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

    async Task OpenSystemAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return;
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
                if (stuckLaunch) return true;
                Native.ShowWindow(h, 0 /* SW_HIDE */);
                if (hiddenSteam.Add(h)) Log.Write($"ventana de Steam escondida: \"{Native.Title(h)}\"");
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) { Log.Error("ventanas de Steam", ex); }
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
        var fg = Native.GetForegroundWindow();
        if (fg == Handle && web.Visible) { Log.Write("toque: menú rápido de la interfaz"); Emit("quick.toggle", null); return; }
        if (IsGameBar(fg)) fg = beforeGuide;
        if (quick != null && quick.ShowOver(fg)) { xin.ForwardKeys = true; WarnExclusive(); return; }
        Log.Write("toque: menú rápido en la interfaz (el panel no está listo)");
        BringUiToFront(); Emit("quick.toggle", null);
    }

    void OnGuideDouble()
    {
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
    async void BringUiToFront()
    {
        IntPtr game = Native.GetForegroundWindow();
        // Silenciar el foco: solo cuando tú vas al menú (no cuando un juego se cierra y queda otra app al frente).
        if (cfg.MuteBehind && game != IntPtr.Zero && game != Handle && !(quick != null && game == quick.Handle)) AudioFocus.MuteWindow(game);
        ShowUi();
        await Task.Delay(150);
        if (Native.GetForegroundWindow() == Handle) { PauseGameIfMarked(); return; }
        IntPtr now = Native.GetForegroundWindow();
        Log.Write($"Scarlet no pudo pasar al frente: {Native.Describe(now)} no suelta la pantalla; se minimiza{(Native.ExclusiveFullscreen() ? " (pantalla completa exclusiva)" : "")}");
        WarnExclusive();
        if (now != IntPtr.Zero && now != Handle && !(quick != null && now == quick.Handle)) Native.Minimize(now);
        else if (game != IntPtr.Zero && game != Handle) Native.Minimize(game);
        await Task.Delay(120);
        TopMost = true;
        bool ok = Native.ForceForeground(Handle);
        web.Focus();
        await Task.Delay(400);
        TopMost = false;   // solo un instante: luego el juego debe poder volver al frente
        if (Native.GetForegroundWindow() == Handle) PauseGameIfMarked();   // congelar solo si Scarlet de verdad quedó al frente
        else if (!ok) Log.Write($"Scarlet sigue detrás de {Native.Describe(Native.GetForegroundWindow())}");
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
