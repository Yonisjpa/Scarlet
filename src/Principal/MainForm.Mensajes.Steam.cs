using Microsoft.Web.WebView2.Core;

namespace XboxScarlet;

/// <summary>Mensajes de la interfaz · Steam: perfil, biblioteca, amigos, logros, noticias, instalar, descargas y portadas.</summary>
sealed partial class MainForm
{
    async Task<object> SteamMessage(string type, JsonElement p, CoreWebView2 from)
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
                lastGames = WithExternalGames(lastGames);
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
            // Perfil (beta 1.2): tu código de amigo, buscar a alguien por su código y mandarle la solicitud con Steam.
            case "steam.me": return new { code = steam.AccountId, steamid = steam.SteamId };
            case "steam.lookup": return await steam.LookupAsync(P.S(p, "code"));
            case "steam.addfriend":
            {
                string sid = P.S(p, "steamid");
                if (string.IsNullOrEmpty(sid) || !Regex.IsMatch(sid, @"^7656\d{13}$")) return false;
                Log.Write($"amigos: se manda solicitud a {sid} con Steam");
                Shell.Open("steam://friends/add/" + sid);
                return true;
            }
            case "steam.friends.refresh": return await steam.FriendsAsync(true);
            case "steam.achievements": return await steam.AchievementsAsync(P.L(p, "appid"));
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
            // Fondo de la pantalla del juego con una imagen tuya (beta 1.5)
            case "art.file":
            {
                string url = await Task.Run(() => ArtCache.CopyOwn(P.S(p, "path"), "fondo"));
                return url == null ? new { ok = false, error = "No se pudo usar esa imagen" } : (object)new { ok = true, url };
            }
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
            case "download.now": return new { ok = await SteamCdp.UpdateNowAsync(P.L(p, "appid")) };
            case "install.enable": return InstallEnable();
            default: throw new NotSupportedException("Mensaje desconocido: " + type);
        }
    }
}
