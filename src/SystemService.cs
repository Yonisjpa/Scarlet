using System.Management;
using Windows.Devices.Radios;

namespace XboxScarlet;

/// <summary>Brillo, volumen, modo de energía, Wi-Fi / modo avión, almacenamiento y Armoury Crate.</summary>
sealed class SystemService
{
    // ───── Brillo (WMI, pantallas integradas) ─────

    static int? GetBrightness()
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject o in s.Get())
                using (o) return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch { }
        return null;
    }

    public static void SetBrightness(int value)
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject o in s.Get())
                using (o) o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)Math.Clamp(value, 0, 100) });
        }
        catch (Exception ex) { Log.Error("brillo", ex); }
    }

    // ───── Volumen (Core Audio) ─────

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    }

    static IAudioEndpointVolume Endpoint()
    {
        var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        if (en.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var dev) != 0 || dev == null) return null;
        var iid = typeof(IAudioEndpointVolume).GUID;
        if (dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out object o) != 0) return null;
        return o as IAudioEndpointVolume;
    }

    static int? GetVolume()
    {
        try
        {
            var v = Endpoint();
            if (v != null && v.GetMasterVolumeLevelScalar(out float level) == 0) return (int)Math.Round(level * 100);
        }
        catch (Exception ex) { Log.Error("volumen", ex); }
        return null;
    }

    public static void SetVolume(int value)
    {
        try
        {
            var v = Endpoint();
            if (v == null) return;
            var ctx = Guid.Empty;
            v.SetMasterVolumeLevelScalar(Math.Clamp(value, 0, 100) / 100f, ref ctx);
        }
        catch (Exception ex) { Log.Error("volumen", ex); }
    }

    // ───── Modo de energía de Windows (el mismo de Configuración › Sistema › Energía) ─────

    static readonly Dictionary<string, Guid> Overlays = new()
    {
        ["silent"] = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a"),   // mayor eficiencia energética
        ["perf"] = Guid.Empty,                                             // equilibrado
        ["turbo"] = new Guid("ded574b5-45a0-4f42-8737-46345c09c238")      // máximo rendimiento
    };

    static string GetPower()
    {
        try
        {
            if (Native.PowerGetEffectiveOverlayScheme(out var g) == 0)
                foreach (var kv in Overlays) if (kv.Value == g) return kv.Key;
        }
        catch { }
        return null;
    }

    public static void SetPower(string profile)
    {
        if (profile == null || !Overlays.TryGetValue(profile, out var g)) return;
        try { Native.PowerSetActiveOverlayScheme(g); }
        catch (Exception ex) { Log.Error("energía", ex); }
    }

    // ───── Wi-Fi y modo avión ─────

    static async Task<IReadOnlyList<Radio>> Radios()
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return null;
            return await Radio.GetRadiosAsync();
        }
        catch (Exception ex) { Log.Error("radios", ex); return null; }
    }

    public async Task SetRadiosAsync(bool wifi, bool airplane)
    {
        var radios = await Radios();
        if (radios == null) return;
        foreach (var r in radios)
        {
            try
            {
                RadioState target = airplane ? RadioState.Off
                    : r.Kind == RadioKind.WiFi ? (wifi ? RadioState.On : RadioState.Off)
                    : RadioState.On;
                if (r.State != target) await r.SetStateAsync(target);
            }
            catch (Exception ex) { Log.Error("radio " + r.Kind, ex); }
        }
    }

    // ───── Estado para el menú rápido ─────

    public async Task<Dictionary<string, object>> StateAsync()
    {
        var st = await Task.Run(() => new Dictionary<string, object>
        {
            ["brightness"] = GetBrightness(),
            ["volume"] = AudioMixer.GetMaster() ?? GetVolume(),   // el real: 0 si está silenciado
            ["power"] = GetPower()
        });
        var radios = await Radios();
        if (radios != null && radios.Count > 0)
        {
            bool anyOn = radios.Any(r => r.State == RadioState.On);
            var wifi = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
            if (wifi != null) st["wifi"] = wifi.State == RadioState.On;
            st["airplane"] = !anyOn;
        }
        return st;
    }

    public static object Storage(string steamPath)
    {
        try
        {
            var di = new DriveInfo(Path.GetPathRoot(steamPath ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\");
            return new { total = di.TotalSize / 1073741824.0, free = di.AvailableFreeSpace / 1073741824.0 };
        }
        catch (Exception ex) { Log.Error("almacenamiento", ex); return null; }
    }

    // ───── Armoury Crate ─────

    string armouryId;

    public async Task<bool> OpenArmouryAsync()
    {
        armouryId ??= await Task.Run(() => RunPowerShell("Get-StartApps | Where-Object { $_.Name -like '*Armoury*' } | Select-Object -First 1 -ExpandProperty AppID")) ?? "";
        if (armouryId.Length == 0) { Log.Write("No encontré Armoury Crate en el menú Inicio"); return false; }
        Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + armouryId) { UseShellExecute = true });
        return true;
    }

    static string RunPowerShell(string command)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"" + command.Replace("\"", "\\\"") + "\"")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            string o = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(8000);
            return o.Length > 0 ? o.Split('\n')[0].Trim() : null;
        }
        catch (Exception ex) { Log.Error("powershell", ex); return null; }
    }
}
