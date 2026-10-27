using Windows.Media.Control;
using Windows.Storage.Streams;

namespace XboxScarlet;

/// <summary>
/// "Reproduciendo ahora": lee la música que suena (Apple Music primero; si no, cualquier app que la reporte a Windows,
/// como Spotify o YouTube) y la controla. Es el mismo sistema que usan las teclas multimedia de Windows.
/// </summary>
sealed class MediaService
{
    GlobalSystemMediaTransportControlsSessionManager mgr;
    GlobalSystemMediaTransportControlsSession session;
    readonly ArtCache art;
    public event Action<object> Changed;
    object last;

    public MediaService(ArtCache art) { this.art = art; }

    public async Task StartAsync()
    {
        try
        {
            mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            mgr.SessionsChanged += (_, _) => _ = PickAsync();
            mgr.CurrentSessionChanged += (_, _) => _ = PickAsync();
            await PickAsync();
            // Cada 30 s se vuelve a verificar que la carátula sea la de la canción que suena.
            _ = Task.Run(async () => { while (true) { await Task.Delay(30_000); if (session != null) await RefreshAsync(true); } });
        }
        catch (Exception ex) { Log.Error("música", ex); }
    }

    static bool IsApple(GlobalSystemMediaTransportControlsSession s) =>
        s?.SourceAppUserModelId?.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase) == true;

    static bool IsPlaying(GlobalSystemMediaTransportControlsSession s)
    {
        try { return s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
        catch { return false; }
    }

    /// <summary>Apple Music si está abierta; si no, lo que esté sonando; si nada suena, lo que Windows marque como actual.</summary>
    async Task PickAsync()
    {
        try
        {
            var all = mgr?.GetSessions()?.ToList() ?? new();
            var pick = all.FirstOrDefault(IsApple) ?? all.FirstOrDefault(IsPlaying) ?? mgr?.GetCurrentSession();
            if (!ReferenceEquals(pick, session))
            {
                if (session != null)
                {
                    session.MediaPropertiesChanged -= OnProps;
                    session.PlaybackInfoChanged -= OnInfo;
                    session.TimelinePropertiesChanged -= OnInfo;
                }
                session = pick;
                if (session != null)
                {
                    session.MediaPropertiesChanged += OnProps;
                    session.PlaybackInfoChanged += OnInfo;
                    session.TimelinePropertiesChanged += OnInfo;
                    Log.Write("música: " + session.SourceAppUserModelId);
                }
            }
            await RefreshAsync();
        }
        catch (Exception ex) { Log.Error("música (sesión)", ex); }
    }

    void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ = RefreshAsync();
    void OnInfo(GlobalSystemMediaTransportControlsSession s, object e) => _ = RefreshAsync();

    readonly SemaphoreSlim busy = new(1, 1);

    async Task RefreshAsync(bool forceArt = false)
    {
        if (!await busy.WaitAsync(0)) { _ = Task.Delay(250).ContinueWith(_ => RefreshAsync(forceArt)); return; }
        try
        {
            object state = null;
            var s = session;
            if (s != null)
            {
                var props = await s.TryGetMediaPropertiesAsync();
                var info = s.GetPlaybackInfo();
                var tl = s.GetTimelineProperties();
                if (props != null && !string.IsNullOrEmpty(props.Title))
                {
                    string artUrl = await ArtFor(props, forceArt);
                    double dur = tl != null ? (tl.EndTime - tl.StartTime).TotalSeconds : 0;
                    double pos = tl != null ? (tl.Position - tl.StartTime).TotalSeconds : 0;
                    long at = tl != null && tl.LastUpdatedTime.Year > 2000 ? tl.LastUpdatedTime.ToUnixTimeMilliseconds() : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var c = info?.Controls;
                    state = new
                    {
                        app = AppName(s.SourceAppUserModelId),
                        title = props.Title,
                        artist = string.IsNullOrEmpty(props.Artist) ? props.AlbumArtist : props.Artist,
                        album = props.AlbumTitle,
                        art = artUrl,
                        playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                        position = Math.Max(0, pos),
                        duration = Math.Max(0, dur),
                        at,
                        now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        shuffle = info?.IsShuffleActive == true,
                        canShuffle = c?.IsShuffleEnabled == true,
                        canNext = c?.IsNextEnabled != false,
                        canPrev = c?.IsPreviousEnabled != false,
                        canSeek = c?.IsPlaybackPositionEnabled == true
                    };
                }
            }
            last = state;
            Changed?.Invoke(state);
        }
        catch (Exception ex) { Log.Error("música (datos)", ex); }
        finally { busy.Release(); }
    }

    static string AppName(string aumid) =>
        aumid == null ? "" :
        aumid.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase) ? "Apple Music" :
        aumid.Contains("Spotify", StringComparison.OrdinalIgnoreCase) ? "Spotify" :
        aumid.Contains("msedge", StringComparison.OrdinalIgnoreCase) ? "Microsoft Edge" :
        aumid.Contains("chrome", StringComparison.OrdinalIgnoreCase) ? "Chrome" : aumid.Split('!')[0].Split('_')[0];

    // ───── Carátula ─────
    // Al cambiar de canción, Apple Music avisa el título nuevo pero unos segundos sigue mandando la imagen de la canción
    // anterior; antes se guardaba esa como la nueva. Ahora:
    //   1) primero el catálogo de iTunes con coincidencia exacta (canción + artista): es la carátula oficial;
    //   2) si no hay, la imagen de Apple Music, solo si no es la misma de la canción anterior;
    //   3) si nada, la mejor coincidencia aproximada de iTunes.
    // Se revisa cada vez que cambia la canción y cada 30 s.
    string curKey, curArt, curSource, curThumbHash, prevThumbHash;
    int retries;

    static string Clean(string s) => Regex.Replace((s ?? "").ToLowerInvariant(), @"\s*[\(\[].*?[\)\]]", "").Replace("’", "'").Trim();

    async Task<string> ArtFor(GlobalSystemMediaTransportControlsSessionMediaProperties props, bool force)
    {
        string key = props.Title + "|" + props.Artist + "|" + props.AlbumTitle;
        if (key != curKey)
        {
            if (curThumbHash != null) prevThumbHash = curThumbHash;
            curKey = key; curArt = null; curSource = null; curThumbHash = null; retries = 0;
        }
        if (curSource == "itunes" && !force) return curArt;   // la oficial ya está: no hay nada que revisar
        if (curArt != null && !force) return curArt;           // los avisos de avance llegan seguido: se revisa al cambiar y cada 30 s
        try
        {
            var (iurl, exact) = await ItunesAsync(props);
            if (exact && iurl != null) { Use(iurl, "itunes"); return curArt; }

            byte[] bytes = await ThumbBytes(props);
            if (bytes != null)
            {
                string h = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes))[..16];
                if (h == prevThumbHash)
                {
                    Log.Write($"música: la imagen de «{props.Title}» todavía es la de la canción anterior; se vuelve a pedir");
                    ScheduleRetry();
                }
                else if (h != curThumbHash || curArt == null)
                {
                    string dir = Path.Combine(Paths.Cache, "music");
                    Directory.CreateDirectory(dir);
                    string ext = bytes[0] == 0x89 && bytes[1] == 0x50 ? ".png" : bytes[0] == 0xFF && bytes[1] == 0xD8 ? ".jpg" : bytes[0] == 'R' && bytes[1] == 'I' ? ".webp" : ".png";
                    string file = Path.Combine(dir, "thumb_" + h + ext);
                    if (!File.Exists(file)) await File.WriteAllBytesAsync(file, bytes);
                    curThumbHash = h;
                    Use(ArtCache.LocalUrl(file), "thumb");
                    return curArt;
                }
                else return curArt;
            }
            else ScheduleRetry();

            if (iurl != null && curArt == null) Use(iurl, "itunes~");
        }
        catch (Exception ex) { Log.Error("música (carátula)", ex); }
        return curArt;
    }

    void Use(string url, string source)
    {
        if (curArt != url) Log.Write($"música: carátula {(source == "itunes" ? "oficial (iTunes)" : source == "thumb" ? "de Apple Music" : "aproximada (iTunes)")}");
        curArt = url; curSource = source;
    }

    void ScheduleRetry()
    {
        if (retries >= 5) return;
        int wait = new[] { 1200, 2500, 4500, 8000, 15000 }[retries++];
        _ = Task.Delay(wait).ContinueWith(_ => RefreshAsync(true));
    }

    static async Task<byte[]> ThumbBytes(GlobalSystemMediaTransportControlsSessionMediaProperties props)
    {
        if (props.Thumbnail == null) return null;
        try
        {
            using IRandomAccessStreamWithContentType ras = await props.Thumbnail.OpenReadAsync();
            using var input = ras.AsStreamForRead();
            using var ms = new MemoryStream();
            await input.CopyToAsync(ms);
            return ms.Length < 200 ? null : ms.ToArray();
        }
        catch { return null; }
    }

    // Catálogo público de iTunes (misma carátula que Apple Music).
    static readonly HttpClient web = new() { Timeout = TimeSpan.FromSeconds(10) };
    readonly Dictionary<string, (string url, bool exact)> itunes = new();

    async Task<(string url, bool exact)> ItunesAsync(GlobalSystemMediaTransportControlsSessionMediaProperties props)
    {
        // Apple Music pone "Artista — Álbum" en el campo de artista.
        string artist = (props.Artist ?? props.AlbumArtist ?? "").Split(new[] { " — ", " - " }, StringSplitOptions.None)[0].Trim();
        string album = props.AlbumTitle;
        if (string.IsNullOrEmpty(album) && (props.Artist ?? "").Contains(" — ")) album = props.Artist.Split(" — ", 2)[1].Trim();
        string key = artist + "|" + props.Title + "|" + album;
        if (itunes.TryGetValue(key, out var cached)) return cached;
        itunes[key] = (null, false);
        try
        {
            string url = "https://itunes.apple.com/search?entity=song&limit=15&country=mx&term=" + Uri.EscapeDataString($"{artist} {props.Title}");
            using var d = JsonDocument.Parse(await web.GetStringAsync(url));
            var results = d.RootElement.GetProperty("results").EnumerateArray().ToList();
            static string S(JsonElement e, string k) => e.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
            bool Title(JsonElement r) => Clean(S(r, "trackName")) == Clean(props.Title);
            bool Artist(JsonElement r) => artist.Length > 0 && (S(r, "artistName").Contains(artist, StringComparison.OrdinalIgnoreCase) || artist.Contains(S(r, "artistName"), StringComparison.OrdinalIgnoreCase));
            bool Album(JsonElement r) => !string.IsNullOrEmpty(album) && Clean(S(r, "collectionName")) == Clean(album);

            var best = results.FirstOrDefault(r => Title(r) && Artist(r) && Album(r));
            bool exact = best.ValueKind != JsonValueKind.Undefined;
            if (!exact) { best = results.FirstOrDefault(r => Title(r) && Artist(r)); exact = best.ValueKind != JsonValueKind.Undefined; }
            if (!exact) best = results.FirstOrDefault(r => Artist(r) && Album(r));   // mismo disco: misma portada
            if (best.ValueKind == JsonValueKind.Undefined) best = results.FirstOrDefault(Title);
            if (best.ValueKind == JsonValueKind.Undefined) return (null, false);
            if (!exact && Artist(best) && Album(best)) exact = true;
            string art = Regex.Replace(S(best, "artworkUrl100"), @"/\d+x\d+bb\.", "/600x600bb.");
            if (art.Length == 0) return (null, false);
            string local = await this.art.CacheAsync(art, "music");
            itunes[key] = (local, exact);
            return itunes[key];
        }
        catch (Exception ex) { Log.Error("música (iTunes)", ex); itunes.Remove(key); return (null, false); }
    }

    public object State => last;

    public string Describe()
    {
        var sx = session;
        if (sx == null) return "nada sonando";
        return $"{AppName(sx.SourceAppUserModelId)} · «{curKey?.Split('|')[0]}» · carátula {(curSource switch { "itunes" => "oficial (iTunes)", "thumb" => "de la app", "itunes~" => "aproximada", _ => "sin carátula" })}";
    }

    public async Task<bool> CommandAsync(string cmd, double value)
    {
        var s = session;
        if (s == null) return false;
        try
        {
            switch (cmd)
            {
                case "toggle": return await s.TryTogglePlayPauseAsync();
                case "next": return await s.TrySkipNextAsync();
                case "prev": return await s.TrySkipPreviousAsync();
                case "shuffle": return await s.TryChangeShuffleActiveAsync(!(s.GetPlaybackInfo()?.IsShuffleActive ?? false));
                case "seek":
                    var tl = s.GetTimelineProperties();
                    return await s.TryChangePlaybackPositionAsync((tl.StartTime + TimeSpan.FromSeconds(Math.Max(0, value))).Ticks);
            }
        }
        catch (Exception ex) { Log.Error("música " + cmd, ex); }
        return false;
    }
}
