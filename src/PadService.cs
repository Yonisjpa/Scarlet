using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using WGI = Windows.Gaming.Input;

namespace XboxScarlet;

/// <summary>Mandos conectados (con tipo Xbox / DualSense / otro), batería, vibrar para identificar y emparejar por Bluetooth.</summary>
sealed class PadService
{
    readonly Config cfg;
    readonly DateTime born = DateTime.Now;
    DeviceWatcher classic, le;

    public event Action<Dictionary<string, object>> Connected, Disconnected;
    public event Action<string, string> Found;

    public PadService(Config cfg)
    {
        this.cfg = cfg;
        try
        {
            WGI.RawGameController.RawGameControllerAdded += (_, c) =>
            {
                if ((DateTime.Now - born).TotalSeconds < 5) return;   // los que ya estaban al abrir la app no se anuncian
                try { Connected?.Invoke(Describe(c, IsAsus(c))); } catch (Exception ex) { Log.Error("mando +", ex); }
            };
            WGI.RawGameController.RawGameControllerRemoved += (_, c) =>
            {
                try { Disconnected?.Invoke(new Dictionary<string, object> { ["key"] = Key(c), ["name"] = Name(c) }); } catch (Exception ex) { Log.Error("mando -", ex); }
            };
        }
        catch (Exception ex) { Log.Error("Windows.Gaming.Input", ex); }
    }

    static string Key(WGI.RawGameController c) =>
        !string.IsNullOrEmpty(c.NonRoamableId) ? c.NonRoamableId : $"{c.HardwareVendorId:x4}-{c.HardwareProductId:x4}";

    static bool IsAsus(WGI.RawGameController c) =>
        c.HardwareVendorId == 0x0B05 || (c.DisplayName ?? "").Contains("Ally", StringComparison.OrdinalIgnoreCase);

    static string Name(WGI.RawGameController c)
    {
        string n = c.DisplayName ?? "";
        if (c.HardwareVendorId == 0x054C) return c.HardwareProductId is 0x0CE6 or 0x0DF2 ? "DualSense Wireless Controller" : "DualShock 4";
        if (c.HardwareVendorId == 0x045E && !n.Contains("xbox", StringComparison.OrdinalIgnoreCase)) return "Mando Xbox";
        return n.Length > 0 ? n : "Mando";
    }

    static Dictionary<string, object> Describe(WGI.RawGameController c, bool integrated)
    {
        var d = new Dictionary<string, object>
        {
            ["key"] = integrated ? "ally" : Key(c),
            ["name"] = integrated ? "ROG Xbox Ally X" : Name(c),
            ["type"] = integrated ? "Integrado" : c.IsWireless ? "Inalámbrico" : "USB",
            ["vendor"] = (int)c.HardwareVendorId
        };
        try
        {
            var b = c.TryGetBatteryReport();
            if (b != null && b.FullChargeCapacityInMilliwattHours is int full && full > 0 && b.RemainingCapacityInMilliwattHours is int rem)
                d["battery"] = Math.Round(Math.Clamp((double)rem / full, 0, 1), 2);
        }
        catch { }
        return d;
    }

    public List<Dictionary<string, object>> List()
    {
        var list = new List<Dictionary<string, object>>();
        List<WGI.RawGameController> raws;
        try { raws = WGI.RawGameController.RawGameControllers.ToList(); }
        catch (Exception ex) { Log.Error("mandos", ex); raws = new List<WGI.RawGameController>(); }

        // El mando integrado de la Ally: por fabricante (ASUS); si no aparece así, el primer mando con cable.
        var integ = raws.FirstOrDefault(IsAsus) ?? raws.FirstOrDefault(r => !r.IsWireless);
        list.Add(integ != null ? Describe(integ, true)
            : new Dictionary<string, object> { ["key"] = "ally", ["name"] = "ROG Xbox Ally X", ["type"] = "Integrado", ["vendor"] = 0x0B05 });
        foreach (var r in raws)
            if (r != integ) list.Add(Describe(r, false));

        int next = 1;
        foreach (var d in list.Select((d, i) => (d, i)).OrderBy(x => cfg.PadOrder.TryGetValue((string)x.d["key"], out var o) ? o : 100 + x.i).Select(x => x.d))
            d["player"] = next++;
        return list;
    }

