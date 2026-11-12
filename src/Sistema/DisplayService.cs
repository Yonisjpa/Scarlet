namespace XboxScarlet;

/// <summary>
/// Beta 1.8: Ajustes › Pantalla. Resolución y frecuencia de la pantalla que se usa, HDR, y la pantalla externa «como en la
/// Switch»: al conectar una tele o un monitor, Scarlet pasa todo a esa pantalla y apaga la de la consola (como «Solo
/// segunda pantalla» de Windows); al desconectarla, Windows regresa solo a la de la consola.
/// Cambiar la resolución es temporal hasta que se confirma: si en 15 s nadie dice «Mantener», se regresa sola (así nunca
/// te quedas con la pantalla en negro o sin poder ver).
/// </summary>
static class DisplayService
{
    // ───── Windows: modos de pantalla ─────
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
    const int ENUM_CURRENT_SETTINGS = -1, DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;
    const int CDS_UPDATEREGISTRY = 1, CDS_TEST = 2, DISP_CHANGE_SUCCESSFUL = 0;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE devMode, IntPtr hwnd, int flags, IntPtr lParam);

    // ───── Windows: configuración de pantallas (nombres, si es la interna, HDR, topología) ─────
    [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] struct PATH_SOURCE { public LUID adapterId; public uint id, modeInfoIdx, statusFlags; }
    [StructLayout(LayoutKind.Sequential)] struct RATIONAL { public uint Num, Den; }
    [StructLayout(LayoutKind.Sequential)] struct PATH_TARGET
    {
        public LUID adapterId; public uint id, modeInfoIdx, outputTechnology, rotation, scaling; public RATIONAL refreshRate;
        public uint scanLineOrdering; public int targetAvailable; public uint statusFlags;
    }
    [StructLayout(LayoutKind.Sequential)] struct PATH_INFO { public PATH_SOURCE source; public PATH_TARGET target; public uint flags; }
    [StructLayout(LayoutKind.Sequential)] struct MODE_INFO { public uint infoType, id; public LUID adapterId; public ulong a, b, c, d, e, f; }
    [StructLayout(LayoutKind.Sequential)] struct HEADER { public uint type, size; public LUID adapterId; public uint id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct TARGET_NAME
    {
        public HEADER header; public uint flags, outputTechnology; public ushort edidManufactureId, edidProductCodeId; public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct SOURCE_NAME { public HEADER header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName; }
    [StructLayout(LayoutKind.Sequential)] struct COLOR_INFO { public HEADER header; public uint value, colorEncoding, bitsPerColorChannel; }
    [StructLayout(LayoutKind.Sequential)] struct COLOR_SET { public HEADER header; public uint value; }
    const uint QDC_ALL_PATHS = 1, QDC_ONLY_ACTIVE_PATHS = 2;
    const uint SDC_TOPOLOGY_INTERNAL = 1, SDC_TOPOLOGY_EXTERNAL = 8, SDC_APPLY = 0x80;
    const uint GET_SOURCE_NAME = 1, GET_TARGET_NAME = 2, GET_ADVANCED_COLOR_INFO = 9, SET_ADVANCED_COLOR_STATE = 10;
    const uint TECH_INTERNAL = 0x80000000, TECH_DP_EMBEDDED = 11, TECH_UDI_EMBEDDED = 13;
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPath, out uint numMode);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPath, [Out] PATH_INFO[] paths, ref uint numMode, [Out] MODE_INFO[] modes, IntPtr topology);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref TARGET_NAME info);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref SOURCE_NAME info);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref COLOR_INFO info);
    [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref COLOR_SET info);
    [DllImport("user32.dll")] static extern int SetDisplayConfig(uint numPath, IntPtr paths, uint numMode, IntPtr modes, uint flags);

    static bool IsInternal(uint tech) => tech == TECH_INTERNAL || tech == TECH_DP_EMBEDDED || tech == TECH_UDI_EMBEDDED;

    /// <summary>Una pantalla conectada: su nombre de Windows (\\.\DISPLAY1), el de la tele, si es la de la consola y su HDR.</summary>
    public sealed record Screen1(string Device, string Name, bool Internal, bool Active, LUID_ Adapter, uint TargetId, bool HdrSupported, bool HdrOn);
    public readonly record struct LUID_(uint Low, int High);

    static PATH_INFO[] Paths(uint flags)
    {
        for (int tries = 0; tries < 3; tries++)
        {
            if (GetDisplayConfigBufferSizes(flags, out uint np, out uint nm) != 0) return Array.Empty<PATH_INFO>();
            var paths = new PATH_INFO[np]; var modes = new MODE_INFO[nm];
            int r = QueryDisplayConfig(flags, ref np, paths, ref nm, modes, IntPtr.Zero);
            if (r == 0) return paths.Take((int)np).ToArray();
            if (r != 122 /* ERROR_INSUFFICIENT_BUFFER: cambió algo mientras tanto */) break;
        }
        return Array.Empty<PATH_INFO>();
    }

    /// <summary>Las pantallas activas ahora.</summary>
    public static List<Screen1> Screens()
    {
        var list = new List<Screen1>();
        try
        {
            foreach (var p in Paths(QDC_ONLY_ACTIVE_PATHS))
            {
                var tn = new TARGET_NAME { header = { type = GET_TARGET_NAME, size = (uint)Marshal.SizeOf<TARGET_NAME>(), adapterId = p.target.adapterId, id = p.target.id } };
                DisplayConfigGetDeviceInfo(ref tn);
                var sn = new SOURCE_NAME { header = { type = GET_SOURCE_NAME, size = (uint)Marshal.SizeOf<SOURCE_NAME>(), adapterId = p.source.adapterId, id = p.source.id } };
                DisplayConfigGetDeviceInfo(ref sn);
                var ci = new COLOR_INFO { header = { type = GET_ADVANCED_COLOR_INFO, size = (uint)Marshal.SizeOf<COLOR_INFO>(), adapterId = p.target.adapterId, id = p.target.id } };
                bool sup = false, on = false;
                if (DisplayConfigGetDeviceInfo(ref ci) == 0) { sup = (ci.value & 1) != 0 && (ci.value & 8) == 0; on = (ci.value & 2) != 0; }
                bool inside = IsInternal(tn.outputTechnology);
                string name = string.IsNullOrWhiteSpace(tn.monitorFriendlyDeviceName) ? (inside ? "Pantalla de la consola" : "Pantalla externa") : tn.monitorFriendlyDeviceName.Trim();
                list.Add(new Screen1(sn.viewGdiDeviceName, name, inside, true, new LUID_(p.target.adapterId.Low, p.target.adapterId.High), p.target.id, sup, on));
            }
        }
        catch (Exception ex) { Log.Error("pantallas", ex); }
        return list;
    }

    /// <summary>¿Hay una pantalla externa conectada (aunque Windows no la esté usando)?</summary>
    public static bool ExternalAvailable()
    {
        try { return Paths(QDC_ALL_PATHS).Any(p => p.target.targetAvailable != 0 && !IsInternal(p.target.outputTechnology)); }
        catch (Exception ex) { Log.Error("pantallas (externa)", ex); return false; }
    }

    /// <summary>La pantalla que se está usando para Scarlet: la externa si hay una activa; si no, la de la consola.</summary>
    public static Screen1 Current()
    {
        var s = Screens();
        return s.FirstOrDefault(x => !x.Internal) ?? s.FirstOrDefault(x => x.Internal) ?? s.FirstOrDefault();
    }

    public readonly record struct Mode(int W, int H, int Hz);

    static Mode? CurrentMode(string device)
    {
        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        return EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref dm) ? new Mode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency) : null;
    }

    /// <summary>Los modos que acepta la pantalla (sin repetidos y de mayor a menor), solo con la proporción de la pantalla.</summary>
    static List<Mode> Modes(string device, Mode cur)
    {
        var all = new HashSet<Mode>();
        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        for (int i = 0; EnumDisplaySettings(device, i, ref dm); i++)
            if (dm.dmBitsPerPel >= 24 && dm.dmPelsHeight >= 600) all.Add(new Mode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency));
        double ratio = (double)cur.W / Math.Max(1, cur.H);
        return all.Where(m => Math.Abs((double)m.W / m.H - ratio) < .02).OrderByDescending(m => m.H).ThenByDescending(m => m.Hz).ToList();
    }

    /// <summary>Lo que la interfaz necesita para Ajustes › Pantalla.</summary>
    public static object State(Config cfg)
    {
        var s = Current();
        if (s == null) return new { ok = false };
        var cur = CurrentMode(s.Device) ?? new Mode(1920, 1080, 60);
        var modes = Modes(s.Device, cur);
        var res = modes.Select(m => m.H).Distinct().Select(h => { var m = modes.First(x => x.H == h); return new { w = m.W, h = m.H }; }).ToList();
        var hz = modes.Where(m => m.W == cur.W && m.H == cur.H).Select(m => m.Hz).Distinct().OrderByDescending(x => x).ToList();
        int maxH = modes.Count > 0 ? modes.Max(m => m.H) : cur.H;
        return new
        {
            ok = true, name = s.Name, external = !s.Internal, w = cur.W, h = cur.H, hz = cur.Hz, maxH,
            resolutions = res, rates = hz, hdr = new { supported = s.HdrSupported, on = s.HdrOn, mode = cfg.HdrMode ?? "auto" },
            pending = pending != null
        };
    }

    // ───── cambiar la resolución (temporal hasta «Mantener») ─────
    static Mode? before; static string pendingDevice; static System.Threading.Timer pending;
    public static event Action Reverted;

    static int Apply(string device, Mode m, int flags)
    {
        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref dm)) return -1;
        dm.dmPelsWidth = m.W; dm.dmPelsHeight = m.H; dm.dmDisplayFrequency = m.Hz; dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
        int test = ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
        if (test != DISP_CHANGE_SUCCESSFUL) return test;
        return ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, flags, IntPtr.Zero);
    }

    /// <summary>Cambia resolución y frecuencia; si no se confirma en «secs» segundos, regresa a la de antes.</summary>
    public static object Set(int w, int h, int hz, int secs = 15)
    {
        var s = Current(); if (s == null) return new { ok = false, error = "No se encontró la pantalla" };
        var cur = CurrentMode(s.Device); if (cur == null) return new { ok = false, error = "No se pudo leer la pantalla" };
        var modes = Modes(s.Device, cur.Value);
        if (hz <= 0) hz = modes.Where(m => m.W == w && m.H == h).Select(m => m.Hz).DefaultIfEmpty(cur.Value.Hz).Max();
        var want = new Mode(w, h, hz);
        if (want == cur.Value) return new { ok = true, same = true };
        if (pending == null) { before = cur; pendingDevice = s.Device; }
        int r = Apply(s.Device, want, 0);   // 0 = solo por ahora (no se guarda hasta «Mantener»)
        if (r != DISP_CHANGE_SUCCESSFUL) { Log.Write($"pantalla: Windows no aceptó {w}×{h} a {hz} Hz ({r})"); return new { ok = false, error = "La pantalla no acepta ese modo" }; }
        Log.Write($"pantalla: {cur.Value.W}×{cur.Value.H} {cur.Value.Hz} Hz → {w}×{h} {hz} Hz (se regresa en {secs} s si no se confirma)");
        pending?.Dispose();
        pending = new System.Threading.Timer(_ => Revert("no se confirmó"), null, secs * 1000, System.Threading.Timeout.Infinite);
        return new { ok = true, prev = new { w = before.Value.W, h = before.Value.H, hz = before.Value.Hz } };
    }

    public static object Keep()
    {
        if (pending == null || pendingDevice == null) return new { ok = true };
        pending.Dispose(); pending = null;
        var cur = CurrentMode(pendingDevice);
        if (cur != null) Apply(pendingDevice, cur.Value, CDS_UPDATEREGISTRY);   // ahora sí se guarda
        Log.Write("pantalla: se mantiene " + (cur is Mode m ? $"{m.W}×{m.H} {m.Hz} Hz" : ""));
        before = null; pendingDevice = null;
        return new { ok = true };
    }

    public static object Revert(string why = "Regresar")
    {
        var t = pending; pending = null; t?.Dispose();
        if (before is Mode b && pendingDevice != null)
        {
            Apply(pendingDevice, b, CDS_UPDATEREGISTRY);
            Log.Write($"pantalla: se regresó a {b.W}×{b.H} {b.Hz} Hz ({why})");
            before = null; pendingDevice = null;
            try { Reverted?.Invoke(); } catch { }
        }
        return new { ok = true };
    }

    // ───── HDR ─────
    /// <summary>off: apagado · auto: se prende solo si la pantalla tiene HDR · on: siempre (solo si la pantalla lo tiene).</summary>
    public static object SetHdr(Config cfg, string mode)
    {
        cfg.HdrMode = mode is "off" or "on" or "auto" ? mode : "auto"; cfg.Save();
        ApplyHdr(cfg);
        return State(cfg);
    }

    public static void ApplyHdr(Config cfg)
    {
        try
        {
            foreach (var s in Screens())
            {
                if (!s.HdrSupported) continue;
                bool want = cfg.HdrMode != "off";
                if (want == s.HdrOn) continue;
                var set = new COLOR_SET { header = { type = SET_ADVANCED_COLOR_STATE, size = (uint)Marshal.SizeOf<COLOR_SET>(), adapterId = new LUID { Low = s.Adapter.Low, High = s.Adapter.High }, id = s.TargetId }, value = want ? 1u : 0u };
                int r = DisplayConfigSetDeviceInfo(ref set);
                Log.Write($"pantalla: HDR {(want ? "encendido" : "apagado")} en {s.Name}{(r != 0 ? $" (Windows respondió {r})" : "")}");
            }
        }
        catch (Exception ex) { Log.Error("pantalla (HDR)", ex); }
    }

    // ───── pantalla externa «como en la Switch» ─────
    static DateTime lastTopology = DateTime.MinValue;

    /// <summary>
    /// Si hay una pantalla externa y Windows también usa la de la consola (extendida o duplicada) o solo la de la consola,
    /// pasa todo a la externa. Se llama al arrancar y cada vez que cambian las pantallas. Regresa true si cambió algo.
    /// </summary>
    public static bool ExternalOnly()
    {
        try
        {
            if ((DateTime.UtcNow - lastTopology).TotalSeconds < 4) return false;   // el cambio que hicimos también avisa: no se repite
            if (!ExternalAvailable()) return false;
            var active = Screens();
            if (active.Count > 0 && active.All(s => !s.Internal)) return false;   // ya está solo en la externa
            lastTopology = DateTime.UtcNow;
            int r = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, SDC_APPLY | SDC_TOPOLOGY_EXTERNAL);
            Log.Write("pantalla: se conectó una pantalla externa; se usa solo esa" + (r != 0 ? $" (Windows respondió {r})" : ""));
            return r == 0;
        }
        catch (Exception ex) { Log.Error("pantalla (externa)", ex); return false; }
    }
}
