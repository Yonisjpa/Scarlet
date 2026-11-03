using Microsoft.VisualBasic.FileIO;
using Windows.Media.Editing;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace XboxScarlet;

/// <summary>Capturas y videos de la Game Bar (Videos\Captures) y capturas de Steam. Avisa cuando aparece una nueva.</summary>
sealed class AlbumService
{
    readonly SteamService steam;
    readonly List<FileSystemWatcher> watchers = new();
    static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".mp4", ".webm", ".mkv", ".mov" };
    static readonly string[] VideoExts = { ".mp4", ".webm", ".mkv", ".mov" };
    const int MaxItems = 2000;   // antes 600: con muchas capturas las más viejas no salían

    public event Action<Dictionary<string, object>> NewCapture;
    /// <summary>Un video que la Game Bar apenas empezó a escribir ("Guardando video…").</summary>
    public event Action<Dictionary<string, object>> Saving;

    public AlbumService(SteamService steam) { this.steam = steam; }

    /// <summary>
    /// Dónde buscar: siempre Videos › Capturas (en Windows en español la carpeta se llama "Captures" y se ve como "Capturas";
    /// se revisan las dos rutas por si Videos está en OneDrive) y las capturas de Steam de todas las cuentas de esta consola.
    /// </summary>
    public List<string> Roots()
    {
        var l = new List<string>();
        void Add(string p) { if (!string.IsNullOrEmpty(p) && !l.Any(x => string.Equals(x.TrimEnd('\\'), p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) l.Add(p); }
        string vids = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        string home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
        foreach (var v in new[] { vids, home })
            if (!string.IsNullOrEmpty(v)) { Add(Path.Combine(v, "Captures")); Add(Path.Combine(v, "Capturas")); }
        if (steam.SteamPath != null)
        {
            string ud = Path.Combine(steam.SteamPath, "userdata");
            if (steam.AccountId > 0) Add(Path.Combine(ud, steam.AccountId.ToString(), "760", "remote"));
            try { if (Directory.Exists(ud)) foreach (var d in Directory.GetDirectories(ud)) Add(Path.Combine(d, "760", "remote")); } catch { }
        }
        return l;
    }

    static bool IsMedia(string p) =>
        Exts.Contains(Path.GetExtension(p).ToLowerInvariant()) &&
        !p.Contains(Path.DirectorySeparatorChar + "thumbnails" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public List<Dictionary<string, object>> List()
    {
        var files = new List<FileInfo>();
        var skipped = new Dictionary<string, int>();
        // Una subcarpeta sin permiso ya no tumba toda la búsqueda (antes se perdía todo lo que venía después: beta 1.3).
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        foreach (var r in Roots())
        {
            if (!Directory.Exists(r)) continue;
            try
            {
                foreach (var f in new DirectoryInfo(r).EnumerateFiles("*", opts))
                    if (IsMedia(f.FullName)) files.Add(f);
                    else if (f.Extension.Length > 0 && !f.FullName.Contains("thumbnails", StringComparison.OrdinalIgnoreCase)) { string e = f.Extension.ToLowerInvariant(); skipped[e] = skipped.GetValueOrDefault(e) + 1; }
            }
            catch (Exception ex) { Log.Error("álbum " + r, ex); }
        }
        // Para saber qué no se puede mostrar (por ejemplo capturas HDR .jxr de la Game Bar).
        string sk = string.Join(", ", skipped.Where(k => k.Key is ".jxr" or ".avif" or ".heic" or ".tif" or ".tiff" or ".avi" or ".wmv").Select(k => $"{k.Key} ×{k.Value}"));
        if (sk != lastSkipped) { lastSkipped = sk; if (sk.Length > 0) Log.Write("álbum: archivos que no se pueden mostrar: " + sk); }
        var all = files.GroupBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        if (all.Count > MaxItems) Log.Write($"álbum: {all.Count} capturas; se muestran las {MaxItems} más recientes");
        return all.OrderByDescending(f => f.LastWriteTime).Take(MaxItems).Select(f => Item(f.FullName)).ToList();
    }

    string lastSkipped = "";

    public Dictionary<string, object> Item(string path)
    {
        var fi = new FileInfo(path);
        bool video = VideoExts.Contains(fi.Extension.ToLowerInvariant());
        string game, thumb = null;

        var m = Regex.Match(path, @"[\\/]760[\\/]remote[\\/](\d+)[\\/]screenshots[\\/]", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            game = steam.NameOf(long.Parse(m.Groups[1].Value)) ?? "Steam";
            string t = Path.Combine(fi.DirectoryName ?? "", "thumbnails", fi.Name);
            if (File.Exists(t)) thumb = ArtCache.LocalUrl(t);
        }
        else
        {
            // Game Bar: "Nombre del juego 2026-10-25 22-14-03.png"
            var n = Regex.Match(Path.GetFileNameWithoutExtension(path), @"^(.*?)\s*\d{4}-\d{2}-\d{2}\s+\d{2}-\d{2}-\d{2}");
            game = n.Success && n.Groups[1].Value.Trim().Length > 0 ? n.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension(path);
        }

        var d = new Dictionary<string, object>
        {
            ["url"] = ArtCache.LocalUrl(path), ["path"] = path, ["game"] = game,
            ["date"] = SteamService.FmtDateTime(fi.LastWriteTime), ["type"] = video ? "video" : "image"
        };
        if (thumb != null) d["thumb"] = thumb;
        return d;
    }

    readonly HashSet<string> watching = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Vigila las carpetas (se puede llamar varias veces: solo agrega las nuevas, p. ej. Steam ya dio tu cuenta).</summary>
    public void Watch()
    {
        foreach (var r in Roots())
        {
            try
            {
                if (!Directory.Exists(r) || !watching.Add(r)) continue;
                var w = new FileSystemWatcher(r) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName };
                w.Created += (_, e) =>
                {
                    if (!IsMedia(e.FullPath)) return;
                    if (IsVideo(e.FullPath)) try { Saving?.Invoke(Item(e.FullPath)); } catch (Exception ex) { Log.Error("guardando video", ex); }
                    _ = WhenReady(e.FullPath);
                };
                w.EnableRaisingEvents = true;
                watchers.Add(w);
            }
            catch (Exception ex) { Log.Error("vigilar " + r, ex); }
        }
    }

    /// <summary>Espera a que la Game Bar / Steam terminen de escribir el archivo.</summary>
    async Task WhenReady(string path)
    {
        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(500);
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) { }
                var d = Item(path);
                if (IsVideo(path)) { var en = EnrichVideo(d, path); await Task.WhenAny(en, Task.Delay(4000)); }
                NewCapture?.Invoke(d);
                return;
            }
            catch (IOException) { }
            catch (Exception ex) { Log.Error("captura nueva", ex); return; }
        }
    }

    static bool IsVideo(string p) => Path.GetExtension(p).ToLowerInvariant() is ".mp4" or ".webm";

    /// <summary>Para el aviso de un video nuevo: cuánto dura y un cuadro como miniatura (la Game Bar no guarda una).</summary>
    static async Task EnrichVideo(Dictionary<string, object> d, string path)
    {
        try
        {
            var f = await StorageFile.GetFileFromPathAsync(path);
            var props = await f.Properties.GetVideoPropertiesAsync();
            if (props.Duration.TotalSeconds > 0) d["duration"] = Math.Round(props.Duration.TotalSeconds, 1);
            using var th = await f.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.VideosView, 320);
            if (th == null || th.Size == 0) return;
            string dir = Directory.CreateDirectory(Path.Combine(Paths.Cache, "capturas")).FullName;
            string outp = Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + ".png");
            using (var s = th.AsStreamForRead())
            using (var img = Image.FromStream(s))
                img.Save(outp, System.Drawing.Imaging.ImageFormat.Png);
            d["thumb"] = ArtCache.LocalUrl(outp);
            d["thumbPath"] = outp;
        }
        catch (Exception ex) { Log.Error("miniatura del video", ex); }
    }

    public bool IsAlbumFile(string path) => !string.IsNullOrEmpty(path) && File.Exists(path) && Roots().Any(r => Paths.Under(path, r));

    /// <summary>
    /// Recorta un video (de "start" a "end" segundos) y lo guarda junto al original como "… (recorte).mp4".
    /// Usa el editor de video de Windows; el original no se toca.
    /// </summary>
    public async Task<object> TrimAsync(string path, double start, double end)
    {
        if (!IsAlbumFile(path)) return new { ok = false, error = "No encontré el video." };
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var clip = await MediaClip.CreateFromFileAsync(file);
            var total = clip.OriginalDuration;
            var a = TimeSpan.FromSeconds(Math.Clamp(start, 0, total.TotalSeconds));
            var b = TimeSpan.FromSeconds(Math.Clamp(end, 0, total.TotalSeconds));
            if (b - a < TimeSpan.FromSeconds(1)) return new { ok = false, error = "El recorte debe durar al menos 1 segundo." };
            clip.TrimTimeFromStart = a;
            clip.TrimTimeFromEnd = total - b;
            var comp = new MediaComposition();
            comp.Clips.Add(clip);
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path));
            var outFile = await folder.CreateFileAsync(Path.GetFileNameWithoutExtension(path) + " (recorte).mp4", CreationCollisionOption.GenerateUniqueName);
            var r = await comp.RenderToFileAsync(outFile, MediaTrimmingPreference.Fast);
            if (r != TranscodeFailureReason.None)
            {
                try { await outFile.DeleteAsync(); } catch { }
                Log.Write("recorte falló: " + r);
                return new { ok = false, error = "Windows no pudo recortar este video (" + r + ")." };
            }
            Log.Write($"recorte: {Path.GetFileName(outFile.Path)} ({a.TotalSeconds:0.#}–{b.TotalSeconds:0.#} s)");
            return new { ok = true, item = Item(outFile.Path) };
        }
        catch (Exception ex) { Log.Error("recortar", ex); return new { ok = false, error = "No se pudo recortar el video." }; }
    }

    public bool Delete(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path) || !Roots().Any(r => Paths.Under(path, r))) return false;
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        return true;
    }
}