    WGI.RawGameController Find(string key) =>
        WGI.RawGameController.RawGameControllers.FirstOrDefault(r => Key(r) == key || (key == "ally" && IsAsus(r)));

    /// <summary>Hace vibrar un mando para saber cuál es. false si no se pudo (el de la Ally lo vibra MainForm por XInput).</summary>
    public bool Rumble(string key)
    {
        try
        {
            var raw = Find(key);
            var gp = raw != null ? WGI.Gamepad.FromGameController(raw) : null;
            if (gp == null) return false;
            gp.Vibration = new WGI.GamepadVibration { LeftMotor = .8, RightMotor = .8 };
            _ = Task.Delay(600).ContinueWith(_ => { try { gp.Vibration = new WGI.GamepadVibration(); } catch { } });
            return true;
        }
        catch (Exception ex) { Log.Error("vibrar mando", ex); return false; }
    }

    // ───── Bluetooth ─────

    public void Scan()
    {
        StopScan();
        string[] props = { "System.Devices.Aep.IsPaired" };
        classic = DeviceInformation.CreateWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(false), props, DeviceInformationKind.AssociationEndpoint);
        le = DeviceInformation.CreateWatcher(BluetoothLEDevice.GetDeviceSelectorFromPairingState(false), props, DeviceInformationKind.AssociationEndpoint);
        foreach (var w in new[] { classic, le })
        {
            w.Added += (_, info) => { if (!string.IsNullOrWhiteSpace(info.Name)) Found?.Invoke(info.Id, info.Name); };
            w.Updated += (_, _) => { };
            w.Removed += (_, _) => { };
            try { w.Start(); } catch (Exception ex) { Log.Error("bluetooth", ex); }
        }
        _ = Task.Delay(45000).ContinueWith(_ => StopScan());
    }

    void StopScan()
    {
        foreach (var w in new[] { classic, le })
        {
            try
            {
                if (w != null && (w.Status == DeviceWatcherStatus.Started || w.Status == DeviceWatcherStatus.EnumerationCompleted)) w.Stop();
            }
            catch { }
        }
    }

    public async Task<bool> PairAsync(string id)
    {
        var di = await DeviceInformation.CreateFromIdAsync(id);
        if (di.Pairing.IsPaired) return true;
        di.Pairing.Custom.PairingRequested += (_, a) => a.Accept();
        var r = await di.Pairing.Custom.PairAsync(DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch | DevicePairingKinds.DisplayPin);
        Log.Write($"emparejar {di.Name}: {r.Status}");
        if (r.Status == DevicePairingResultStatus.Paired || r.Status == DevicePairingResultStatus.AlreadyPaired) return true;
        throw new InvalidOperationException("No se pudo emparejar (" + r.Status + ")");
    }

    /// <summary>Olvida un mando Bluetooth (lo busca por nombre entre los emparejados).</summary>
    public async Task<bool> UnpairAsync(string key)
    {
        var raw = Find(key);
        var names = new[] { raw?.DisplayName, raw != null ? Name(raw) : null }.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (names.Count == 0) return false;
        foreach (var sel in new[] { BluetoothDevice.GetDeviceSelectorFromPairingState(true), BluetoothLEDevice.GetDeviceSelectorFromPairingState(true) })
        {
            foreach (var d in await DeviceInformation.FindAllAsync(sel))
            {
                if (string.IsNullOrEmpty(d.Name)) continue;
                if (!names.Any(n => n.Contains(d.Name, StringComparison.OrdinalIgnoreCase) || d.Name.Contains(n, StringComparison.OrdinalIgnoreCase))) continue;
                var r = await d.Pairing.UnpairAsync();
                Log.Write($"olvidar {d.Name}: {r.Status}");
                return r.Status == DeviceUnpairingResultStatus.Unpaired;
            }
        }
        return false;
    }
}
