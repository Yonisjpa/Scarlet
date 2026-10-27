using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace XboxScarlet;

/// <summary>Descarga y guarda en disco portadas (SteamGridDB / Steam) y tu avatar, para que todo se vea aun sin internet.</summary>
sealed class ArtCache
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    readonly Config cfg;
    readonly SteamService steam;

    public ArtCache(Config cfg, SteamService steam) { this.cfg = cfg; this.steam = steam; }

    /// <summary>Dirección con la que la interfaz pide un archivo local (la sirve MainForm).</summary>
    public static string LocalUrl(string path)
    {
        string v = "";
        try { v = "&v=" + File.GetLastWriteTimeUtc(path).Ticks.ToString("x"); } catch { }
        return "https://files.scarlet/f?p=" + Uri.EscapeDataString(path) + v;
    }

    static string Hash(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..16].ToLowerInvariant();

    /// <summary>Guarda la imagen de esa URL (si no estaba) y regresa su dirección local. null si no se pudo.</summary>
    public async Task<string> CacheAsync(string url, string prefix = "img") =>
        await CacheFileAsync(url, prefix) is string file ? LocalUrl(file) : null;

    // Descargas en curso: si la interfaz pide la misma imagen dos veces a la vez, se espera la primera
    // (antes chocaban al escribir el mismo archivo y salía "Access to the path is denied").
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<string>>> inflight = new();

    /// <summary>Igual que CacheAsync, pero regresa la ruta del archivo en disco.</summary>
    public Task<string> CacheFileAsync(string url, string prefix = "img")
    {
        if (string.IsNullOrWhiteSpace(url)) return Task.FromResult<string>(null);
        string key = prefix + "|" + url;
        var lazy = inflight.GetOrAdd(key, k => new Lazy<Task<string>>(() => DownloadAsync(url, prefix, true)));
        var task = lazy.Value;
        task.ContinueWith(t => inflight.TryRemove(key, out var removed));
        return task;
    }

    async Task<string> DownloadAsync(string url, string prefix, bool retrySteam)
    {
        string ext;
        try { ext = Path.GetExtension(new Uri(url).AbsolutePath); }
        catch { return null; }
        if (ext.Length is 0 or > 5) ext = ".img";

        string file = Path.Combine(Paths.Images, prefix + "_" + Hash(url) + ext);
        if (File.Exists(file)) return file;

        string local = SteamLibraryArt(url);
        if (local != null) return local;

        byte[] bytes;
        try { bytes = await http.GetByteArrayAsync(url); }
        catch (HttpRequestException ex) when (retrySteam && ex.StatusCode == System.Net.HttpStatusCode.NotFound && SteamArtMatch(url) is { Success: true } m)
        {
            // Juegos nuevos: Steam ya no guarda su arte en la dirección clásica; se pregunta la nueva.
            string fresh = await SteamStoreAssetAsync(long.Parse(m.Groups[1].Value), m.Groups[2].Value);
            if (fresh == null) { Log.Write($"sin arte de Steam para {m.Groups[1].Value} ({m.Groups[2].Value})"); return null; }
            string got = await DownloadAsync(fresh, prefix, false);
            if (got != null) { try { File.Copy(got, file, true); return file; } catch { return got; } }
            return null;
        }
        catch (Exception ex) { Log.Write($"imagen no descargada {url}: {ex.Message}"); return null; }
        if (bytes.Length < 64) return null;

        // Se escribe con nombre único y luego se renombra; si el renombre falla (antivirus, otro proceso), sirve el único.
        string unique = Path.Combine(Paths.Images, $"{prefix}_{Hash(url)}_{Guid.NewGuid():N}"[..48] + ext);
        try { await File.WriteAllBytesAsync(unique, bytes); }
        catch (Exception ex) { Log.Write($"no se pudo guardar la imagen {url}: {ex.Message}"); return null; }
        for (int i = 0; i < 3; i++)
        {
            try
            {
                if (File.Exists(file)) { TryDelete(unique); return file; }
                File.Move(unique, file);
                return file;
            }
            catch { await Task.Delay(150); }
        }
        return unique;
    }

    static void TryDelete(string f) { try { File.Delete(f); } catch { } }

    static Match SteamArtMatch(string url) =>
        Regex.Match(url, @"/steam/apps/(\d+)/(library_600x900|library_hero|header|capsule_616x353)\.jpg", RegexOptions.IgnoreCase);

    /// <summary>Dirección actual del arte de un juego en la tienda de Steam (los juegos nuevos usan rutas con código).</summary>
    async Task<string> SteamStoreAssetAsync(long appid, string kind)
    {
        string key = kind switch { "library_600x900" => "library_capsule", "library_hero" => "library_hero", "header" => "header", _ => "main_capsule" };
        string cacheFile = Path.Combine(Paths.Cache, $"assets_{appid}.json");
        string body = null;
        try
        {
            if (File.Exists(cacheFile) && DateTime.Now - File.GetLastWriteTime(cacheFile) < TimeSpan.FromDays(7)) body = File.ReadAllText(cacheFile);
            else
            {
                string input = Uri.EscapeDataString($"{{\"ids\":[{{\"appid\":{appid}}}],\"context\":{{\"language\":\"spanish\",\"country_code\":\"MX\"}},\"data_request\":{{\"include_assets\":true}}}}");
                body = await http.GetStringAsync("https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json=" + input);
                File.WriteAllText(cacheFile, body);
            }
            using var d = JsonDocument.Parse(body);
            var items = d.RootElement.GetProperty("response").GetProperty("store_items");
            if (items.GetArrayLength() == 0 || !items[0].TryGetProperty("assets", out var a)) return null;
            string fmt = a.TryGetProperty("asset_url_format", out var f) ? f.GetString() : null;
            string name = a.TryGetProperty(key, out var n) ? n.GetString() : null;
            if (string.IsNullOrEmpty(fmt) || string.IsNullOrEmpty(name)) return null;
            return "https://shared.akamai.steamstatic.com/store_item_assets/" + fmt.Replace("${FILENAME}", name);
        }
        catch (Exception ex) { Log.Error("arte de la tienda de Steam " + appid, ex); return null; }
    }

    /// <summary>Si la URL es el arte oficial de un juego de Steam, primero busca la copia que Steam ya guardó en tu equipo.</summary>
    string SteamLibraryArt(string url)
    {
        var m = Regex.Match(url, @"/steam/apps/(\d+)/(library_600x900|library_hero)\.jpg", RegexOptions.IgnoreCase);
        if (!m.Success || steam.SteamPath == null) return null;
        string dir = Path.Combine(steam.SteamPath, "appcache", "librarycache");
        string id = m.Groups[1].Value, kind = m.Groups[2].Value;
        foreach (var c in new[] { Path.Combine(dir, id, kind + ".jpg"), Path.Combine(dir, $"{id}_{kind}.jpg") })
            if (File.Exists(c)) return c;
        try
        {
            string sub = Path.Combine(dir, id);
            if (Directory.Exists(sub))
            {
                // Las versiones nuevas de Steam guardan "library_capsule" en subcarpetas con código.
                var names = kind == "library_600x900" ? new[] { "library_600x900*.jpg", "library_capsule*.jpg" } : new[] { kind + "*.jpg" };
                foreach (var pat in names)
                    if (Directory.EnumerateFiles(sub, pat, SearchOption.AllDirectories).FirstOrDefault() is string f) return f;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Consulta a SteamGridDB (la respuesta se guarda 7 días).</summary>
    public async Task<JsonElement> SgdbAsync(string path)
    {
        string key = cfg.SgdbKey;
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Sin API key de SteamGridDB");
        string dir = Path.Combine(Paths.Cache, "sgdb");
        Directory.CreateDirectory(dir);
        string cacheFile = Path.Combine(dir, Hash(path) + ".json");
        if (File.Exists(cacheFile) && DateTime.Now - File.GetLastWriteTime(cacheFile) < TimeSpan.FromDays(7))
            return Parse(File.ReadAllText(cacheFile));
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.steamgriddb.com/api/v2/" + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var r = await http.SendAsync(req);
            string body = await r.Content.ReadAsStringAsync();
            if (r.IsSuccessStatusCode || (int)r.StatusCode == 404) File.WriteAllText(cacheFile, body);
            return Parse(body);
        }
        catch
        {
            if (File.Exists(cacheFile)) return Parse(File.ReadAllText(cacheFile));
            throw;
        }
    }

    static JsonElement Parse(string s)
    {
        using var d = JsonDocument.Parse(s);
        return d.RootElement.Clone();
    }
}
