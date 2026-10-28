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
    static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".mp4", ".webm" };

    public event Action<Dictionary<string, object>> NewCapture;
    /// <summary>Un video que la Game Bar apenas empezó a escribir ("Guardando video…").</summary>
    public event Action<Dictionary<string, object>> Saving;

    public AlbumService(SteamService steam) { this.steam = steam; }

    public List<string> Roots()
    {
        var l = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Captures") };
        if (steam.SteamPath != null && steam.AccountId > 0)
            l.Add(Path.Combine(steam.SteamPath, "userdata", steam.AccountId.ToString(), "760", "remote"));
        return l;
    }

    static bool IsMedia(string p) =>
        Exts.Contains(Path.GetExtension(p).ToLowerInvariant()) &&
        !p.Contains(Path.DirectorySeparatorChar + "thumbnails" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public List<Dictionary<string, object>> List()
    {
        var files = new List<FileInfo>();
        foreach (var r in Roots())
        {
            if (!Directory.Exists(r)) continue;
            try
            {
                foreach (var f in new DirectoryInfo(r).EnumerateFiles("*", System.IO.SearchOption.AllDirectories))
                    if (IsMedia(f.FullName)) files.Add(f);
            }
            catch (Exception ex) { Log.Error("álbum " + r, ex); }
        }
        return files.OrderByDescending(f => f.LastWriteTime).Take(400).Select(f => Item(f.FullName)).ToList();
    }

    public Dictionary<string, object> Item(string path)
    {
        var fi = new FileInfo(path);
        bool video = fi.Extension.ToLowerInvariant() is ".mp4" or ".webm";
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

    public void Watch()
    {
        foreach (var r in Roots())
        {
            try
            {
                if (!Directory.Exists(r)) continue;
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
