global using System.Diagnostics;
global using System.Globalization;
global using System.Runtime.InteropServices;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Text.RegularExpressions;
global using Microsoft.Win32;

using System.Security.Cryptography;

namespace XboxScarlet;

/// <summary>Carpetas de la app.</summary>
static class Paths
{
    public static readonly string Roaming = Dir(Migrate(Environment.SpecialFolder.ApplicationData));
    public static readonly string Local = Dir(Migrate(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>La app se llamaba "Xbox Scarlet": su carpeta se renombra a "Scarlet" una sola vez, con todo (configuración, portadas, caché).</summary>
    static string Migrate(Environment.SpecialFolder root)
    {
        string baseDir = Environment.GetFolderPath(root);
        string now = Path.Combine(baseDir, "Scarlet"), old = Path.Combine(baseDir, "XboxScarlet");
        try { if (!Directory.Exists(now) && Directory.Exists(old)) Directory.Move(old, now); }
        catch
        {
            try   // si algo la tiene abierta, al menos la configuración
            {
                Directory.CreateDirectory(now);
                foreach (var f in new[] { "config.json", "apps.json" })
                    if (File.Exists(Path.Combine(old, f)) && !File.Exists(Path.Combine(now, f))) File.Copy(Path.Combine(old, f), Path.Combine(now, f));
            }
            catch { }
        }
        return now;
    }
    public static readonly string Cache = Dir(Path.Combine(Local, "cache"));
    public static readonly string Images = Dir(Path.Combine(Cache, "img"));
    public static readonly string WebData = Dir(Path.Combine(Local, "WebView2"));
    public static string Web => Path.Combine(AppContext.BaseDirectory, "web");
    public static string ConfigFile => Path.Combine(Roaming, "config.json");

    static string Dir(string p) { Directory.CreateDirectory(p); return p; }

    /// <summary>¿La ruta está dentro de la carpeta raíz?</summary>
    public static bool Under(string path, string root)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;
        try
        {
            string p = Path.GetFullPath(path);
            string r = Path.GetFullPath(root).TrimEnd('\\', '/') + "\\";
            return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}

/// <summary>Registro sencillo en %LOCALAPPDATA%\Scarlet\log.txt</summary>
static class Log
{
    static readonly object gate = new();

    // Se escribe en %LOCALAPPDATA%\Scarlet\log.txt y una copia junto a Scarlet.exe (más fácil de encontrar y mandar).
    static readonly string[] files = { Path.Combine(Paths.Local, "log.txt"), Path.Combine(AppContext.BaseDirectory, "log.txt") };

    public static void Write(string msg)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}";
        lock (gate)
            foreach (var file in files)
            {
                try
                {
                    if (File.Exists(file) && new FileInfo(file).Length > 2_000_000) File.Delete(file);
                    File.AppendAllText(file, line);
                }
                catch { }
            }
    }

    public static void Error(string where, Exception ex)
    {
        string s = ex.ToString();   // con la pila, para saber exactamente dónde falló
        Write($"[{where}] {(s.Length > 2500 ? s[..2500] + " …" : s)}");
    }
}

/// <summary>Configuración guardada en %APPDATA%\Scarlet\config.json. Las claves van cifradas con tu usuario de Windows.</summary>
sealed class Config
{
    public string SteamId { get; set; } = "";
    public string SteamKeyEnc { get; set; } = "";
    public string SgdbKeyEnc { get; set; } = "";
    public bool SetupDone { get; set; }
    public bool StartBigPicture { get; set; } = true;
    public bool DevTools { get; set; }
    public bool MuteBehind { get; set; } = true;
    public bool HideCursor { get; set; } = true;
    /// <summary>Versión de la interfaz que se cargó la última vez: si cambia, se borra la caché del navegador.</summary>
    public string UiStamp { get; set; } = "";
    /// <summary>Tiempo de juego que cuenta Scarlet (juegos de Xbox y apps; los de Steam los cuenta Steam). Clave = la de AppLastUsed.</summary>
    public Dictionary<string, long> PlaySeconds { get; set; } = new();
    /// <summary>(Versiones viejas: juegos marcados para pausarse. Ya no se usa: ahora todos se pausan.)</summary>
    public List<long> PauseOnHome { get; set; } = new();
    /// <summary>Juegos que NO se congelan al ir al Inicio (todos los demás sí, como en una consola; p. ej. juegos en línea).</summary>
    public List<long> NoPauseOnHome { get; set; } = new();
    /// <summary>Juegos donde el menú rápido se abre sin quitarles el foco (siguen sonando; también reciben los botones).</summary>
    public List<long> MenuKeepsFocus { get; set; } = new();
    public string InternalDisplay { get; set; } = "";
    public int? GameBarNexusBackup { get; set; }
    public int? GameBarLegacyBackup { get; set; }
    public Dictionary<string, int> PadOrder { get; set; } = new();
    public Dictionary<string, long> AppLastUsed { get; set; } = new();

    [JsonIgnore] public string SteamKey { get => Unprotect(SteamKeyEnc); set => SteamKeyEnc = Protect(value); }
    [JsonIgnore] public string SgdbKey { get => Unprotect(SgdbKeyEnc); set => SgdbKeyEnc = Protect(value); }

    public static Config Load()
    {
        try
        {
            if (File.Exists(Paths.ConfigFile))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(Paths.ConfigFile)) ?? new Config();
        }
        catch (Exception ex) { Log.Error("config", ex); }
        return new Config();
    }

