using System.Management;
using Windows.Devices.Radios;

namespace XboxScarlet;

/// <summary>Brillo, volumen, modo de energía, Wi-Fi / modo avión, almacenamiento y Armoury Crate.</summary>
sealed class SystemService
{
    // ───── Brillo (WMI, pantallas integradas) ─────

    /// <summary>Brillo de la pantalla integrada. null = esta pantalla no deja cambiarlo (una TV o un monitor externo).</summary>
    public static int? GetBrightness()
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness, Active FROM WmiMonitorBrightness");
            foreach (ManagementObject o in s.Get())
                using (o) if (o["Active"] is not bool a || a) return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Cambia el brillo y comprueba que haya quedado. Se llama a través de AudioMixer.Queue (solo el último valor): WMI
    /// tarda y antes, al moverlo rápido, los cambios llegaban desordenados y quedaba un valor viejo.
    /// </summary>
    public static void SetBrightness(int value)
    {
        byte want = (byte)Math.Clamp(value, 0, 100);
        try
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            int n = 0;
            foreach (ManagementObject o in s.Get())
                using (o) { o.InvokeMethod("WmiSetBrightness", new object[] { (uint)0, want }); n++; }
            if (n == 0) { Log.Write("brillo: esta pantalla no deja cambiar el brillo"); return; }
            int? now = GetBrightness();
            if (now != null && Math.Abs(now.Value - want) > 3)
                Log.Write($"brillo: se pidió {want} y quedó en {now} (¿brillo automático de Windows o del fabricante?)");
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
            lastAccess = await Radio.RequestAccessAsync();
            if (lastAccess != RadioAccessStatus.Allowed) { Log.Write("radios: Windows no da acceso (" + lastAccess + ")"); return null; }
            return await Radio.GetRadiosAsync();
        }
        catch (Exception ex) { Log.Error("radios", ex); return null; }
    }

    static RadioAccessStatus lastAccess = RadioAccessStatus.Unspecified;
    /// <summary>Para Diagnóstico: si Windows deja a Scarlet cambiar el Wi-Fi.</summary>
    public static string RadioAccess => lastAccess switch
    {
        RadioAccessStatus.Allowed => "permitido",
        RadioAccessStatus.DeniedByUser => "bloqueado en Configuración › Privacidad › Radios",
        RadioAccessStatus.DeniedBySystem => "bloqueado por Windows",
        _ => "sin revisar todavía"
    };

    /// <summary>
    /// Wi-Fi y modo avión. Como en Windows: el modo avión apaga todo, pero con el modo avión puesto puedes volver a
    /// prender solo el Wi-Fi (antes, prenderlo lo volvía a apagar). Devuelve cómo quedó de verdad, para que el menú no
    /// muestre un interruptor que no corresponde; si Windows no deja, dice por qué.
    /// </summary>
    public async Task<Dictionary<string, object>> SetRadiosAsync(bool wifi, bool airplane)
    {
        var radios = await Radios();
        if (radios == null || radios.Count == 0)
            return new Dictionary<string, object> { ["error"] = lastAccess == RadioAccessStatus.DeniedByUser
                ? "Windows no deja a Scarlet cambiar el Wi-Fi: Configuración › Privacidad › Radios › permitir"
                : "No se encontró el Wi-Fi" };
        foreach (var r in radios)
        {
            try
            {
                RadioState target = r.Kind == RadioKind.WiFi ? (wifi ? RadioState.On : RadioState.Off) : (airplane ? RadioState.Off : RadioState.On);
                if (r.State == target) continue;
                var res = await r.SetStateAsync(target);
                if (res != RadioAccessStatus.Allowed) Log.Write($"radio {r.Kind}: Windows no dejó cambiarla ({res})");
            }
            catch (Exception ex) { Log.Error("radio " + r.Kind, ex); }
        }
        await Task.Delay(300);
        var after = await Radios();
        var w = after?.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
        return new Dictionary<string, object>
        {
            ["wifi"] = w?.State == RadioState.On,
            ["airplane"] = AirplaneOf(after)
        };
    }

    /// <summary>Modo avión: todo lo que no es Wi-Fi apagado (con el modo avión se puede volver a prender solo el Wi-Fi).</summary>
    static bool AirplaneOf(IReadOnlyList<Radio> radios)
    {
        if (radios == null || radios.Count == 0) return false;
        var others = radios.Where(r => r.Kind != RadioKind.WiFi).ToList();
        return others.Count > 0 ? others.All(r => r.State != RadioState.On) : radios.All(r => r.State != RadioState.On);
    }

    // ───── Estado para el menú rápido ─────

    public async Task<Dictionary<string, object>> StateAsync()
    {
        int? bri = await Task.Run(GetBrightness);
        var st = await Task.Run(() => new Dictionary<string, object>
        {
            ["brightness"] = bri,
            ["brightnessOk"] = bri != null,   // false = esta pantalla no deja cambiarlo (TV, monitor externo)
            ["volume"] = AudioMixer.GetMaster() ?? GetVolume(),   // el real: 0 si está silenciado
            ["power"] = GetPower()
        });
        var radios = await Radios();
        if (radios != null && radios.Count > 0)
        {
            var wifi = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
            if (wifi != null) st["wifi"] = wifi.State == RadioState.On;
            st["airplane"] = AirplaneOf(radios);
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
