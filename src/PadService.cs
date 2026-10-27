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
            // Un mando con dos caras (Xbox + HID) llega como dos avisos: se espera un momento y se compara la lista ya
            // limpia, así se anuncia una sola vez y con su dibujo correcto.
            WGI.RawGameController.RawGameControllerAdded += (_, c) => _ = Changed((DateTime.Now - born).TotalSeconds >= 5);
            WGI.RawGameController.RawGameControllerRemoved += (_, c) => _ = Changed(true);
        }
        catch (Exception ex) { Log.Error("Windows.Gaming.Input", ex); }
    }

    Dictionary<string, string> known;   // mandos que ya se conocen (clave → nombre)
    readonly SemaphoreSlim changing = new(1, 1);

    async Task Changed(bool announce)
    {
        await Task.Delay(1000);
        await changing.WaitAsync();
        try
        {
            var before = known;
            var now = List(remember: false);
            var map = now.GroupBy(d => (string)d["key"]).ToDictionary(g => g.Key, g => (string)g.First()["name"]);
            known = map;
            if (!announce || before == null) return;   // los que ya estaban al abrir la app no se anuncian
            foreach (var d in now.Where(d => !before.ContainsKey((string)d["key"]))) Connected?.Invoke(d);
            foreach (var kv in before.Where(kv => !map.ContainsKey(kv.Key))) Disconnected?.Invoke(new Dictionary<string, object> { ["key"] = kv.Key, ["name"] = kv.Value });
        }
        catch (Exception ex) { Log.Error("mandos (cambio)", ex); }
        finally { changing.Release(); }
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
            ["vendor"] = (int)c.HardwareVendorId,
            ["layout"] = Layout(c)   // qué dibujo lleva: por cómo se comporta el mando, no por la marca
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

    /// <summary>
    /// Tipo de mando: "xbox" si Windows lo maneja como mando Xbox (XInput: Machenike, 8BitDo en modo X, etc.), "ps" si es
    /// de Sony, si no "generic".
    /// </summary>
    static string Layout(WGI.RawGameController c)
    {
        if (c.HardwareVendorId == 0x054C) return "ps";
        try { if (c.HardwareVendorId == 0x045E || WGI.Gamepad.FromGameController(c) != null) return "xbox"; } catch { }
        return "generic";
    }

    /// <summary>Nombres que Windows le pone a la "otra cara" HID de un mando que ya aparece como Xbox.</summary>
    static bool GenericHid(WGI.RawGameController c)
    {
        string n = (c.DisplayName ?? "").ToLowerInvariant();
        return n.Length == 0 || n.Contains("hid") || n.Contains("dispositivo de juego") || n.Contains("game controller") || n.Contains("controlador de juego") || n.Contains("gamepad");
    }

    public List<Dictionary<string, object>> List(bool remember = true)
    {
        var list = new List<Dictionary<string, object>>();
        List<WGI.RawGameController> raws;
        try { raws = WGI.RawGameController.RawGameControllers.ToList(); }
        catch (Exception ex) { Log.Error("mandos", ex); raws = new List<WGI.RawGameController>(); }

        // El mando integrado de la Ally: por fabricante (ASUS); si no aparece así, el primer mando con cable.
        var integ = raws.FirstOrDefault(IsAsus) ?? raws.FirstOrDefault(r => !r.IsWireless);
        list.Add(integ != null ? Describe(integ, true)
            : new Dictionary<string, object> { ["key"] = "ally", ["name"] = "ROG Xbox Ally X", ["type"] = "Integrado", ["vendor"] = 0x0B05 });
        // Muchos mandos (Machenike, 8BitDo…) se ven en Windows dos veces: como mando Xbox y como "dispositivo de juego HID".
        // Es el mismo mando: si ya hay uno tipo Xbox, la cara HID genérica (sin forma de mando Xbox) no se cuenta.
        var others = raws.Where(r => r != integ).ToList();
        bool anyXbox = others.Any(r => Layout(r) == "xbox");
        foreach (var r in others)
        {
            if (anyXbox && Layout(r) == "generic" && GenericHid(r)) { Log.Write($"mandos: «{r.DisplayName}» es la otra cara de un mando Xbox; no se cuenta dos veces"); continue; }
            list.Add(Describe(r, false));
        }

        int next = 1;
        foreach (var d in list.Select((d, i) => (d, i)).OrderBy(x => cfg.PadOrder.TryGetValue((string)x.d["key"], out var o) ? o : 100 + x.i).Select(x => x.d))
            d["player"] = next++;
        if (remember && known == null) known = list.GroupBy(d => (string)d["key"]).ToDictionary(g => g.Key, g => (string)g.First()["name"]);
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
