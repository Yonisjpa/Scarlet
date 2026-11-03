using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace XboxScarlet;

/// <summary>Mezclador: apps con sonido, lecturas con tiempo límite y silenciar el juego de atrás.</summary>
sealed partial class MainForm
{
    long detachedApp;   // la última app que dejaste abierta en segundo plano (para ponerle nombre e ícono en el mezclador)

    static readonly HashSet<string> NotApps = new(StringComparer.OrdinalIgnoreCase)
    { "Scarlet", "msedgewebview2", "steam", "steamwebhelper", "explorer", "ShellExperienceHost", "SystemSettings", "audiodg", "GameBar", "ArmouryCrate", "ArmouryCrateSE", "RuntimeBroker" };

    /// <summary>Una entrada de la biblioteca (nombre, ícono) para un id; si no está, un nombre suelto.</summary>
    Dictionary<string, object> Entry(long appid, string exe, string fallbackName, int volume, string key)
    {
        var g = appid != 0 ? lastGames.FirstOrDefault(e => (long)e["appid"] == appid) : null;
        return new Dictionary<string, object>
        {
            ["key"] = key, ["exe"] = exe, ["volume"] = volume, ["appid"] = appid,
            ["name"] = g?["name"] ?? fallbackName, ["source"] = g?["source"] ?? "app", ["kind"] = g?["kind"] ?? "app",
            ["art"] = g != null && g.TryGetValue("art", out var a) ? a : null
        };
    }

    // Lecturas del menú que se quedaron atoradas (Windows no contesta): no se lanza otra encima, se espera a esa.
    readonly Dictionary<string, Task> inflight = new();

    /// <summary>Corre una lectura con tiempo límite. ok=false si tardó o falló (la interfaz se queda con lo último que sabía).</summary>
    async Task<(bool ok, T val)> Timed<T>(string what, Func<T> f, int ms)
    {
        Task<T> t;
        lock (inflight)
        {
            if (inflight.TryGetValue(what, out var prev) && !prev.IsCompleted && prev is Task<T> same) t = same;
            else { t = Task.Run(f); inflight[what] = t; }
        }
        if (await Task.WhenAny(t, Task.Delay(ms)) != t) { Log.Write($"menú: {what} tardó más de {ms} ms; se queda lo último"); return (false, default); }
        if (t.IsFaulted) { Log.Error("menú: " + what, t.Exception?.InnerException ?? t.Exception); return (false, default); }
        return (true, t.Result);
    }

