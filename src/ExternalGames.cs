namespace XboxScarlet;

/// <summary>Un juego que agregaste tú (no es de Steam ni de Xbox): un .exe, un acceso directo (.lnk) o un .url.</summary>
sealed class ExtGame
{
    public long Id { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }        // lo que elegiste
    public string Target { get; set; }      // el .exe de verdad (si era un acceso directo), para saber su carpeta
    public string Args { get; set; }
    public string Image { get; set; }       // portada o ícono propio (copiado a la caché de Scarlet)
    public bool IconStyle { get; set; }     // la imagen es un ícono: se ve centrado en una tarjeta, como las apps
    public long Added { get; set; }
}

/// <summary>
/// Juegos externos (beta 1.1): se agregan desde Ajustes con un explorador de archivos para el mando, se lanzan como
/// cualquier otro juego (pantalla de carga, Jugando, menú rápido, horas) y se pueden renombrar, cambiarles la portada o el
/// ícono, ponerles parámetros de arranque y quitarlos de Scarlet sin borrar nada del disco.
/// </summary>
static class ExternalGames
{
    public static readonly string[] Exts = { ".exe", ".lnk", ".url", ".bat", ".cmd" };
    static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico" };
    const long Base = 2_000_000_000L;   // lejos de Steam, de las apps (900000…) y de Xbox (1 000 000 000…)

    public static string Key(ExtGame g) => "ext:" + g.Id;