    public void Save()
    {
        try { File.WriteAllText(Paths.ConfigFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { Log.Error("config.save", ex); }
    }

    static string Protect(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s.Trim()), null, DataProtectionScope.CurrentUser));
    }

    static string Unprotect(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
}

/// <summary>Lector de archivos VDF de texto de Steam (libraryfolders, appmanifest, localconfig…).</summary>
static class Vdf
{
    public static Dictionary<string, object> Load(string path)
    {
        try { return File.Exists(path) ? Parse(File.ReadAllText(path)) : null; }
        catch (Exception ex) { Log.Error("vdf " + path, ex); return null; }
    }

    public static Dictionary<string, object> Parse(string text)
    {
        int i = 0;
        return ParseObject(text, ref i);
    }

    static Dictionary<string, object> ParseObject(string s, ref int i)
    {
        var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            SkipSpace(s, ref i);
            if (i >= s.Length) return d;
            if (s[i] == '}') { i++; return d; }
            string key = ReadToken(s, ref i);
            if (key == null) return d;
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == '{') { i++; d[key] = ParseObject(s, ref i); }
            else d[key] = ReadToken(s, ref i) ?? "";
        }
    }

    static void SkipSpace(string s, ref int i)
    {
        while (i < s.Length)
        {
            if (char.IsWhiteSpace(s[i])) i++;
            else if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; }
            else break;
        }
    }

    static string ReadToken(string s, ref int i)
    {
        if (i >= s.Length) return null;
        if (s[i] == '"')
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    sb.Append(s[i] switch { 'n' => '\n', 't' => '\t', _ => s[i] });
                }
                else sb.Append(s[i]);
                i++;
            }
            i++;
            return sb.ToString();
        }
        int start = i;
        while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '{' && s[i] != '}' && s[i] != '"') i++;
        if (i == start) { i++; return ""; }
        return s.Substring(start, i - start);
    }

    public static Dictionary<string, object> Obj(this Dictionary<string, object> d, string key) =>
        d != null && d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

    public static string Str(this Dictionary<string, object> d, string key) =>
        d != null && d.TryGetValue(key, out var v) ? v as string : null;

    public static long Long(this Dictionary<string, object> d, string key) =>
        long.TryParse(d.Str(key), out var n) ? n : 0;
}

/// <summary>Lectura segura de los datos (payload) que manda la interfaz.</summary>
static class P
{
    static JsonElement? Get(JsonElement p, string k) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(k, out var v) ? v : null;

    public static string S(JsonElement p, string k)
    {
        var v = Get(p, k);
        if (v == null) return null;
        return v.Value.ValueKind switch
        {
            JsonValueKind.String => v.Value.GetString(),
            JsonValueKind.Number => v.Value.GetRawText(),
            _ => null
        };
    }

    public static long L(JsonElement p, string k)
    {
        var v = Get(p, k);
        if (v == null) return 0;
        if (v.Value.ValueKind == JsonValueKind.Number) return v.Value.TryGetInt64(out var n) ? n : (long)v.Value.GetDouble();
        return long.TryParse(S(p, k), out var m) ? m : 0;
    }

    public static double D(JsonElement p, string k)
    {
        var v = Get(p, k);
        if (v != null && v.Value.ValueKind == JsonValueKind.Number) return v.Value.GetDouble();
        return double.TryParse(S(p, k), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    public static bool B(JsonElement p, string k)
    {
        var v = Get(p, k);
        return v != null && v.Value.ValueKind == JsonValueKind.True;
    }
}

/// <summary>Apps que aparecen en la biblioteca junto a los juegos (YouTube, Edge…). Se editan en %APPDATA%\Scarlet\apps.json</summary>
sealed class AppEntry
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Exec { get; set; }
    public string Args { get; set; }

    [JsonIgnore] public long AppId => IdFor(Id ?? Name ?? "");

    /// <summary>Número estable a partir del texto (900000–989999) para que la interfaz lo trate como un juego más.</summary>
    public static long IdFor(string id)
    {
        uint h = 2166136261;
        foreach (char ch in id) { h ^= ch; h *= 16777619; }
        return 900000 + (h % 90000);
    }

    public static List<AppEntry> Load()
    {
        try
        {
            string bundled = Path.Combine(AppContext.BaseDirectory, "apps.json");
            string user = Path.Combine(Paths.Roaming, "apps.json");
            if (!File.Exists(user) && File.Exists(bundled)) File.Copy(bundled, user);
            string src = File.Exists(user) ? user : bundled;
            if (!File.Exists(src)) return new List<AppEntry>();
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            return JsonSerializer.Deserialize<List<AppEntry>>(File.ReadAllText(src), opts) ?? new List<AppEntry>();
        }
        catch (Exception ex) { Log.Error("apps.json", ex); return new List<AppEntry>(); }
    }

    public void Start()
    {
        var psi = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(Exec ?? ""))
        {
            Arguments = Environment.ExpandEnvironmentVariables(Args ?? ""),
            UseShellExecute = true
        };
        Process.Start(psi);
    }
}

static class Shell
{
    /// <summary>Abre una URL, un enlace steam:// o un ms-settings: con el programa que tenga Windows.</summary>
    public static void Open(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("shell " + target, ex); }
    }
}
