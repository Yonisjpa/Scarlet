namespace XboxScarlet;

/// <summary>
/// Mezclador de Windows desde Scarlet: volumen por app (el del juego y el de la música, por separado) y elegir la salida
/// de audio (bocinas, audífonos Bluetooth, pantalla por HDMI). Usa las mismas funciones que el mezclador de Windows.
/// </summary>
static class AudioMixer
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
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
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPERTYKEY { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPVARIANT { public ushort vt; public ushort r1, r2, r3; public IntPtr p; public IntPtr p2; }

    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT pvar);

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
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, IntPtr ctx);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, IntPtr ctx);
        [PreserveSig] int GetGroupingParam(out Guid param);
        [PreserveSig] int SetGroupingParam(IntPtr param, IntPtr ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
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

    // Interfaz de Windows (sin documentar, la usa el propio panel de sonido) para cambiar la salida predeterminada.
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    class PolicyConfigClient { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
        [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
        [PreserveSig] int ResetDeviceFormat(IntPtr a);
        [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
        [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }

    static IMMDeviceEnumerator Enumerator() => (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();

    // ───── Salidas de audio ─────

    public sealed record Output(string id, string name, bool current);

    /// <summary>Salidas activas (bocinas, audífonos, HDMI…) y cuál está en uso.</summary>
    public static List<Output> Outputs()
    {
        var list = new List<Output>();
        try
        {
            var en = Enumerator();
            string cur = null;
            if (en.GetDefaultAudioEndpoint(0, 1, out var def) == 0 && def != null) def.GetId(out cur);
            if (en.EnumAudioEndpoints(0 /* eRender */, 1 /* ACTIVE */, out var col) != 0 || col == null) return list;
            col.GetCount(out uint n);
            for (uint i = 0; i < n; i++)
            {
                if (col.Item(i, out var dev) != 0 || dev == null) continue;
                dev.GetId(out string id);
                list.Add(new Output(id, FriendlyName(dev) ?? "Salida de audio", id == cur));
            }
        }
        catch (Exception ex) { Log.Error("salidas de audio", ex); }
        return list;
    }

    static string FriendlyName(IMMDevice dev)
    {
        try
        {
            if (dev.OpenPropertyStore(0 /* STGM_READ */, out var store) != 0 || store == null) return null;
            var key = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };   // nombre completo
            if (store.GetValue(ref key, out var v) != 0) return null;
            string s = v.vt == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(v.p) : null;
            PropVariantClear(ref v);
            return s;
        }
        catch { return null; }
    }

    /// <summary>Cambia la salida de audio de todo Windows (para juegos, música y sistema).</summary>
    public static bool SetOutput(string id)
    {
        try
        {
            var pc = (IPolicyConfig)new PolicyConfigClient();
            for (int role = 0; role < 3; role++) pc.SetDefaultEndpoint(id, role);
            Log.Write("audio: salida cambiada a " + (Outputs().FirstOrDefault(o => o.id == id)?.name ?? id));
            return true;
        }
        catch (Exception ex) { Log.Error("cambiar salida de audio", ex); return false; }
    }

    // ───── Volumen por app ─────

    public sealed record Session(string exe, uint pid, bool active, int volume);

    /// <summary>Apps con sonido ahora mismo (sesiones del mezclador), con su volumen 0–100.</summary>
    /// <param name="strict">Si Windows no contesta, lanza error en vez de devolver la lista vacía (el menú distingue "nada suena" de "no se pudo leer").</param>
    public static List<Session> Sessions(bool strict = false)
    {
        var list = new List<Session>();
        bool ok = Each((c2, vol, pid, exe) =>
        {
            c2.GetState(out int state);
            vol.GetMasterVolume(out float level);
            list.Add(new Session(exe, pid, state == 1 /* activa */, (int)Math.Round(level * 100)));
        });
        if (!ok && strict) throw new InvalidOperationException("Windows no dio las apps con sonido");
        return list;
    }

    public static int? GetVolume(string exe)
    {
        int? v = null;
        Each((c2, vol, pid, name) => { if (v == null && name.Equals(exe, StringComparison.OrdinalIgnoreCase)) { vol.GetMasterVolume(out float l); v = (int)Math.Round(l * 100); } });
        return v;
    }

    /// <summary>
    /// Volumen de una app: todas sus sesiones (Edge suena desde otro proceso con el mismo nombre) en TODAS las salidas.
    /// Antes solo se cambiaba en la salida principal y, si la app sonaba por otra, la barra no hacía nada.
    /// Después se comprueba que de verdad haya cambiado.
    /// </summary>
    public static void SetVolume(string exe, int value)
    {
        var ctx = Guid.Empty;
        float level = Math.Clamp(value, 0, 100) / 100f;
        int touched = 0, wrong = 0;
        Each((c2, vol, pid, name) =>
        {
            if (!name.Equals(exe, StringComparison.OrdinalIgnoreCase)) return;
            vol.SetMasterVolume(level, ref ctx);
            touched++;
            if (vol.GetMasterVolume(out float now) == 0 && Math.Abs(now - level) > 0.02f) wrong++;
        }, allOutputs: true);
        if (touched == 0) Log.Write($"mezclador: {exe} no tiene sonido en ninguna salida; la barra no tiene qué mover");
        else if (wrong > 0) Log.Write($"mezclador: {exe} no aceptó el volumen {value} en {wrong} de {touched} sesión/es");
    }

    /// <summary>"Restablecer sonido": quita el silencio a todas las apps y sube las que quedaron casi en cero. Devuelve cuántas tocó.</summary>
    public static int ResetAll()
    {
        int n = 0; var ctx = Guid.Empty;
        Each((c2, vol, pid, name) =>
        {
            vol.GetMute(out bool m); vol.GetMasterVolume(out float l);
            if (m) { vol.SetMute(false, ref ctx); n++; }
            if (l < 0.05f) { vol.SetMasterVolume(1f, ref ctx); n++; }
        });
        Log.Write($"audio: restablecido ({n} ajuste/s)");
        return n;
    }

    /// <summary>Para el log: qué apps tienen sesión de audio, si están silenciadas y su volumen.</summary>
    public static string Describe()
    {
        var parts = new List<string>();
        Each((c2, vol, pid, name) => { vol.GetMute(out bool m); vol.GetMasterVolume(out float l); c2.GetState(out int st); parts.Add($"{name}{(m ? " [silenciada]" : "")} {(int)(l * 100)}%{(st == 1 ? " ▶" : "")}"); });
        return string.Join(", ", parts);
    }

    /// <param name="allOutputs">Todas las salidas activas (bocinas, audífonos, HDMI); false = solo la principal.</param>
    static bool Each(Action<IAudioSessionControl2, ISimpleAudioVolume, uint, string> act, bool allOutputs = true)
    {
        try
        {
            var en = Enumerator();
            var devs = new List<IMMDevice>();
            if (allOutputs && en.EnumAudioEndpoints(0, 1 /* ACTIVE */, out var col) == 0 && col != null)
            {
                col.GetCount(out uint nd);
                for (uint d = 0; d < nd; d++) if (col.Item(d, out var x) == 0 && x != null) devs.Add(x);
            }
            else if (en.GetDefaultAudioEndpoint(0, 1, out var dev) == 0 && dev != null) devs.Add(dev);
            if (devs.Count == 0) return false;
            bool any = false;
            foreach (var dev in devs) any |= EachOn(dev, act);
            return any;
        }
        catch (Exception ex) { Log.Error("mezclador", ex); return false; }
    }

    static bool EachOn(IMMDevice dev, Action<IAudioSessionControl2, ISimpleAudioVolume, uint, string> act)
    {
        try
        {
            var iid = typeof(IAudioSessionManager2).GUID;
            if (dev.Activate(ref iid, 23, IntPtr.Zero, out object o) != 0 || o is not IAudioSessionManager2 mgr) return false;
            if (mgr.GetSessionEnumerator(out var sessions) != 0 || sessions == null) return false;
            sessions.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out object s) != 0 || s == null) continue;
                try
                {
                    if (s is not IAudioSessionControl2 c2 || s is not ISimpleAudioVolume vol) continue;
                    if (c2.IsSystemSoundsSession() == 0) continue;
                    if (c2.GetProcessId(out uint pid) != 0 || pid == 0) continue;
                    string name;
                    try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; } catch { continue; }
                    act(c2, vol, pid, name);
                }
                finally { Marshal.ReleaseComObject(s); }
            }
            return true;
        }
        catch (Exception ex) { Log.Error("mezclador", ex); return false; }
    }
}
