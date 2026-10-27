using System.Xml.Linq;
using Windows.Management.Deployment;

namespace XboxScarlet;

/// <summary>Un juego instalado desde la Xbox Store / PC Game Pass.</summary>
sealed class XboxGame
{
    public long AppId { get; set; }
    public string Name { get; set; }
    public string Pfn { get; set; }          // PackageFamilyName
    public string Aumid { get; set; }        // con esto lo abre Windows
    public string Dir { get; set; }          // carpeta instalada (XboxGames\…\Content)
    public string StoreId { get; set; }      // id de la Microsoft Store (9N…)
    public string LocalLogo { get; set; }    // logo cuadrado que trae el juego
    public double SizeGb { get; set; }
}

/// <summary>
/// Biblioteca de Xbox: busca los juegos instalados de la Xbox Store (paquetes de Windows con MicrosoftGame.config o en
/// carpetas XboxGames), trae su portada oficial del catálogo de la Microsoft Store y los abre como lo hace Windows.
/// </summary>
sealed class XboxService
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    readonly ArtCache art;
    List<XboxGame> games = new();
    readonly Dictionary<long, (string grid, string hero)> artMap = new();
    bool fetching;

    public event Action ArtChanged;

    public XboxService(ArtCache art) { this.art = art; }

    public IReadOnlyList<XboxGame> Games => games;
    public XboxGame Find(long appid) => games.FirstOrDefault(g => g.AppId == appid);

    /// <summary>Número estable por juego, lejos de los ids de Steam y de las apps.</summary>
    static long IdFor(string pfn)
    {
        ulong h = 1469598103934665603;
        foreach (char c in pfn.ToLowerInvariant()) { h ^= c; h *= 1099511628211; }
        return 1_000_000_000L + (long)(h % 900_000_000UL);
    }

    public async Task<List<XboxGame>> ScanAsync()
    {
        var list = new List<XboxGame>();
        try
        {
            var pm = new PackageManager();
            foreach (var pkg in pm.FindPackagesForUser(""))
            {
                try
                {
                    if (pkg.IsFramework || pkg.IsResourcePackage || pkg.IsBundle) continue;
                    string dir = null;
                    try { dir = pkg.InstalledPath; } catch { }
                    if (string.IsNullOrEmpty(dir)) try { dir = pkg.InstalledLocation?.Path; } catch { }
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

                    string config = Path.Combine(dir, "MicrosoftGame.config");
                    bool isGame = File.Exists(config) || dir.Contains(@"\XboxGames\", StringComparison.OrdinalIgnoreCase);
                    if (!isGame) continue;

                    var g = new XboxGame { Pfn = pkg.Id.FamilyName, Dir = dir, Name = SafeName(pkg) };
                    if (File.Exists(config)) ReadConfig(config, g);
                    var entries = await pkg.GetAppListEntriesAsync();
                    var entry = entries.FirstOrDefault();
                    if (entry == null) continue;
                    g.Aumid = entry.AppUserModelId;
                    if (string.IsNullOrWhiteSpace(g.Name) || g.Name.StartsWith("ms-resource", StringComparison.OrdinalIgnoreCase))
                        g.Name = entry.DisplayInfo?.DisplayName ?? pkg.Id.Name;
                    g.AppId = IdFor(g.Pfn);
                    g.SizeGb = FolderSizeGb(dir);
                    list.Add(g);
                }
                catch (Exception ex) { Log.Error("xbox: paquete", ex); }
            }
        }
        catch (Exception ex) { Log.Error("xbox: biblioteca", ex); }

        games = list;
        Log.Write("xbox: " + (list.Count == 0 ? "sin juegos" : string.Join(", ", list.Select(g => $"{g.Name} ({g.StoreId ?? g.Pfn})"))));
        _ = FetchArtAsync();
        return list;
    }

    static string SafeName(Windows.ApplicationModel.Package pkg)
    {
        try { return pkg.DisplayName; } catch { return null; }
    }

    static void ReadConfig(string file, XboxGame g)
    {
        try
        {
            var x = XDocument.Load(file);
            XElement El(string name) => x.Descendants().FirstOrDefault(e => e.Name.LocalName == name);
            var visuals = El("ShellVisuals");
            string display = visuals?.Attribute("DefaultDisplayName")?.Value;
            if (!string.IsNullOrWhiteSpace(display) && !display.StartsWith("ms-resource", StringComparison.OrdinalIgnoreCase)) g.Name = display;
            g.StoreId = El("StoreId")?.Value?.Trim();
            foreach (var attr in new[] { "Square480x480Logo", "Square150x150Logo", "StoreLogo" })
            {
                string rel = visuals?.Attribute(attr)?.Value;
                if (string.IsNullOrEmpty(rel)) continue;
                string full = Path.Combine(Path.GetDirectoryName(file)!, rel);
                if (File.Exists(full)) { g.LocalLogo = full; break; }
            }
        }
        catch (Exception ex) { Log.Error("xbox: MicrosoftGame.config", ex); }
    }

    static double FolderSizeGb(string dir)
    {
        try
        {
            long total = 0;
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                total += f.Length;
            return Math.Round(total / 1073741824.0, 1);
        }
        catch { return 0; }
    }

    /// <summary>Portada (cuadrada) y fondo del juego, ya guardados en disco. null si todavía no hay.</summary>
    public (string grid, string hero) ArtFor(long appid) => artMap.TryGetValue(appid, out var a) ? a : (null, null);

    async Task FetchArtAsync()
    {
        if (fetching) return;
        fetching = true;
        bool changed = false;
        try
        {
            foreach (var g in games.ToList())
            {
                string grid = null, hero = null;
                try
                {
                    var images = await StoreImagesAsync(g);
                    string Pick(params string[] purposes) =>
                        purposes.Select(p => images.FirstOrDefault(i => i.purpose.Equals(p, StringComparison.OrdinalIgnoreCase)).uri).FirstOrDefault(u => u != null);
                    string gridUrl = Pick("BoxArt", "FeaturePromotionalSquareArt", "Tile", "Poster");
                    string heroUrl = Pick("SuperHeroArt", "TitledHeroArt", "Screenshot");
                    if (gridUrl != null) grid = await art.CacheAsync(gridUrl, "xbox");
                    if (heroUrl != null) hero = await art.CacheAsync(heroUrl, "xboxhero");
                }
                catch (Exception ex) { Log.Error("xbox: portada de " + g.Name, ex); }

                if (grid == null && g.LocalLogo != null)   // sin internet o sin datos en la tienda: el logo que trae el juego
                {
                    try
                    {
                        string copy = Path.Combine(Paths.Images, "xboxlocal_" + g.AppId + Path.GetExtension(g.LocalLogo));
                        if (!File.Exists(copy)) File.Copy(g.LocalLogo, copy, true);
                        grid = ArtCache.LocalUrl(copy);
                    }
                    catch { }
                }
                var old = ArtFor(g.AppId);
                if (old.grid != grid || old.hero != hero) { artMap[g.AppId] = (grid, hero); changed = true; }
            }
        }
        finally
        {
            fetching = false;
            if (changed) ArtChanged?.Invoke();
        }
    }

    /// <summary>Imágenes oficiales del juego en el catálogo de la Microsoft Store (las mismas de la tienda de Xbox). Se guardan 14 días.</summary>
    async Task<List<(string purpose, string uri)>> StoreImagesAsync(XboxGame g)
    {
        string key = g.StoreId ?? g.Pfn;
        string cacheFile = Path.Combine(Paths.Cache, "xbox_" + Regex.Replace(key, @"[^\w.-]", "_") + ".json");
        string body = null;
        if (File.Exists(cacheFile) && DateTime.Now - File.GetLastWriteTime(cacheFile) < TimeSpan.FromDays(14)) body = File.ReadAllText(cacheFile);
        if (body == null)
        {
            string url = !string.IsNullOrEmpty(g.StoreId)
                ? $"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={Uri.EscapeDataString(g.StoreId)}&market=MX&languages=es-MX,en-US,neutral"
                : $"https://displaycatalog.mp.microsoft.com/v7.0/products/lookup?alternateId=PackageFamilyName&value={Uri.EscapeDataString(g.Pfn)}&market=MX&languages=es-MX,en-US,neutral";
            body = await http.GetStringAsync(url);
            File.WriteAllText(cacheFile, body);
        }

        var result = new List<(string, string)>();
        using var d = JsonDocument.Parse(body);
        if (!d.RootElement.TryGetProperty("Products", out var products) || products.GetArrayLength() == 0) return result;
        var product = products[0];
        if (string.IsNullOrEmpty(g.StoreId) && product.TryGetProperty("ProductId", out var pid)) g.StoreId = pid.GetString();
        if (!product.TryGetProperty("LocalizedProperties", out var locs) || locs.GetArrayLength() == 0) return result;
        if (!locs[0].TryGetProperty("Images", out var imgs)) return result;
        foreach (var i in imgs.EnumerateArray())
        {
            string purpose = i.TryGetProperty("ImagePurpose", out var p) ? p.GetString() : null;
            string uri = i.TryGetProperty("Uri", out var u) ? u.GetString() : null;
            if (purpose == null || string.IsNullOrEmpty(uri)) continue;
            if (uri.StartsWith("//")) uri = "https:" + uri;
            result.Add((purpose, uri));
        }
        return result;
    }

    /// <summary>Abre el juego como lo haría el menú Inicio.</summary>
    public void Launch(XboxGame g)
    {
        Log.Write($"xbox: abriendo {g.Name} ({g.Aumid})");
        Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\" + g.Aumid) { UseShellExecute = true });
    }
}
