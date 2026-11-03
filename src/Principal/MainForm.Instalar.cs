using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace XboxScarlet;

/// <summary>Instalar, cancelar y desinstalar directo con Steam (sin sus ventanas).</summary>
sealed partial class MainForm
{
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
        // ¿Instalación nueva o actualización? Una actualización cancelada nunca desinstala el juego.
        bool fresh = before == null || !before.FullyInstalled;
        GuardSteam(30);
        string how = await SteamCdp.CancelDownloadAsync(appid, fresh);
        Log.Write($"Steam: cancelar {(fresh ? "instalación" : "actualización")} de {appid} ({how ?? "falló"})");
        if (how == null) { GuardSteam(3); return new { ok = false, error = "Steam no contestó." }; }
        // ¿Ya no está en las descargas de Steam? (hasta ~6 s)
        bool stopped = false;
        for (int i = 0; i < 12 && !stopped; i++)
        {
            await Task.Delay(500);
            var (live, _, ok) = await SteamCdp.DownloadsAsync();
            stopped = ok && live != null && !live.Any(d => d.AppId == appid && d.State != "needs");
        }
        if (!stopped) { GuardSteam(3); Log.Write($"Steam: {appid} sigue en sus descargas tras cancelar"); return new { ok = false, error = "Steam no la quitó de sus descargas. Intenta de nuevo en unos segundos." }; }
        Emit("download.removed", new { appid });
        if (!fresh) { GuardSteam(3); Log.Write($"Steam: actualización de {appid} quitada de la lista (el juego sigue instalado)"); return new { ok = true, update = true }; }
        steam.Canceled[appid] = true;
        Ui(() => steam.Installed.Remove(appid));
        Log.Write($"Steam: descarga de {appid} detenida y quitada de la lista");
        _ = WipeCanceledAsync(appid, before);
        return new { ok = true };
    }

    // ───── Desinstalar sin la ventanita de Steam ─────
    readonly HashSet<long> uninstalling = new();

    /// <summary>
    /// Desinstalar (ya confirmado en Scarlet): se le pide a Steam sin preguntar y se confirma mirando que su manifiesto
    /// desaparezca. Si Steam no hace caso en ~45 s, plan B: Scarlet lo borra con Steam cerrado unos segundos (espera a que
    /// cierres el juego si hay uno abierto). Sin conexión directa queda la ventanita de Steam (avisándolo).
    /// Contesta al momento; el final llega con game.uninstalled {appid, ok, freed}.
    /// </summary>
    async Task<object> Uninstall(long appid)
    {
        if (appid <= 0) return new { ok = false, error = "Juego no válido" };
        var app = steam.LocalApps().FirstOrDefault(a => a.AppId == appid);
        if (app == null) { Emit("game.uninstalled", new { appid, ok = true, freed = 0L }); return new { ok = true, mode = "gone" }; }
        if (tracker != null && tracker.Active && tracker.AppId == appid) return new { ok = false, error = "Cierra el juego antes de desinstalarlo." };
        if (!uninstalling.Add(appid)) return new { ok = true, mode = "busy" };
        if (!SteamCdp.MarkerPresent(steam.SteamPath) || !await SteamCdp.ReadyAsync())
        {
            uninstalling.Remove(appid);
            _ = SteamDialogAsync("steam://uninstall/" + appid, "desinstalar");
            return new { ok = true, mode = "steam" };
        }
        _ = UninstallRunAsync(app);
        return new { ok = true, mode = "cdp" };
    }

    async Task UninstallRunAsync(LocalApp app)
    {
        long appid = app.AppId, freed = Math.Max(app.SizeOnDisk, 0);
        bool Gone() => !steam.LocalApps().Any(a => a.AppId == appid);
        try
        {
            GuardSteam(60);
            // Si se estaba descargando o actualizando, primero se quita de la lista (si no, Steam no la suelta).
            await SteamCdp.CancelDownloadAsync(appid, false);
            await SteamCdp.UninstallAsync(appid);
            for (int i = 0; i < 45 && !Gone(); i++) await Task.Delay(1000);
            if (!Gone())
            {
                Log.Write($"Steam: no desinstaló {appid} en 45 s; Scarlet lo borra con Steam cerrado");
                while (tracker != null && tracker.Active) await Task.Delay(3000);
                while (reconnecting || enabling) await Task.Delay(2000);
                Emit("game.uninstalled", new { appid, ok = false, pending = true, state = "restarting" });
                reconnecting = true; lastReconnect = DateTime.Now;
                string gone = null;
                try { await SteamCdp.RestartSteamAsync(steam.SteamPath, () => Ui(StartSteamSilently), () => gone = WipeFiles(app, full: true)); }
                finally { reconnecting = false; }
                Log.Write($"Steam: borrado de {appid}: {gone ?? "Steam no se cerró"}");
            }
            bool ok = Gone();
            if (ok) Ui(() => steam.Installed.Remove(appid));
            Log.Write($"Steam: {appid} " + (ok ? "desinstalado" : "no se pudo desinstalar"));
            Emit("game.uninstalled", new { appid, ok, freed });
            _ = Task.Delay(3000).ContinueWith(_ => Emit("library.changed", new { }));
        }
        catch (Exception ex) { Log.Error("desinstalar", ex); Emit("game.uninstalled", new { appid, ok = false, freed = 0L }); }
        finally { uninstalling.Remove(appid); GuardSteam(3); }
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
    static string WipeFiles(LocalApp app, bool full = false)
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
        if (full)
        {
            Del(Path.Combine(steamapps, "shadercache", app.AppId.ToString()), true);
            Del(Path.Combine(steamapps, "workshop", "content", app.AppId.ToString()), true);
            Del(Path.Combine(steamapps, "workshop", $"appworkshop_{app.AppId}.acf"), false);
        }
        Del(Path.Combine(steamapps, $"appmanifest_{app.AppId}.acf"), false);
        return did.Count > 0 ? "se borró " + string.Join(", ", did) : "no había archivos";
    }
}