    /// <summary>
    /// El proceso del juego (o app) que está detrás de Scarlet. Se busca en orden: su proceso, su ventana (también la
    /// que está cargando), lo que suene desde su carpeta y, si Scarlet no lo está siguiendo, la ventana sobre la que
    /// abriste el menú. Así el menú lo reconoce desde el primer momento, sin esperar a que suene.
    /// </summary>
    (string exe, uint pid, bool tracked) GameProcess(IntPtr hint, List<AudioMixer.Session> sessions)
    {
        string Name(uint id) { try { using var gp = Process.GetProcessById((int)id); return gp.HasExited ? null : gp.ProcessName; } catch { return null; } }
        uint WinPid(IntPtr h) { if (h == IntPtr.Zero || !Native.IsWindow(h)) return 0; Native.GetWindowThreadProcessId(h, out uint p); return p; }
        // Carpeta del juego a partir de su .exe (subiendo de bin\x64, Binaries\Win64…): ahí suelen estar también los
        // procesos que suenan cuando no es el mismo que dibuja la ventana (juegos de Xbox Store, de HoYoverse…).
        string GameDir(uint p)
        {
            string dir = Path.GetDirectoryName(Native.ProcessPath(p) ?? "");
            for (int i = 0; i < 3 && dir != null && Regex.IsMatch(Path.GetFileName(dir), @"^(bin|binaries|win64|win32|x64|x86|shipping)$", RegexOptions.IgnoreCase); i++) dir = Path.GetDirectoryName(dir);
            return dir;
        }
        // De lo que se sabe del juego, el proceso que de verdad suena (o, si nada suena aún, el de su ventana).
        (string, uint)? Pick(uint wpid, string dir)
        {
            if (sessions != null)
            {
                if (wpid != 0 && sessions.FirstOrDefault(x => x.pid == wpid) is { } own) return (own.exe, own.pid);
                foreach (var d in new[] { dir, wpid != 0 ? GameDir(wpid) : null })
                    if (!string.IsNullOrEmpty(d) && sessions.FirstOrDefault(x => !NotApps.Contains(x.exe) && !AudioFocus.IsMusic(x.exe) && Paths.Under(Native.ProcessPath(x.pid) ?? "", d)) is { } s) return (s.exe, s.pid);
            }
            if (wpid != 0 && Name(wpid) is string n) return (n, wpid);
            return null;
        }
        if (tracker != null && tracker.Active)
        {
            string dir = tracker.InstallDir ?? steam.InstallDirOf(tracker.AppId);
            if (string.IsNullOrEmpty(dir) && xbox.Find(tracker.AppId) is { } xg) dir = xg.Dir;
            foreach (uint wp in new[] { tracker.Pid, WinPid(tracker.Window), WinPid(tracker.Candidate) })
                if (wp != 0 && Name(wp) != null && Pick(wp, dir) is { } r) return (r.Item1, r.Item2, true);
            if (Pick(0, dir) is { } rd) return (rd.Item1, rd.Item2, true);
        }
        if (hint != IntPtr.Zero && hint != hMain && hint != hQuick && Native.IsWindow(hint))
        {
            uint hp = WinPid(hint);
            if (hp != 0 && Name(hp) is string hn && !NotApps.Contains(hn) && !AudioFocus.IsMusic(hn) && Pick(hp, null) is { } rh)
            {
                // Es el juego que Scarlet sigue (su ventana cambió): se le pone su nombre. Si no, es otra app suelta.
                bool same = tracker != null && tracker.Active && IsGameWindow(hint);
                return (rh.Item1, rh.Item2, same);
            }
        }
        return (null, 0, false);
    }

    string lastOtherExe; DateTime lastOtherAt;

    /// <summary>
    /// Barras de volumen del menú: la de Apple Music / Spotify siempre que esté abierta (se quita al cerrarla); la del
    /// juego si hay uno; y la de otra app (YouTube…) mientras suena. Solo el juego, sin nada más, no lleva barra (para eso
    /// está el volumen general). Lista vacía = nada que mostrar; null = no se pudo leer (la interfaz se queda con lo último).
    /// </summary>
    List<Dictionary<string, object>> MixerState(IntPtr hint)
    {
        var list = new List<Dictionary<string, object>>();
        var sessions = AudioMixer.Sessions(strict: true);
        var (gameExe, _, tracked) = GameProcess(hint, sessions);
        bool Is(AudioMixer.Session x, string exe) => exe != null && x.exe.Equals(exe, StringComparison.OrdinalIgnoreCase);
        int VolOf(string exe) => sessions.Where(x => Is(x, exe)).Select(x => x.volume).DefaultIfEmpty(100).Max();

        if (gameExe != null)
        {
            long gameId = tracked ? tracker.AppId : 0;   // si se detectó por la ventana del frente, no se le pone el nombre de otro juego
            string gameName = gameExe;
            if (gameId == 0) try { var gp = Native.ProcessPath(sessions.FirstOrDefault(x => Is(x, gameExe))?.pid ?? 0); if (gp != null && FileVersionInfo.GetVersionInfo(gp).FileDescription is string gd && gd.Length > 0) gameName = gd; } catch { }
            list.Add(Entry(gameId, gameExe, gameName, VolOf(gameExe), "game"));
        }
        // Música: siempre que la app tenga su sesión de sonido (abierta, aunque esté en pausa).
        bool MusicSession(AudioMixer.Session x) => AudioFocus.IsMusic(x.exe) || (x.path != null && Regex.IsMatch(x.path, @"\\WindowsApps\\[^\\]*(AppleMusic|Spotify)", RegexOptions.IgnoreCase));
        // Una barra por app (no por programa): Apple Music puede tener varios programas con sonido en su carpeta.
        string Family(AudioMixer.Session x) => xbox.Games.FirstOrDefault(g => !string.IsNullOrEmpty(g.Dir) && x.path != null && Paths.Under(x.path, g.Dir))?.Dir ?? x.exe;
        foreach (var m in sessions.Where(x => MusicSession(x) && !Is(x, gameExe)).GroupBy(Family, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(x => x.active).First()))
        {
            var (id, name) = AppInfo(m);
            list.Add(Entry(id, m.exe, name, VolOf(m.exe), "music"));
        }
        // Otra app que suena (YouTube, Netflix…); entre video y video (unos segundos sin sonar) se sigue mostrando.
        bool sticky = lastOtherExe != null && DateTime.Now - lastOtherAt < TimeSpan.FromSeconds(10);
        var other = sessions.Where(x => !Is(x, gameExe) && !NotApps.Contains(x.exe) && !MusicSession(x)
                                        && (x.active || (sticky && Is(x, lastOtherExe))))
                            .OrderByDescending(x => x.active).FirstOrDefault();
        if (other != null)
        {
            if (other.active) { lastOtherExe = other.exe; lastOtherAt = DateTime.Now; }
            var (id, name) = AppInfo(other);
            list.Add(Entry(id, other.exe, name, VolOf(other.exe), "app"));
        }
        if (list.Count == 1 && (string)list[0]["key"] == "game") list.Clear();
        return list;
    }

