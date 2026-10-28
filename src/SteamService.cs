using System.Collections.Concurrent;
namespace XboxScarlet;

/// <summary>Un juego instalado según su appmanifest_*.acf</summary>
sealed class LocalApp
{
    public long AppId;
    public string Name, InstallDir, LibraryPath;
    public long SizeOnDisk, BytesToDownload, BytesDownloaded, StateFlags, LastPlayed;
    public bool FullyInstalled => (StateFlags & 4) != 0;
    public string FullInstallPath => Path.Combine(LibraryPath, "steamapps", "common", InstallDir ?? "");
}

sealed class Friend
{
    public string steamid { get; set; }
    public string name { get; set; }
    public string status { get; set; }
    public string game { get; set; }
    public string avatar { get; set; }
    public string last { get; set; }
    // Solo para uso interno (no se mandan a la interfaz):
    public string Lobby;
    public string Server;
}

/// <summary>Todo lo de Steam: archivos locales (sin internet) y la Steam Web API (con tu API key).</summary>
sealed class SteamService
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(12) };
    const string API = "https://api.steampowered.com/";
    const long SteamIdBase = 76561197960265728;

    readonly Config cfg;
    public string SteamPath { get; }

    public SteamService(Config cfg)
    {
        this.cfg = cfg;
        SteamPath = FindSteamPath();
        if (string.IsNullOrEmpty(cfg.SteamId)) cfg.SteamId = DetectSteamId(SteamPath) ?? "";
    }

    public string SteamId => cfg.SteamId ?? "";
    public long AccountId => long.TryParse(SteamId, out var id) && id > SteamIdBase ? id - SteamIdBase : 0;
    string Key => cfg.SteamKey;
    public bool HasKey => !string.IsNullOrWhiteSpace(Key) && AccountId > 0;

    public event Action LibraryChanged;
    public event Action<Friend> FriendOnline;
    public event Action FriendsChanged;
    public event Action<long, string, string, string> AchievementUnlocked;   // appid, nombre, descripción, ícono
    public event Action<long, double, string> DownloadProgress;
    public event Action<long> DownloadDone;

    /// <summary>Olvida lo descargado (tras cambiar claves).</summary>
    public void Reset() { owned = null; friends = new List<Friend>(); }

    // ───────────────────────────── Steam en este equipo ─────────────────────────────

    public static string FindSteamPath()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (k?.GetValue("SteamPath") is string p && p.Length > 0) return Path.GetFullPath(p.Replace('/', '\\'));
        }
        catch { }
        foreach (var c in new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" })
            if (Directory.Exists(c)) return c;
        return null;
    }

    public static string DetectSteamId(string steamPath)
    {
        if (steamPath == null) return null;
        var users = Vdf.Load(Path.Combine(steamPath, "config", "loginusers.vdf"))?.Obj("users");
        if (users == null) return null;
        string first = null;
        foreach (var kv in users)
        {
            if (kv.Value is not Dictionary<string, object> u) continue;
            first ??= kv.Key;
            if (u.Str("MostRecent") == "1") return kv.Key;
        }
        return first;
    }

    public string LocalPersonaName()
    {
        if (SteamPath == null) return null;
        return Vdf.Load(Path.Combine(SteamPath, "config", "loginusers.vdf"))?.Obj("users")?.Obj(SteamId)?.Str("PersonaName");
    }

    /// <summary>Juego de Steam que está corriendo ahora (0 = ninguno).</summary>
    public long RunningAppId()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return Convert.ToInt64(k?.GetValue("RunningAppID") ?? 0);
        }
        catch { return 0; }
    }

    public List<string> Libraries()
    {
        var list = new List<string>();
        if (SteamPath == null) return list;
        list.Add(SteamPath);
        var lf = Vdf.Load(Path.Combine(SteamPath, "steamapps", "libraryfolders.vdf"))?.Obj("libraryfolders");
        if (lf == null) return list;
        foreach (var kv in lf)
        {
            string path = kv.Value is Dictionary<string, object> o ? o.Str("path") : kv.Value as string;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) continue;
            if (!list.Any(x => string.Equals(Path.GetFullPath(x).TrimEnd('\\'), Path.GetFullPath(path).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                list.Add(path);
        }
        return list;
    }

    static readonly HashSet<long> ToolIds = new() { 228980, 1070560, 1391110, 1628350, 1493710, 1826330, 2180100, 2348590, 961940, 1887720, 1420170, 250820, 1054830, 1580130, 2805730, 3658110 };

    public static bool IsTool(long id, string name)
    {
        if (ToolIds.Contains(id)) return true;
        return !string.IsNullOrEmpty(name) && Regex.IsMatch(name, @"(Redistributable|Steam Linux Runtime|^Proton|Steamworks|SteamVR|Dedicated Server|\bSDK\b)", RegexOptions.IgnoreCase);
    }

    public List<LocalApp> LocalApps()
    {
        var res = new List<LocalApp>();
        foreach (var lib in Libraries())
        {
            string dir = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "appmanifest_*.acf"))
            {
                var a = Vdf.Load(f)?.Obj("AppState");
                if (a == null) continue;
                var app = new LocalApp
                {
                    AppId = a.Long("appid"), Name = a.Str("name"), InstallDir = a.Str("installdir"), LibraryPath = lib,
                    SizeOnDisk = a.Long("SizeOnDisk"), BytesToDownload = a.Long("BytesToDownload"), BytesDownloaded = a.Long("BytesDownloaded"),
                    StateFlags = a.Long("StateFlags"), LastPlayed = a.Long("LastPlayed")
                };
                if (app.AppId > 0 && !IsTool(app.AppId, app.Name)) res.Add(app);
            }
        }
        return res;
    }

    public string InstallDirOf(long appid)
    {
        if (!Installed.TryGetValue(appid, out var a))
            foreach (var x in LocalApps()) Installed[x.AppId] = x;
        return Installed.TryGetValue(appid, out a) ? a.FullInstallPath : null;
    }

    Dictionary<long, (long last, long minutes)> LocalPlaytimes()
    {
        var res = new Dictionary<long, (long, long)>();
        if (AccountId == 0 || SteamPath == null) return res;
        var apps = Vdf.Load(Path.Combine(SteamPath, "userdata", AccountId.ToString(), "config", "localconfig.vdf"))
            ?.Obj("UserLocalConfigStore")?.Obj("Software")?.Obj("Valve")?.Obj("Steam")?.Obj("apps");
        if (apps == null) return res;
        foreach (var kv in apps)
            if (long.TryParse(kv.Key, out var id) && kv.Value is Dictionary<string, object> o)
                res[id] = (o.Long("LastPlayed"), o.Long("Playtime"));
        return res;
    }

    /// <summary>Tus colecciones de la biblioteca de Steam (Favoritos y las que hayas creado).</summary>
    Dictionary<long, List<string>> Collections()
    {
        var res = new Dictionary<long, List<string>>();
        try
        {
            if (AccountId == 0 || SteamPath == null) return res;
            string f = Path.Combine(SteamPath, "userdata", AccountId.ToString(), "config", "cloudstorage", "cloud-storage-namespace-1.json");
            if (!File.Exists(f)) return res;
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 2) continue;
                string key = e[0].GetString() ?? "";
                var o = e[1];
                if (!key.StartsWith("user-collections.") || o.ValueKind != JsonValueKind.Object) continue;
                if (o.TryGetProperty("is_deleted", out var del) && del.ValueKind == JsonValueKind.True) continue;
                if (!o.TryGetProperty("value", out var val) || val.ValueKind != JsonValueKind.String) continue;
                using var c = JsonDocument.Parse(val.GetString() ?? "{}");
                var cr = c.RootElement;
                string id = cr.TryGetProperty("id", out var idv) ? idv.GetString() : "";
                if (id == "hidden") continue;
                string name = id == "favorite" ? "Favoritos" : cr.TryGetProperty("name", out var nv) ? nv.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) || !cr.TryGetProperty("added", out var added) || added.ValueKind != JsonValueKind.Array) continue;
                foreach (var a in added.EnumerateArray())
                {
                    if (!a.TryGetInt64(out var appid)) continue;
                    if (!res.TryGetValue(appid, out var l)) res[appid] = l = new List<string>();
                    if (!l.Contains(name)) l.Add(name);
                }
            }
        }
        catch (Exception ex) { Log.Error("colecciones", ex); }
        return res;
    }

    // ───────────────────────────── Steam Web API ─────────────────────────────

    async Task<JsonDocument> Api(string pathAndQuery)
    {
        string sep = pathAndQuery.Contains('?') ? "&" : "?";
        using var r = await http.GetAsync(API + pathAndQuery + sep + "key=" + Uri.EscapeDataString(Key));
        if (!r.IsSuccessStatusCode) throw new HttpRequestException("Steam API " + (int)r.StatusCode, null, r.StatusCode);
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync());
    }

    static string Str(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static string Nz(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    static long Num(JsonElement e, string k)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(k, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetInt64(out var n) ? n : 0;
        return long.TryParse(v.GetString(), out var m) ? m : 0;
    }

    static T LoadCache<T>(string name) where T : class
    {
        try
        {
            string f = Path.Combine(Paths.Cache, name);
            return File.Exists(f) ? JsonSerializer.Deserialize<T>(File.ReadAllText(f)) : null;
        }
        catch { return null; }
    }

    static void SaveCache(string name, object o)
    {
        try { File.WriteAllText(Path.Combine(Paths.Cache, name), JsonSerializer.Serialize(o)); }
        catch (Exception ex) { Log.Error("cache " + name, ex); }
    }

    // ───── Juegos ─────

    sealed class Owned { public long AppId { get; set; } public string Name { get; set; } public long Minutes { get; set; } public long LastPlayed { get; set; } }
    sealed class AchCount { public int U { get; set; } public int T { get; set; } public long Checked { get; set; } }

    List<Owned> owned;
    readonly Dictionary<long, AchCount> ach = LoadCache<Dictionary<long, AchCount>>("ach.json") ?? new Dictionary<long, AchCount>();
    public readonly Dictionary<long, string> Names = new();
    public readonly Dictionary<long, LocalApp> Installed = new();

    async Task<List<Owned>> OwnedAsync(bool refresh)
    {
        if (owned != null && !refresh) return owned;
        if (HasKey && NetCheck.Internet)   // sin internet: lo guardado, al instante (no esperar a que falle la conexión)
        {
            try
            {
                using var d = await Api($"IPlayerService/GetOwnedGames/v1/?steamid={SteamId}&include_appinfo=1&include_played_free_games=1&format=json");
                var list = new List<Owned>();
                if (d.RootElement.TryGetProperty("response", out var resp) && resp.TryGetProperty("games", out var gs))
                    foreach (var g in gs.EnumerateArray())
                        list.Add(new Owned { AppId = Num(g, "appid"), Name = Str(g, "name"), Minutes = Num(g, "playtime_forever"), LastPlayed = Num(g, "rtime_last_played") });
                owned = list;
                SaveCache("owned.json", list);
                return list;
            }
            catch (Exception ex) { Log.Error("juegos (API)", ex); }
        }
        return owned = LoadCache<List<Owned>>("owned.json") ?? new List<Owned>();
    }

    static Dictionary<string, object> Entry(long id, string name, long minutes, long last) => new()
    {
        ["appid"] = id, ["name"] = string.IsNullOrEmpty(name) ? "App " + id : name, ["source"] = "steam", ["kind"] = "game",
        ["installed"] = false, ["hours"] = Math.Round(minutes / 60.0, 1), ["lastTs"] = last, ["size"] = 0.0
    };

    /// <summary>Lista completa para la interfaz: lo instalado (archivos locales) + lo que tienes en tu cuenta (API) + tus apps.</summary>
    public async Task<List<Dictionary<string, object>>> GamesAsync(List<AppEntry> apps, Dictionary<string, long> appLast)
    {
        var local = LocalApps();
        var times = LocalPlaytimes();
        var cols = Collections();
        var own = await OwnedAsync(false);

        Installed.Clear();
        foreach (var a in local) Installed[a.AppId] = a;

        var map = new Dictionary<long, Dictionary<string, object>>();
        foreach (var o in own)
            if (!IsTool(o.AppId, o.Name)) map[o.AppId] = Entry(o.AppId, o.Name, o.Minutes, o.LastPlayed);

        foreach (var a in local)
        {
            if (!map.TryGetValue(a.AppId, out var e)) map[a.AppId] = e = Entry(a.AppId, a.Name, 0, a.LastPlayed);
            e["installed"] = a.FullyInstalled;
            e["size"] = Math.Round(a.SizeOnDisk / 1073741824.0, 1);
            e["lastTs"] = Math.Max((long)e["lastTs"], a.LastPlayed);
        }

        foreach (var kv in times)
        {
            if (!map.TryGetValue(kv.Key, out var e)) continue;
            e["lastTs"] = Math.Max((long)e["lastTs"], kv.Value.last);
            if ((double)e["hours"] == 0 && kv.Value.minutes > 0) e["hours"] = Math.Round(kv.Value.minutes / 60.0, 1);
        }

        var list = new List<Dictionary<string, object>>();
        foreach (var e in map.Values)
        {
            long id = (long)e["appid"];
            e["last"] = Rel((long)e["lastTs"]);
            e["cols"] = cols.TryGetValue(id, out var c) ? c : new List<string>();
            e["ach"] = ach.TryGetValue(id, out var ac) ? new[] { ac.U, ac.T } : new[] { 0, 0 };
            Names[id] = e["name"] as string;
            list.Add(e);
        }

        foreach (var a in apps)
        {
            long last = appLast.TryGetValue(a.Id ?? "", out var t) ? t : 0;
            list.Add(new Dictionary<string, object>
            {
                ["appid"] = a.AppId, ["name"] = a.Name ?? a.Id, ["source"] = "app", ["kind"] = "app", ["installed"] = true,
                ["hours"] = 0.0, ["lastTs"] = last, ["last"] = Rel(last), ["ach"] = new[] { 0, 0 }, ["cols"] = new List<string>(), ["size"] = 0.0
            });
        }

        return list.OrderByDescending(e => (long)e["lastTs"]).ThenBy(e => e["name"] as string, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public string NameOf(long appid) =>
        Names.TryGetValue(appid, out var n) ? n : Installed.TryGetValue(appid, out var a) ? a.Name : null;

    // ───── Logros ─────

    sealed class AchResult { public int U; public int T; public List<Dictionary<string, object>> List = new(); }

    sealed class AchIcon { public string On { get; set; } public string Off { get; set; } }

    /// <summary>Imágenes de los logros de un juego (a color y en gris). Se guardan 14 días.</summary>
    async Task<Dictionary<string, AchIcon>> AchIconsAsync(long appid)
    {
        string file = $"schema_{appid}.json";
        var cached = LoadCache<Dictionary<string, AchIcon>>(file);
        string full = Path.Combine(Paths.Cache, file);
        if (cached != null && DateTime.Now - File.GetLastWriteTime(full) < TimeSpan.FromDays(14)) return cached;
        try
        {
            using var d = await Api($"ISteamUserStats/GetSchemaForGame/v2/?appid={appid}&l=spanish");
            var map = new Dictionary<string, AchIcon>();
            if (d.RootElement.TryGetProperty("game", out var g) && g.TryGetProperty("availableGameStats", out var st)
                && st.TryGetProperty("achievements", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var a in arr.EnumerateArray())
                    if (Str(a, "name") is string api)
                        map[api] = new AchIcon { On = Nz(Str(a, "icon")), Off = Nz(Str(a, "icongray")) };
            SaveCache(file, map);
            return map;
        }
        catch (Exception ex) { Log.Error("imágenes de logros " + appid, ex); return cached ?? new Dictionary<string, AchIcon>(); }
    }

    /// <summary>null = sin internet o error temporal; vacío = el juego no tiene logros o el perfil es privado.</summary>
    async Task<AchResult> AchievementsRawAsync(long appid, bool icons = false)
    {
        if (!HasKey) return null;
        try
        {
            using var d = await Api($"ISteamUserStats/GetPlayerAchievements/v1/?steamid={SteamId}&appid={appid}&l=spanish");
            var res = new AchResult();
            var ps = d.RootElement.GetProperty("playerstats");
            if (!ps.TryGetProperty("achievements", out var arr) || arr.ValueKind != JsonValueKind.Array) return res;
            foreach (var a in arr.EnumerateArray())
            {
                bool got = Num(a, "achieved") == 1;
                long t = Num(a, "unlocktime");
                if (got) res.U++;
                string api = Str(a, "apiname") ?? "";
                string name = Str(a, "name");
                res.List.Add(new Dictionary<string, object>
                {
                    ["name"] = string.IsNullOrEmpty(name) ? api : name, ["desc"] = Str(a, "description") ?? "",
                    ["unlocked"] = got, ["date"] = got && t > 0 ? FmtDate(t) : "", ["ts"] = t, ["api"] = api
                });
            }
            res.T = res.List.Count;
            if (icons && res.T > 0)
            {
                var map = await AchIconsAsync(appid);
                foreach (var x in res.List)
                    if (map.TryGetValue((string)x["api"], out var ic)) { x["icon"] = ic.On; x["iconGray"] = ic.Off ?? ic.On; }
            }
            return res;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Forbidden)
        {
            return new AchResult();
        }
        catch (Exception ex) { Log.Error("logros " + appid, ex); return null; }
    }

    void Remember(long appid, AchResult r)
    {
        ach[appid] = new AchCount { U = r.U, T = r.T, Checked = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
    }

    public async Task<object> AchievementsAsync(long appid)
    {
        var r = await AchievementsRawAsync(appid, true);
        if (r == null)
        {
            ach.TryGetValue(appid, out var c);
            return new { unlocked = c?.U ?? 0, total = c?.T ?? 0, list = new List<object>() };
        }
        Remember(appid, r);
        SaveCache("ach.json", ach);
        var list = r.List.OrderByDescending(x => (bool)x["unlocked"]).ThenByDescending(x => (long)x["ts"]).ToList();
        return new { unlocked = r.U, total = r.T, list };
    }

    bool achRunning;

    /// <summary>Al arrancar: cuenta logros de tus juegos recientes (para la pantalla Logros), una vez cada 6 h.</summary>
    public async Task RefreshAchievementCountsAsync(List<long> appids)
    {
        if (!HasKey || achRunning) return;
        achRunning = true;
        bool changed = false;
        try
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var id in appids)
            {
                if (ach.TryGetValue(id, out var c) && now - c.Checked < 6 * 3600) continue;
                var r = await AchievementsRawAsync(id);
                if (r == null) break;   // sin internet: se intenta otro día
                Remember(id, r);
                changed = true;
                await Task.Delay(250);
            }
        }
        finally
        {
            achRunning = false;
            if (changed) { SaveCache("ach.json", ach); LibraryChanged?.Invoke(); }
        }
    }

    long runningApp;
    HashSet<string> runningUnlocked;

    public void SetRunning(long appid) { runningApp = appid; runningUnlocked = null; }

    /// <summary>Mientras juegas: revisa cada minuto si desbloqueaste algo.</summary>
    public async Task PollRunningAchievementsAsync()
    {
        long id = runningApp;
        if (id == 0 || !HasKey || !NetCheck.Internet) return;
        var r = await AchievementsRawAsync(id, true);
        if (r == null || id != runningApp || r.T == 0) return;
        var now = r.List.Where(x => (bool)x["unlocked"]).Select(x => (string)x["api"]).ToHashSet();
        if (runningUnlocked != null)
            foreach (var x in r.List)
                if ((bool)x["unlocked"] && !runningUnlocked.Contains((string)x["api"]))
                    AchievementUnlocked?.Invoke(id, (string)x["name"], (string)x["desc"], x.TryGetValue("icon", out var ic) ? ic as string : null);
        runningUnlocked = now;
        Remember(id, r);
        SaveCache("ach.json", ach);
    }

    // ───── Perfil y avatar ─────

    /// <summary>¿La última consulta del perfil llegó a Steam? (al prender la consola a veces todavía no hay red)</summary>
    public bool ProfileOnline { get; private set; }

    public static Dictionary<string, object> CachedProfile() => LoadCache<Dictionary<string, object>>("profile.json");

    // Steam ha movido estos archivos de servidor varias veces; se prueban en orden.
    static readonly string[] ItemBases =
    {
        "https://shared.akamai.steamstatic.com/community_assets/images/",
        "https://shared.fastly.steamstatic.com/community_assets/images/",
        "https://cdn.akamai.steamstatic.com/steamcommunity/public/images/",
        "https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/",
    };

    static IEnumerable<string> ItemUrls(string p)
    {
        if (string.IsNullOrEmpty(p)) yield break;
        if (p.StartsWith("http", StringComparison.OrdinalIgnoreCase)) { yield return p; yield break; }
        foreach (var b in ItemBases) yield return b + p.TrimStart('/');
    }

    static async Task<string> CacheFirstAsync(ArtCache art, IEnumerable<string> urls, string prefix)
    {
        foreach (var u in urls)
            if (await art.CacheAsync(u, prefix) is string local) return local;
        return null;
    }

    /// <summary>Respaldo: lee tu perfil público y toma el avatar animado (.gif) y el marco tal como los muestra Steam.</summary>
    async Task<(string anim, string frame)> ScrapeProfileItemsAsync()
    {
        try
        {
            string html = await http.GetStringAsync($"https://steamcommunity.com/profiles/{SteamId}/?l=spanish");
            int at = html.IndexOf("playerAvatarAutoSizeInner", StringComparison.Ordinal);
            if (at < 0) return (null, null);
            string block = html.Substring(at, Math.Min(4000, html.Length - at));
            string Find(string pattern) => Regex.Match(block, pattern, RegexOptions.IgnoreCase) is { Success: true } m ? m.Value : null;
            string anim = Find(@"https://[^""'\s]+/items/\d+/[^""'\s]+\.gif");
            string frame = Find(@"https://[^""'\s]+/items/\d+/[^""'\s]+\.png");
            return (anim, frame);
        }
        catch (Exception ex) { Log.Error("perfil público", ex); return (null, null); }
    }

    /// <summary>Nombre, nivel, avatar (animado si tienes uno equipado) y marco. Las imágenes se guardan en disco para verlas sin internet.</summary>
    public async Task<Dictionary<string, object>> FetchProfileAsync(ArtCache art)
    {
        var cached = CachedProfile();
        string name = LocalPersonaName() ?? "Jugador";
        object level = null;
        string avatarRemote = null, animPath = null, frameRemote = null;
        bool online = false;

        if (HasKey)
        {
            try
            {
                using var d = await Api($"ISteamUser/GetPlayerSummaries/v2/?steamids={SteamId}");
                var pl = d.RootElement.GetProperty("response").GetProperty("players");
                if (pl.GetArrayLength() > 0) { name = Str(pl[0], "personaname") ?? name; avatarRemote = Str(pl[0], "avatarfull"); }
                online = true;
            }
            catch (Exception ex) { Log.Error("perfil", ex); }

            if (online)
            {
                try
                {
                    using var d = await Api($"IPlayerService/GetSteamLevel/v1/?steamid={SteamId}");
                    if (d.RootElement.GetProperty("response").TryGetProperty("player_level", out var lv)) level = lv.GetInt64();
                }
                catch { }
                try
                {
                    using var d = await Api($"IPlayerService/GetProfileItemsEquipped/v1/?steamid={SteamId}");
                    var r = d.RootElement.GetProperty("response");
                    // En el avatar animado, "image_large" suele ser una imagen fija; la que se mueve es el .gif (normalmente "image_small").
                    if (r.TryGetProperty("animated_avatar", out var aa) && aa.ValueKind == JsonValueKind.Object)
                    {
                        var opts = new[] { "image_small", "image_large", "movie_webm", "movie_mp4" }.Select(k => Nz(Str(aa, k))).Where(x => x != null).ToList();
                        animPath = opts.FirstOrDefault(x => Regex.IsMatch(x, @"\.(gif|webm|mp4)(\?|$)", RegexOptions.IgnoreCase));
                        Log.Write("avatar animado (Steam): " + string.Join(" | ", opts));
                    }
                    if (r.TryGetProperty("avatar_frame", out var af) && af.ValueKind == JsonValueKind.Object)
                        frameRemote = Nz(Str(af, "image_small")) ?? Nz(Str(af, "image_large"));
                }
                catch (Exception ex) { Log.Error("objetos del perfil", ex); }
            }
        }

        ProfileOnline = online;
        if (!online && cached != null) return cached;   // sin internet: lo guardado

        string avatar = await CacheFirstAsync(art, ItemUrls(animPath), "avatar");
        string frame = await CacheFirstAsync(art, ItemUrls(frameRemote), "frame");
        if (online && (avatar == null || (frameRemote != null && frame == null)))
        {
            var (anim, fr) = await ScrapeProfileItemsAsync();
            avatar ??= await art.CacheAsync(anim, "avatar");
            if (frame == null && frameRemote != null) frame = await art.CacheAsync(fr, "frame");
        }
        Log.Write($"avatar: animado={(animPath ?? "no equipado")} → {(avatar != null ? "ok" : "se usa el normal")}");
        avatar ??= await art.CacheAsync(avatarRemote, "avatar");
        var p = new Dictionary<string, object> { ["name"] = name, ["level"] = level, ["avatarUrl"] = avatar, ["frameUrl"] = frame };
        if (avatar == null && cached != null && cached.TryGetValue("avatarUrl", out var old)) p["avatarUrl"] = old;
        SaveCache("profile.json", p);
        return p;
    }

    // ───── Amigos ─────

    public List<Friend> Friends { get; private set; } = LoadCache<List<Friend>>("friends.json") ?? new List<Friend>();
    List<Friend> friends { get => Friends; set => Friends = value; }

    public async Task<List<Friend>> FriendsAsync(bool emit)
    {
        if (!HasKey) return friends;
        // Sin internet no se sabe quién está conectado: todos "sin conexión" (no un "Jugando" de hace horas).
        if (!NetCheck.Internet) return friends.Select(f => new Friend { steamid = f.steamid, name = f.name, avatar = f.avatar, status = "offline", last = f.last }).ToList();
        try
        {
            using var fl = await Api($"ISteamUser/GetFriendList/v1/?steamid={SteamId}&relationship=friend");
            var ids = new List<string>();
            foreach (var f in fl.RootElement.GetProperty("friendslist").GetProperty("friends").EnumerateArray())
                if (Str(f, "steamid") is string sid) ids.Add(sid);

            var list = new List<Friend>();
            for (int i = 0; i < ids.Count; i += 100)
            {
                using var d = await Api($"ISteamUser/GetPlayerSummaries/v2/?steamids={string.Join(",", ids.Skip(i).Take(100))}");
                foreach (var p in d.RootElement.GetProperty("response").GetProperty("players").EnumerateArray())
                {
                    long st = Num(p, "personastate");
                    string game = Str(p, "gameextrainfo");
                    var f = new Friend
                    {
                        steamid = Str(p, "steamid"), name = Str(p, "personaname") ?? "?", game = game, avatar = Str(p, "avatarmedium"),
                        status = st == 0 ? "offline" : game != null ? "playing" : (st == 3 || st == 4) ? "away" : "online",
                        Lobby = Str(p, "lobbysteamid"), Server = Str(p, "gameserverip")
                    };
                    long lastOff = Num(p, "lastlogoff");
                    if (st == 0 && lastOff > 0) f.last = "Última conexión: " + Ago(lastOff);
                    list.Add(f);
                }
            }

            if (emit && friends.Count > 0)
                foreach (var f in list)
                {
                    var old = friends.FirstOrDefault(x => x.steamid == f.steamid);
                    bool wasOff = old == null || old.status == "offline";
                    if (wasOff && f.status != "offline") FriendOnline?.Invoke(f);
                }

            friends = list;
            SaveCache("friends.json", list);
            if (emit) FriendsChanged?.Invoke();
            return list;
        }
        catch (Exception ex) { Log.Error("amigos", ex); return friends; }
    }

    public async Task<object> FriendProfileAsync(string sid)
    {
        object level = null;
        bool priv = false;
        var common = new List<(long id, string name, double mine, double theirs, long sort)>();
        if (HasKey && !string.IsNullOrEmpty(sid))
        {
            try
            {
                using var d = await Api($"IPlayerService/GetSteamLevel/v1/?steamid={sid}");
                if (d.RootElement.GetProperty("response").TryGetProperty("player_level", out var lv)) level = lv.GetInt64();
            }
            catch { }
            try
            {
                using var d = await Api($"IPlayerService/GetOwnedGames/v1/?steamid={sid}&include_played_free_games=1&format=json");
                var resp = d.RootElement.GetProperty("response");
                if (!resp.TryGetProperty("games", out var gs)) priv = true;
                else
                {
                    var mine = new Dictionary<long, long>();
                    foreach (var o in await OwnedAsync(false)) mine[o.AppId] = o.Minutes;
                    foreach (var g in gs.EnumerateArray())
                    {
                        long id = Num(g, "appid");
                        if (!mine.TryGetValue(id, out var m)) continue;
                        long t = Num(g, "playtime_forever");
                        common.Add((id, NameOf(id) ?? "", Math.Round(m / 60.0, 1), Math.Round(t / 60.0, 1), Math.Min(m, t)));
                    }
                }
            }
            catch { priv = true; }
        }
        var top = common.OrderByDescending(x => x.sort).Take(8).Select(x => new { appid = x.id, name = x.name, mine = x.mine, theirs = x.theirs }).ToList();
        return new { level, common = top, @private = priv };
    }

    /// <summary>Unirse a la partida de un amigo, si Steam lo permite (lobby o servidor).</summary>
    public bool Join(string sid, long appid)
    {
        var f = friends.FirstOrDefault(x => x.steamid == sid);
        if (f == null) return false;
        if (!string.IsNullOrEmpty(f.Lobby)) { Shell.Open($"steam://joinlobby/{appid}/{f.Lobby}/{sid}"); return true; }
        if (!string.IsNullOrEmpty(f.Server)) { Shell.Open($"steam://connect/{f.Server}"); return true; }
        return false;
    }

    // ───── Noticias ─────

    public async Task<object> NewsAsync(List<(long id, string name)> top)
    {
        var all = new List<(long date, object item)>();
        foreach (var (id, name) in top)
        {
            try
            {
                using var r = await http.GetAsync($"{API}ISteamNews/GetNewsForApp/v2/?appid={id}&count=3&maxlength=1500&format=json&feeds=steam_community_announcements");
                if (!r.IsSuccessStatusCode) continue;
                using var d = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                foreach (var n in d.RootElement.GetProperty("appnews").GetProperty("newsitems").EnumerateArray())
                {
                    string text = Clean(Str(n, "contents") ?? "");
                    long date = Num(n, "date");
                    var paras = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length > 1).Take(10).ToList();
                    if (paras.Count == 0) continue;
                    string summary = paras[0].Length > 180 ? paras[0][..177] + "…" : paras[0];
                    all.Add((date, new { appid = id, game = name, title = Str(n, "title") ?? "", summary, body = paras, date = FmtDate(date), url = Str(n, "url") ?? "" }));
                }
            }
            catch (Exception ex) { Log.Error("noticias " + id, ex); }
        }
        if (all.Count == 0) return LoadCache<List<JsonElement>>("news.json") ?? new List<JsonElement>();
        var list = all.OrderByDescending(x => x.date).Take(14).Select(x => x.item).ToList();
        SaveCache("news.json", list);
        return list;
    }

    static string Clean(string s)
    {
        s = Regex.Replace(s, @"\{STEAM_CLAN_IMAGE\}\S*", "");
        s = Regex.Replace(s, @"\[img[^\]]*\].*?\[/img\]", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"\[(previewyoutube|video)[^\]]*\].*?\[/\1\]", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"\[\*\]", "\n• ");
        s = Regex.Replace(s, @"\[/?(p|h\d|list|olist|hr)[^\]]*\]", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<br\s*/?>|</p>|</h\d>|</li>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\[[^\]]{1,60}\]", "");
        s = Regex.Replace(s, @"<[^>]+>", "");
        s = System.Net.WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"[ \t]+", " ");
        s = Regex.Replace(s, @"\n\s*\n+", "\n");
        return s.Trim();
    }

    // ───── Descargas y actualizaciones (archivos locales, cada 3 s) ─────

    readonly HashSet<long> downloading = new();
    /// <summary>Instalaciones que cancelaste: sus archivos a medias ya no cuentan como descarga.</summary>
    public readonly ConcurrentDictionary<long, bool> Canceled = new();

    public void PollDownloads()
    {
        foreach (var a in LocalApps())
        {
            if (Canceled.ContainsKey(a.AppId)) continue;
            bool active = a.BytesToDownload > 0 && a.BytesDownloaded < a.BytesToDownload;
            if (active)
            {
                downloading.Add(a.AppId);
                double p = Math.Clamp((double)a.BytesDownloaded / a.BytesToDownload, 0, 1);
                DownloadProgress?.Invoke(a.AppId, p, a.FullyInstalled ? "Actualizando" : "Descargando");
            }
            else if (downloading.Remove(a.AppId))
            {
                Installed[a.AppId] = a;
                DownloadDone?.Invoke(a.AppId);
            }
        }
    }

    // ───── Fechas en español ─────

    static readonly string[] Months = { "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic" };

    public static string FmtDate(long unix)
    {
        var d = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
        return $"{d.Day} {Months[d.Month - 1]} {d.Year}";
    }

    public static string FmtDateTime(DateTime d) => $"{d.Day} {Months[d.Month - 1]} {d.Year} · {d:HH:mm}";

    public static string Rel(long unix)
    {
        if (unix <= 0) return "nunca";
        int days = (int)(DateTime.Today - DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime.Date).TotalDays;
        if (days <= 0) return "hoy";
        if (days == 1) return "ayer";
        if (days < 7) return $"hace {days} días";
        if (days < 14) return "la semana pasada";
        if (days < 30) return $"hace {days / 7} semanas";
        if (days < 60) return "el mes pasado";
        if (days < 365) return $"hace {days / 30} meses";
        return "hace más de un año";
    }

    static string Ago(long unix)
    {
        var span = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unix);
        if (span.TotalMinutes < 60) return $"hace {Math.Max(1, (int)span.TotalMinutes)} min";
        if (span.TotalHours < 24) return $"hace {(int)span.TotalHours} h";
        if (span.TotalDays < 30) return $"hace {(int)span.TotalDays} días";
        return FmtDate(unix);
    }
}
