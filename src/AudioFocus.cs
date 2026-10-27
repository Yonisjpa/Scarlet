namespace XboxScarlet;

/// <summary>
/// "Silenciar el foco": al volver a Scarlet se silencia la app que quedó detrás (el juego, YouTube…)
/// y recupera su sonido al regresar a ella. Usa el mezclador de volumen de Windows (sesiones de audio por app),
/// así que no toca el volumen general. La música (Apple Music, Spotify) sigue sonando.
/// </summary>
static class AudioFocus
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, IntPtr ctx);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, IntPtr ctx);
        [PreserveSig] int GetGroupingParam(out Guid param);
        [PreserveSig] int SetGroupingParam(IntPtr param, IntPtr ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    /// <summary>Apps que nunca se silencian: la música, Scarlet y su vista web, Steam y el sistema.</summary>
    static readonly HashSet<string> Never = new(StringComparer.OrdinalIgnoreCase)
    {
        "AppleMusic", "Spotify", "iTunes", "Scarlet", "msedgewebview2", "explorer", "steam", "steamwebhelper", "ApplicationFrameHost", "GameBar", "ArmouryCrate", "ArmouryCrateSE"
    };

    static readonly object gate = new();
    static readonly HashSet<string> mutedApps = new(StringComparer.OrdinalIgnoreCase);
    static string StateFile => Path.Combine(Paths.Local, "silenciadas.txt");

    /// <summary>¿Esta app es música que debe seguir sonando en el menú?</summary>
    public static bool IsMusic(string exe) => exe != null && (exe.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase) || exe.Contains("Spotify", StringComparison.OrdinalIgnoreCase) || exe.Contains("iTunes", StringComparison.OrdinalIgnoreCase));

    /// <summary>Silencia la app dueña de esa ventana (todas sus sesiones de audio; Edge reproduce desde otro proceso con el mismo nombre).</summary>
    public static void MuteWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        string exe = Path.GetFileNameWithoutExtension(Native.WindowProcessPath(hWnd, out _) ?? "");
        MuteExe(exe);
    }

    /// <summary>
    /// Silencia una app por el nombre de su proceso (el juego que sigue Scarlet, aunque no sea la ventana del frente).
    /// La música, Scarlet, Steam y el sistema nunca. Devuelve si quedó silenciada (tenía sonido que callar).
    /// </summary>
    public static bool MuteExe(string exe)
    {
        if (string.IsNullOrEmpty(exe) || Never.Contains(exe) || IsMusic(exe)) return false;
        // Siempre se revisa de verdad: algunos juegos se quitan el silencio solos (al ganar o perder el foco) o abren otra
        // sesión de sonido; antes, una vez anotado como silenciado, ya no se volvía a mirar.
        int n = Apply(exe, true, out int changed);
        if (n == 0) return false;   // todavía no tiene sonido: se vuelve a intentar al rato
        bool first;
        lock (gate) { first = mutedApps.Add(exe); Save(); }
        if (first) Log.Write($"audio: {exe} silenciada ({n} sesión/es)");
        else if (changed > 0) Log.Write($"audio: {exe} se había quitado el silencio; se vuelve a silenciar ({changed} sesión/es)");
        return true;
    }

    /// <summary>¿Todas las sesiones de sonido de esa app están en silencio? (null si no tiene sonido).</summary>
    public static bool? IsMuted(string exe)
    {
        if (string.IsNullOrEmpty(exe)) return null;
        int total = 0, muted = 0;
        Each((vol, name) => { if (!name.Equals(exe, StringComparison.OrdinalIgnoreCase)) return; total++; vol.GetMute(out bool m); if (m) muted++; });
        return total == 0 ? null : muted == total;
    }

    public static bool AnyMuted { get { lock (gate) return mutedApps.Count > 0; } }

    static bool steamMuted;
    public static bool SteamMuted => steamMuted;
    public static string MutedList() { lock (gate) return string.Join(", ", mutedApps); }

    /// <summary>
    /// Steam en silencio: sus sonidos de Big Picture y avisos se oían detrás de Scarlet. Los juegos no se tocan
    /// (son otros procesos). Se le devuelve el sonido cuando tú abres la tienda o el chat.
    /// </summary>
    public static void MuteSteam(bool mute)
    {
        if (!mute && !steamMuted) return;
        bool changed = mute != steamMuted;
        steamMuted = mute;
        AudioThread.Post(() =>   // sin trabar la pantalla
        {
            int n = Apply("steamwebhelper", mute, out _) + Apply("steam", mute, out _);
            if (changed) Log.Write($"audio: Steam {(mute ? "en silencio" : "con sonido")} ({n} sesión/es)");
        });
    }

    /// <summary>Tras "Restablecer sonido": ya no hay nada silenciado por Scarlet que devolver.</summary>
    public static void ForgetMuted() { lock (gate) { mutedApps.Clear(); Save(); } steamMuted = false; }

    /// <summary>Le devuelve el sonido a todo lo que silenció Scarlet.</summary>
    public static void RestoreAll()
    {
        string[] list;
        lock (gate) { if (mutedApps.Count == 0) return; list = mutedApps.ToArray(); mutedApps.Clear(); Save(); }
        AudioThread.Post(() =>   // sin trabar la pantalla
        {
            foreach (var exe in list) Apply(exe, false, out _);
            Log.Write("audio: sonido devuelto a " + string.Join(", ", list));
        });
    }

    /// <summary>Si Scarlet se cerró de golpe con algo silenciado, al arrancar se le devuelve el sonido.</summary>
    public static void RecoverAfterCrash()
    {
        try
        {
            if (!File.Exists(StateFile)) return;
            var list = File.ReadAllLines(StateFile).Where(l => l.Trim().Length > 0).ToArray();
            File.Delete(StateFile);
            foreach (var exe in list) Apply(exe.Trim(), false, out _);
            if (list.Length > 0) Log.Write("audio: se recuperó el sonido de " + string.Join(", ", list));
        }
        catch (Exception ex) { Log.Error("audio (recuperar)", ex); }
    }

    static void Save()
    {
        try
        {
            if (mutedApps.Count == 0) { if (File.Exists(StateFile)) File.Delete(StateFile); }
            else File.WriteAllLines(StateFile, mutedApps);
        }
        catch { }
    }

    /// <summary>
    /// Silencia o devuelve el sonido de todas las sesiones de audio cuyo proceso se llame así, en TODAS las salidas
    /// (bocinas, audífonos, HDMI…): un juego puede sonar por una que no es la principal. Devuelve cuántas tocó;
    /// changed = cuántas no estaban ya como se pedía.
    /// </summary>
    static int Apply(string exe, bool mute, out int changed)
    {
        int touched = 0, ch = 0;
        var ctx = Guid.Empty;
        Each((vol, name) =>
        {
            if (!name.Equals(exe, StringComparison.OrdinalIgnoreCase)) return;
            if (vol.GetMute(out bool m) == 0 && m != mute) ch++;
            vol.SetMute(mute, ref ctx);
            touched++;
        });
        changed = ch;
        return touched;
    }

    static IMMDeviceEnumerator enumerator;
    static AudioFocus() { AudioThread.Reset += () => enumerator = null; }

    /// <summary>Recorre las sesiones de sonido de las apps en todas las salidas activas (en el hilo del audio).</summary>
    static void Each(Action<ISimpleAudioVolume, string> act) => AudioThread.Run(() => { EachCore(act); return true; }, false);

    static void EachCore(Action<ISimpleAudioVolume, string> act)
    {
        try
        {
            var en = enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (en.EnumAudioEndpoints(0 /* eRender */, 1 /* ACTIVE */, out var col) != 0 || col == null) return;
            col.GetCount(out uint nd);
            var iid = typeof(IAudioSessionManager2).GUID;
            for (uint d = 0; d < nd; d++)
            {
                if (col.Item(d, out var dev) != 0 || dev == null) continue;
                if (dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out object o) != 0 || o is not IAudioSessionManager2 mgr) continue;
                if (mgr.GetSessionEnumerator(out var sessions) != 0 || sessions == null) continue;
                sessions.GetCount(out int count);
                for (int i = 0; i < count; i++)
                {
                    if (sessions.GetSession(i, out object s) != 0 || s == null) continue;
                    try
                    {
                        if (s is not IAudioSessionControl2 c2 || s is not ISimpleAudioVolume vol) continue;
                        if (c2.IsSystemSoundsSession() == 0) continue;   // S_OK = es la de sonidos del sistema
                        if (c2.GetProcessId(out uint pid) != 0 || pid == 0) continue;
                        string name;
                        try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; } catch { continue; }
                        act(vol, name);
                    }
                    finally { Marshal.ReleaseComObject(s); }
                }
            }
        }
        catch (Exception ex) when (ex is not InvalidCastException && ex is not COMException) { Log.Error("audio (sesiones)", ex); }
    }
}
