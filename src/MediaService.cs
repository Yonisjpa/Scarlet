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
    string lastArtKey, lastArtUrl;

    public MediaService(ArtCache art) { this.art = art; }

    public async Task StartAsync()
    {
        try
        {
            mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            mgr.SessionsChanged += (_, _) => _ = PickAsync();
            mgr.CurrentSessionChanged += (_, _) => _ = PickAsync();
            await PickAsync();
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

    async Task RefreshAsync()
    {
        if (!await busy.WaitAsync(0)) { _ = Task.Delay(250).ContinueWith(_ => RefreshAsync()); return; }
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
                    string artUrl = await ArtAsync(props) ?? await ItunesArtAsync(props);
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

    /// <summary>La carátula se guarda en la caché (una vez por canción) y se sirve como archivo local.</summary>
    // Apple Music a veces avisa la canción antes de tener lista la carátula (llega vacía): se vuelve a pedir un par de veces.
    string retryKey;
    int retries;

    async Task<string> ArtAsync(GlobalSystemMediaTransportControlsSessionMediaProperties props)
    {
        string key = (props.Title + "|" + props.Artist + "|" + props.AlbumTitle);
        if (key == lastArtKey) return lastArtUrl;
        try
        {
            string dir = Path.Combine(Paths.Cache, "music");
            Directory.CreateDirectory(dir);
            string stem = "music_" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..16];
            string file = Directory.EnumerateFiles(dir, stem + ".*").FirstOrDefault();
            if (file != null && new FileInfo(file).Length < 200) { try { File.Delete(file); } catch { } file = null; }   // guardada vacía antes
            if (file == null)
            {
                byte[] bytes = null;
                if (props.Thumbnail != null)
                {
                    using IRandomAccessStreamWithContentType ras = await props.Thumbnail.OpenReadAsync();
                    using var input = ras.AsStreamForRead();
                    using var ms = new MemoryStream();
                    await input.CopyToAsync(ms);
                    bytes = ms.ToArray();
                }
                if (bytes == null || bytes.Length < 200)
                {
                    if (retryKey != key) { retryKey = key; retries = 0; }
                    if (retries < 4)
                    {
                        int wait = new[] { 1200, 2500, 5000, 9000 }[retries++];
                        _ = Task.Delay(wait).ContinueWith(_ => RefreshAsync());
                    }
                    else Log.Write($"música: {props.Title} no trae carátula");
                    return null;
                }
                // El tipo real se ve en los primeros bytes (algunas apps lo reportan mal).
                string ext = bytes[0] == 0x89 && bytes[1] == 0x50 ? ".png" : bytes[0] == 0xFF && bytes[1] == 0xD8 ? ".jpg" : bytes[0] == 'R' && bytes[1] == 'I' ? ".webp" : ".png";
                file = Path.Combine(dir, stem + ext);
                await File.WriteAllBytesAsync(file, bytes);
            }
            lastArtKey = key;
            lastArtUrl = ArtCache.LocalUrl(file);
            return lastArtUrl;
        }
        catch (Exception ex) { Log.Error("música (carátula)", ex); return null; }
    }

    // ───── Carátula de respaldo: el catálogo público de iTunes (es la misma carátula de Apple Music) ─────
    static readonly HttpClient web = new() { Timeout = TimeSpan.FromSeconds(10) };
    readonly Dictionary<string, string> itunes = new();

    async Task<string> ItunesArtAsync(GlobalSystemMediaTransportControlsSessionMediaProperties props)
    {
        // Apple Music pone "Artista — Álbum" en el campo de artista.
        string artist = (props.Artist ?? props.AlbumArtist ?? "").Split(new[] { " — ", " - " }, StringSplitOptions.None)[0].Trim();
        string album = props.AlbumTitle;
        if (string.IsNullOrEmpty(album) && (props.Artist ?? "").Contains(" — ")) album = props.Artist.Split(" — ", 2)[1].Trim();
        string key = artist + "|" + props.Title;
        if (itunes.TryGetValue(key, out var cached)) return cached;
        itunes[key] = null;   // una sola búsqueda por canción
        try
        {
            string url = "https://itunes.apple.com/search?entity=song&limit=8&country=mx&term=" + Uri.EscapeDataString($"{artist} {props.Title}");
            using var d = JsonDocument.Parse(await web.GetStringAsync(url));
            var results = d.RootElement.GetProperty("results").EnumerateArray().ToList();
            if (results.Count == 0) return null;
            static string S(JsonElement e, string k) => e.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
            var best = results.FirstOrDefault(r => S(r, "trackName").Equals(props.Title, StringComparison.OrdinalIgnoreCase) && (string.IsNullOrEmpty(album) || S(r, "collectionName").Equals(album, StringComparison.OrdinalIgnoreCase)));
            if (best.ValueKind == JsonValueKind.Undefined) best = results.FirstOrDefault(r => S(r, "trackName").Equals(props.Title, StringComparison.OrdinalIgnoreCase));
            if (best.ValueKind == JsonValueKind.Undefined) best = results[0];
            string art = Regex.Replace(S(best, "artworkUrl100"), @"/\d+x\d+bb\.", "/600x600bb.");
            if (art.Length == 0) return null;
            string local = await this.art.CacheAsync(art, "music");
            itunes[key] = local;
            Log.Write($"música: carátula de {props.Title} desde iTunes");
            return local;
        }
        catch (Exception ex) { Log.Error("música (iTunes)", ex); return null; }
    }

    public object State => last;

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
