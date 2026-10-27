using Microsoft.VisualBasic.FileIO;

namespace XboxScarlet;

/// <summary>Capturas y videos de la Game Bar (Videos\Captures) y capturas de Steam. Avisa cuando aparece una nueva.</summary>
sealed class AlbumService
{
    readonly SteamService steam;
    readonly List<FileSystemWatcher> watchers = new();
    static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".mp4", ".webm" };

    public event Action<Dictionary<string, object>> NewCapture;

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
                w.Created += (_, e) => { if (IsMedia(e.FullPath)) _ = WhenReady(e.FullPath); };
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
                NewCapture?.Invoke(Item(path));
                return;
            }
            catch (IOException) { }
            catch (Exception ex) { Log.Error("captura nueva", ex); return; }
        }
    }

    public bool Delete(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path) || !Roots().Any(r => Paths.Under(path, r))) return false;
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        return true;
    }
}