    /// <summary>Carpeta de una app de la Store (Apple Music…) a partir del nombre de su programa: sus otros programas también cuentan.</summary>
    string AppDirOf(string exe)
    {
        try
        {
            foreach (var pr in Process.GetProcessesByName(exe))
                using (pr)
                {
                    string path = Native.ProcessPath((uint)pr.Id) ?? "";
                    var store = xbox.Games.FirstOrDefault(g => !string.IsNullOrEmpty(g.Dir) && Paths.Under(path, g.Dir));
                    if (store != null) return store.Dir;
                }
        }
        catch { }
        return null;
    }

    /// <summary>¿Qué app es? Apps de la Store por su carpeta; Edge/YouTube por la que dejaste abierta; si no, el nombre del programa.</summary>
    (long id, string name) AppInfo(AudioMixer.Session x)
    {
        string path = Native.ProcessPath(x.pid) ?? "";
        var store = xbox.Games.FirstOrDefault(g => g.IsApp && !string.IsNullOrEmpty(g.Dir) && Paths.Under(path, g.Dir));
        if (store != null) return (store.AppId, store.Name);
        var app = apps.FirstOrDefault(a => a.AppId == detachedApp) is AppEntry d && Path.GetFileNameWithoutExtension(d.Exec ?? "").Equals(x.exe, StringComparison.OrdinalIgnoreCase)
            ? d : apps.FirstOrDefault(a => Path.GetFileNameWithoutExtension(a.Exec ?? "").Equals(x.exe, StringComparison.OrdinalIgnoreCase));
        if (app != null) return (app.AppId, app.Name);
        try { if (FileVersionInfo.GetVersionInfo(path).FileDescription is string fd && fd.Length > 0) return (0, fd); } catch { }
        return (0, x.exe);
    }

    bool muting;
    IntPtr hMain, hQuick;   // las ventanas de Scarlet, para usarlas fuera del hilo de la pantalla

    /// <summary>
    /// Al entrar a Scarlet el juego se calla siempre (por su proceso, aunque al frente estuviera el menú rápido u otra
    /// ventana); la música no. Recupera su sonido al volver al juego. Se hace aparte para no trabar la pantalla.
    /// </summary>
    void MuteGameBehind(IntPtr hint)
    {
        if (!cfg.MuteBehind || muting) return;
        muting = true;
        _ = Task.Run(() =>
        {
            try
            {
                var (exe, _, _) = GameProcess(hint, AudioMixer.Sessions());
                if (exe != null) AudioFocus.MuteExe(exe);
                // Lo que estaba al frente, si era otra cosa (YouTube encima del juego, por ejemplo).
                if (hint != IntPtr.Zero && hint != hMain && hint != hQuick && Native.IsWindow(hint)) AudioFocus.MuteWindow(hint);
            }
            catch (Exception ex) { Log.Error("silenciar juego", ex); }
            finally { muting = false; }
        });
    }
}