    /// <summary>Lo que se ve en el explorador: carpetas y archivos que se pueden agregar (o imágenes, para la portada).</summary>
    public static object List(string dir, bool images)
    {
        var exts = images ? ImageExts : Exts;
        if (string.IsNullOrEmpty(dir))
        {
            // Inicio del explorador: los lugares donde suelen estar los juegos y las unidades.
            var places = new List<object>();
            void Place(string name, string path, string kind) { if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) places.Add(new { name, path, kind }); }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Place("Escritorio", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "place");
            Place("Descargas", System.IO.Path.Combine(home, "Downloads"), "place");
            Place("Documentos", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "place");
            if (images) Place("Imágenes", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "place");
            else
            {
                Place("Programas", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "place");
                Place("Programas (x86)", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "place");
                Place("Menú Inicio", Environment.GetFolderPath(Environment.SpecialFolder.Programs), "place");
            }
            foreach (var d in DriveInfo.GetDrives())
                try { if (d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable) places.Add(new { name = $"{(string.IsNullOrEmpty(d.VolumeLabel) ? "Disco" : d.VolumeLabel)} ({d.Name.TrimEnd('\\')})", path = d.RootDirectory.FullName, kind = "drive" }); }
                catch { }
            return new { path = "", parent = (string)null, entries = places };
        }
        var di = new DirectoryInfo(dir);
        if (!di.Exists) return new { path = dir, parent = (string)null, entries = new List<object>(), error = "Esa carpeta ya no existe" };
        var list = new List<object>();
        try
        {
            foreach (var d in di.EnumerateDirectories().OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
                if ((d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0) list.Add(new { name = d.Name, path = d.FullName, kind = "dir" });
            foreach (var f in di.EnumerateFiles().OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
                if (exts.Contains(f.Extension.ToLowerInvariant()) && (f.Attributes & FileAttributes.Hidden) == 0)
                    list.Add(new { name = f.Name, path = f.FullName, kind = images ? "image" : "file", size = f.Length });
        }
        catch (UnauthorizedAccessException) { return new { path = di.FullName, parent = di.Parent?.FullName ?? "", entries = list, error = "Windows no deja ver esta carpeta" }; }
        return new { path = di.FullName, parent = di.Parent?.FullName ?? "", entries = list };
    }

    /// <summary>Agrega un archivo como juego. Devuelve el juego, o un error en texto.</summary>
    public static (ExtGame game, string error) Add(Config cfg, string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return (null, "No encontré ese archivo");
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (!Exts.Contains(ext)) return (null, "Ese tipo de archivo no se puede abrir como juego");
        var old = cfg.ExternalGames.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
        if (old != null) return (old, null);   // ya estaba: no se duplica
        string target = ext == ".lnk" ? ShortcutTarget(path) : ext == ".exe" ? path : null;
        if (target != null && target.Contains("steam.exe", StringComparison.OrdinalIgnoreCase)) return (null, "Es un acceso de Steam: ese juego ya aparece solo en Scarlet");
        var g = new ExtGame
        {
            Id = Base + (cfg.ExternalGames.Count == 0 ? 1 : cfg.ExternalGames.Max(x => x.Id) - Base + 1),
            Name = NiceName(path, target), Path = path, Target = target, Added = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        cfg.ExternalGames.Add(g);
        cfg.Save();
        Log.Write($"juego externo agregado: «{g.Name}» ({path}{(target != null && target != path ? " → " + target : "")})");
        return (g, null);
    }

    /// <summary>El nombre que tendría: la descripción del programa (si dice algo útil) o el nombre del archivo.</summary>
    static string NiceName(string path, string target)
    {
        string file = System.IO.Path.GetFileNameWithoutExtension(path);
        try
        {
            string exe = target ?? (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null);
            if (exe != null && File.Exists(exe) && System.IO.Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var v = FileVersionInfo.GetVersionInfo(exe);
                foreach (var n in new[] { v.ProductName, v.FileDescription })
                    if (!string.IsNullOrWhiteSpace(n) && n.Length <= 40 && !Regex.IsMatch(n, @"launcher|setup|installer|bootstrap|\.exe", RegexOptions.IgnoreCase)) return n.Trim();
            }
        }
        catch { }
        return Regex.Replace(file, @"[_\.]+", " ").Trim();
    }

    /// <summary>A dónde apunta un acceso directo (.lnk), con el componente de Windows que los lee.</summary>
    public static string ShortcutTarget(string lnk)
    {
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return null;
            dynamic sh = Activator.CreateInstance(t);
            dynamic sc = sh.CreateShortcut(lnk);
            string target = sc.TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch (Exception ex) { Log.Error("acceso directo", ex); return null; }
    }

    /// <summary>Carpeta del juego (para reconocer su ventana): la del .exe de verdad, si se sabe.</summary>
    public static string DirOf(ExtGame g)
    {
        string exe = g.Target ?? (g.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? g.Path : null);
        return exe != null && File.Exists(exe) ? System.IO.Path.GetDirectoryName(exe) : null;
    }

    public static void Launch(ExtGame g)
    {
        if (!File.Exists(g.Path)) throw new InvalidOperationException($"No encontré {System.IO.Path.GetFileName(g.Path)}: ¿lo moviste o lo borraste?");
        var psi = new ProcessStartInfo(g.Path) { UseShellExecute = true, WorkingDirectory = DirOf(g) ?? System.IO.Path.GetDirectoryName(g.Path) };
        if (!string.IsNullOrWhiteSpace(g.Args)) psi.Arguments = g.Args;
        Log.Write($"abriendo juego externo «{g.Name}»: {g.Path} {g.Args}");
        Process.Start(psi);
    }

    /// <summary>El ícono del programa como imagen (para usarlo de portada).</summary>
    public static string ExtractIcon(ExtGame g)
    {
        string exe = g.Target ?? g.Path;
        if (!File.Exists(exe)) return null;
        string dir = Directory.CreateDirectory(System.IO.Path.Combine(Paths.Cache, "externos")).FullName;
        string outp = System.IO.Path.Combine(dir, $"icono_{g.Id}.png");
        using var ico = Icon.ExtractAssociatedIcon(exe);
        if (ico == null) return null;
        // El ícono más grande que traiga el archivo (ExtractAssociatedIcon da 32×32; se pide el de 256 si existe).
        using var big = LargeIcon(exe) ?? ico.ToBitmap();
        big.Save(outp, System.Drawing.Imaging.ImageFormat.Png);
        return outp;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHDefExtractIcon(string file, int index, uint flags, out IntPtr large, out IntPtr small, uint size);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    static Bitmap LargeIcon(string exe)
    {
        try
        {
            if (SHDefExtractIcon(exe, 0, 0, out var large, out var small, 256) != 0 || large == IntPtr.Zero) return null;
            using var ic = Icon.FromHandle(large);
            var bmp = ic.ToBitmap();
            DestroyIcon(large); if (small != IntPtr.Zero) DestroyIcon(small);
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>Copia una imagen tuya a la caché de Scarlet (así se ve aunque la muevas o borres).</summary>
    public static string CopyImage(ExtGame g, string src)
    {
        if (string.IsNullOrEmpty(src) || !File.Exists(src) || !ImageExts.Contains(System.IO.Path.GetExtension(src).ToLowerInvariant())) return null;
        string dir = Directory.CreateDirectory(System.IO.Path.Combine(Paths.Cache, "externos")).FullName;
        string ext = System.IO.Path.GetExtension(src).ToLowerInvariant();
        string outp = System.IO.Path.Combine(dir, $"portada_{g.Id}_{DateTime.Now:HHmmss}{(ext == ".ico" ? ".png" : ext)}");
        if (ext == ".ico") { using var ic = new Icon(src, 256, 256); using var b = ic.ToBitmap(); b.Save(outp, System.Drawing.Imaging.ImageFormat.Png); }
        else File.Copy(src, outp, true);
        return outp;
    }
}
