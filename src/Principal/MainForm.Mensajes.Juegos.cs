using Microsoft.Web.WebView2.Core;

namespace XboxScarlet;

/// <summary>Mensajes de la interfaz · Juegos: abrir, cerrar, opciones, juegos externos, explorador de archivos y compatibilidad.</summary>
sealed partial class MainForm
{
    async Task<object> GamesMessage(string type, JsonElement p, CoreWebView2 from)
    {
        switch (type)
        {
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
            case "game.pausepref":
            {
                long id = P.L(p, "appid");
                cfg.NoPauseOnHome.Remove(id);
                if (!P.B(p, "on")) cfg.NoPauseOnHome.Add(id);
                cfg.Save();
                return true;
            }
            // ── Juegos externos (beta 1.1)
            case "fs.list": return await Task.Run(() => ExternalGames.List(P.S(p, "path"), P.B(p, "images")));
            case "ext.add":
            {
                var (g, err) = await Task.Run(() => ExternalGames.Add(cfg, P.S(p, "path")));
                return err != null ? new { ok = false, error = err } : new { ok = true, appid = g.Id, name = g.Name };
            }
            case "ext.update":
            {
                var g = cfg.ExternalGames.FirstOrDefault(x => x.Id == P.L(p, "appid"));
                if (g == null) return new { ok = false, error = "Ya no está ese juego" };
                if (p.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String && nm.GetString().Trim().Length > 0) g.Name = nm.GetString().Trim();
                if (p.TryGetProperty("args", out var ar) && ar.ValueKind == JsonValueKind.String) g.Args = ar.GetString().Trim();
                if (p.TryGetProperty("path", out var pa) && pa.ValueKind == JsonValueKind.String && File.Exists(pa.GetString()))
                { g.Path = pa.GetString(); g.Target = g.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ExternalGames.ShortcutTarget(g.Path) : g.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? g.Path : null; }
                cfg.Save();
                Log.Write($"juego externo {g.Id}: «{g.Name}» {g.Path} {g.Args}");
                return new { ok = true };
            }
            case "ext.remove":
            {
                long id = P.L(p, "appid");
                int n = cfg.ExternalGames.RemoveAll(x => x.Id == id);
                // Sus horas y su «última vez» se van con él (no las hereda otro juego).
                cfg.PlaySeconds.Remove("ext:" + id); cfg.AppLastUsed.Remove("ext:" + id);
                try { foreach (var f in Directory.GetFiles(Path.Combine(Paths.Cache, "externos"), $"*_{id}*")) File.Delete(f); } catch { }
                cfg.Save();
                if (n > 0) Log.Write($"juego externo {id} quitado de Scarlet (no se borró nada del disco)");
                return n > 0;
            }
            case "ext.art":
            {
                // mode: "icon" (el ícono del programa), "file" (una imagen tuya: path) o "clear" (de vuelta a SteamGridDB)
                var g = cfg.ExternalGames.FirstOrDefault(x => x.Id == P.L(p, "appid"));
                if (g == null) return new { ok = false, error = "Ya no está ese juego" };
                string mode = P.S(p, "mode"), file = null;
                try
                {
                    if (mode == "icon") file = await Task.Run(() => ExternalGames.ExtractIcon(g));
                    else if (mode == "file") file = await Task.Run(() => ExternalGames.CopyImage(g, P.S(p, "path")));
                }
                catch (Exception ex) { Log.Error("portada externa", ex); }
                if (mode != "clear" && file == null) return new { ok = false, error = mode == "icon" ? "Ese programa no trae ícono" : "No se pudo usar esa imagen" };
                g.Image = file; g.IconStyle = mode == "icon";
                cfg.Save();
                return new { ok = true, url = file != null ? ArtCache.LocalUrl(file) : null, icon = g.IconStyle };
            }
            case "game.state": return new { appid = tracker.Active ? tracker.AppId : 0, ready = tracker.IsReady };
            case "game.detach":   // dejar la app abierta en segundo plano (música, YouTube) y dejar de seguirla
                GameFreezer.Resume(); FlushPlay(); playKey = null;
                detachedApp = tracker.AppId;
                Log.Write($"{tracker.AppId} queda abierta en segundo plano");
                tracker.Stop(); steam.SetRunning(0); AudioFocus.RestoreAll();
                return true;
            case "game.close": { long was = tracker.AppId; GameFreezer.Resume(); FlushPlay(); playKey = null; tracker.Close(); WatchCloud(was); } steam.SetRunning(0); AudioFocus.RestoreAll(); return true;   // congelado no podría cerrarse bien
            case "game.uninstall": return await Uninstall(P.L(p, "appid"));
            case "game.kill": tracker.Kill(); return true;
            case "game.wait": hungSnooze = DateTime.Now.AddMinutes(2); hungAsked = false; FocusGame(); return true;
            case "game.menufocus":
            {
                long id = P.L(p, "appid");
                cfg.MenuKeepsFocus.Remove(id);
                if (P.B(p, "on")) cfg.MenuKeepsFocus.Add(id);
                cfg.Save();
                return true;
            }
            default: throw new NotSupportedException("Mensaje desconocido: " + type);
        }
    }
}
