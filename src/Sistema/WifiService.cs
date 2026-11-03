namespace XboxScarlet;

/// <summary>
/// Wi-Fi sin entrar a la configuración de Windows (beta 1.2): redes registradas y encontradas, conectarse (con
/// contraseña si hace falta), olvidar una red, estado de la conexión y prueba de conexión.
/// Usa la API nativa de Wi-Fi de Windows (wlanapi), que no depende del idioma de Windows.
/// Ojo: en Windows 11 24H2, Windows solo deja ver las redes de alrededor a las apps que tienen permiso de ubicación
/// («Permitir que las aplicaciones de escritorio accedan a tu ubicación»); sin ese permiso la lista llega vacía.
/// </summary>
static class Wifi
{
    // ───── API nativa ─────
    [DllImport("wlanapi.dll")] static extern int WlanOpenHandle(uint ver, IntPtr reserved, out uint negotiated, out IntPtr h);
    [DllImport("wlanapi.dll")] static extern int WlanCloseHandle(IntPtr h, IntPtr reserved);
    [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr p);
    [DllImport("wlanapi.dll")] static extern int WlanEnumInterfaces(IntPtr h, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] static extern int WlanScan(IntPtr h, ref Guid iface, IntPtr ssid, IntPtr ie, IntPtr reserved);
    [DllImport("wlanapi.dll")] static extern int WlanGetAvailableNetworkList(IntPtr h, ref Guid iface, uint flags, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] static extern int WlanGetNetworkBssList(IntPtr h, ref Guid iface, IntPtr ssid, int bssType, bool secure, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] static extern int WlanGetProfileList(IntPtr h, ref Guid iface, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] static extern int WlanDeleteProfile(IntPtr h, ref Guid iface, string name, IntPtr reserved);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] static extern int WlanSetProfile(IntPtr h, ref Guid iface, uint flags, string xml, string sec, bool overwrite, IntPtr reserved, out uint reason);
    [DllImport("wlanapi.dll")] static extern int WlanQueryInterface(IntPtr h, ref Guid iface, int opcode, IntPtr reserved, out int size, out IntPtr data, out int type);
    [DllImport("wlanapi.dll")] static extern int WlanConnect(IntPtr h, ref Guid iface, ref ConnParams p, IntPtr reserved);
    [DllImport("wlanapi.dll")] static extern int WlanDisconnect(IntPtr h, ref Guid iface, IntPtr reserved);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ConnParams
    {
        public int Mode;                                   // 0 = con un perfil guardado
        [MarshalAs(UnmanagedType.LPWStr)] public string Profile;
        public IntPtr Ssid, Bssids;
        public int BssType;                                // 1 = infraestructura
        public uint Flags;
    }

    const int ErrAccessDenied = 5;
    const int OpCurrentConnection = 7;

    sealed class Session : IDisposable
    {
        public IntPtr H; public Guid Iface; public string Desc; public int State; public int Error;
        public Session()
        {
            Error = WlanOpenHandle(2, IntPtr.Zero, out _, out H);
            if (Error != 0) return;
            Error = WlanEnumInterfaces(H, IntPtr.Zero, out var list);
            if (Error != 0) return;
            try
            {
                int n = Marshal.ReadInt32(list);
                if (n == 0) { Error = -1; return; }
                IntPtr it = list + 8;   // el primer adaptador Wi-Fi
                Iface = Marshal.PtrToStructure<Guid>(it);
                Desc = Marshal.PtrToStringUni(it + 16, 256).TrimEnd('\0');
                State = Marshal.ReadInt32(it + 16 + 512);
            }
            finally { WlanFreeMemory(list); }
        }
        public void Dispose() { if (H != IntPtr.Zero) WlanCloseHandle(H, IntPtr.Zero); H = IntPtr.Zero; }
    }

    static string Ssid(IntPtr p)
    {
        int len = Math.Clamp(Marshal.ReadInt32(p), 0, 32);
        var b = new byte[len]; Marshal.Copy(p + 4, b, 0, len);
        return Encoding.UTF8.GetString(b);
    }

    static string Auth(int a, bool secure) => a switch
    {
        1 => secure ? "WEP" : "Abierta", 2 => "WEP", 3 => "WPA-Enterprise", 4 => "WPA-Personal", 6 => "WPA2-Enterprise",
        7 => "WPA2-Personal", 8 or 11 => "WPA3-Enterprise", 9 => "WPA3-Personal", 10 => "Abierta (cifrada, OWE)", _ => secure ? "Protegida" : "Abierta"
    };

    static int Channel(uint khz)
    {
        int mhz = (int)(khz / 1000);
        if (mhz == 2484) return 14;
        if (mhz >= 2412 && mhz < 2484) return (mhz - 2407) / 5;
        if (mhz >= 5955) return (mhz - 5950) / 5;
        if (mhz >= 5000) return (mhz - 5000) / 5;
        return 0;
    }
    static string Band(uint khz) => khz == 0 ? "" : khz < 3_000_000 ? "2.4 GHz" : khz >= 5_925_000 ? "6 GHz" : "5 GHz";

    /// <summary>Frecuencia de cada red (por nombre) y de cada punto de acceso (por MAC).</summary>
    static (Dictionary<string, uint> bySsid, Dictionary<string, uint> byMac, Dictionary<string, int> rssi) Bss(Session s)
    {
        var bySsid = new Dictionary<string, uint>(); var byMac = new Dictionary<string, uint>(); var rssi = new Dictionary<string, int>();
        if (WlanGetNetworkBssList(s.H, ref s.Iface, IntPtr.Zero, 3, false, IntPtr.Zero, out var list) != 0) return (bySsid, byMac, rssi);
        try
        {
            int total = Marshal.ReadInt32(list), n = Marshal.ReadInt32(list + 4);
            int stride = n > 0 ? (total - 8) / n : 360;
            if (stride < 96) stride = 360;
            for (int i = 0; i < n; i++)
            {
                IntPtr e = list + 8 + i * stride;
                string ssid = Ssid(e);
                var mac = new byte[6]; Marshal.Copy(e + 40, mac, 0, 6);
                int r = Marshal.ReadInt32(e + 56);
                uint f = (uint)Marshal.ReadInt32(e + 92);
                string m = BitConverter.ToString(mac);
                byMac[m] = f;
                // de las varias antenas de una red, la que mejor se oye
                if (!rssi.TryGetValue(ssid, out int best) || r > best) { rssi[ssid] = r; bySsid[ssid] = f; }
            }
        }
        finally { WlanFreeMemory(list); }
        return (bySsid, byMac, rssi);
    }

    static List<string> Profiles(Session s)
    {
        var l = new List<string>();
        if (WlanGetProfileList(s.H, ref s.Iface, IntPtr.Zero, out var list) != 0) return l;
        try
        {
            int n = Marshal.ReadInt32(list);
            for (int i = 0; i < n; i++) l.Add(Marshal.PtrToStringUni(list + 8 + i * 516, 256).TrimEnd('\0'));
        }
        finally { WlanFreeMemory(list); }
        return l;
    }

    static Dictionary<string, object> Current(Session s, Dictionary<string, uint> byMac)
    {
        if (WlanQueryInterface(s.H, ref s.Iface, OpCurrentConnection, IntPtr.Zero, out _, out var d, out _) != 0) return null;
        try
        {
            int state = Marshal.ReadInt32(d);
            if (state != 1) return null;   // 1 = conectado
            string profile = Marshal.PtrToStringUni(d + 8, 256).TrimEnd('\0');
            string ssid = Ssid(d + 520);
            var mac = new byte[6]; Marshal.Copy(d + 560, mac, 0, 6);
            int quality = Marshal.ReadInt32(d + 576);
            uint rx = (uint)Marshal.ReadInt32(d + 580), tx = (uint)Marshal.ReadInt32(d + 584);
            bool secure = Marshal.ReadInt32(d + 588) != 0;
            int auth = Marshal.ReadInt32(d + 596);
            string bssid = BitConverter.ToString(mac);
            byMac.TryGetValue(bssid, out uint f);
            var nic = WifiNic();
            var ip = nic?.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            var gw = nic?.GetIPProperties().GatewayAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            var dns = nic?.GetIPProperties().DnsAddresses.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(a => a.ToString()).ToList();
            string pmac = nic == null ? "" : string.Join(":", nic.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
            return new Dictionary<string, object>
            {
                ["ssid"] = string.IsNullOrEmpty(ssid) ? profile : ssid, ["profile"] = profile, ["signal"] = quality,
                ["secure"] = secure, ["security"] = Auth(auth, secure), ["band"] = Band(f), ["channel"] = f == 0 ? 0 : Channel(f),
                ["bssid"] = bssid.Replace('-', ':'), ["rx"] = rx / 1000, ["tx"] = tx / 1000,
                ["ip"] = ip?.Address.ToString() ?? "", ["mask"] = ip?.IPv4Mask?.ToString() ?? "", ["gateway"] = gw?.Address.ToString() ?? "",
                ["dns"] = dns == null ? "" : string.Join(", ", dns), ["mac"] = pmac
            };
        }
        finally { WlanFreeMemory(d); }
    }

    static System.Net.NetworkInformation.NetworkInterface WifiNic() =>
        System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
            .OrderByDescending(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up).FirstOrDefault();

    /// <summary>Todo lo que se ve en Ajustes › Red y VPN › Internet. scan: pedirle a Windows que busque redes otra vez.</summary>
    public static async Task<object> ListAsync(bool scan)
    {
        return await Task.Run<object>(async () =>
        {
            using var s = new Session();
            if (s.Error == -1) return new { ok = false, error = "Esta consola no tiene Wi-Fi (o está apagado en Windows)" };
            if (s.Error != 0) return new { ok = false, error = $"Windows no dejó leer el Wi-Fi (código {s.Error})" };
            if (scan) { WlanScan(s.H, ref s.Iface, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero); await Task.Delay(3500); }
            var (bySsid, byMac, rssi) = Bss(s);
            var saved = Profiles(s);
            var cur = Current(s, byMac);
            int err = WlanGetAvailableNetworkList(s.H, ref s.Iface, 0, IntPtr.Zero, out var list);
            var nets = new Dictionary<string, Dictionary<string, object>>();
            if (err == 0)
            {
                try
                {
                    int n = Marshal.ReadInt32(list);
                    for (int i = 0; i < n; i++)
                    {
                        IntPtr e = list + 8 + i * 628;
                        string ssid = Ssid(e + 512);
                        if (string.IsNullOrEmpty(ssid)) continue;   // redes ocultas
                        int q = Marshal.ReadInt32(e + 604);
                        bool secure = Marshal.ReadInt32(e + 608) != 0, connectable = Marshal.ReadInt32(e + 556) != 0;
                        int auth = Marshal.ReadInt32(e + 612); uint flags = (uint)Marshal.ReadInt32(e + 620);
                        bool connected = (flags & 1) != 0;
                        // la misma red sale una vez por perfil: se queda la mejor señal (y la conectada)
                        if (nets.TryGetValue(ssid, out var old) && !connected && (int)old["signal"] >= q) continue;
                        bySsid.TryGetValue(ssid, out uint f);
                        nets[ssid] = new Dictionary<string, object>
                        {
                            ["ssid"] = ssid, ["signal"] = q, ["bars"] = q >= 75 ? 3 : q >= 45 ? 2 : q > 0 ? 1 : 0, ["secure"] = secure,
                            ["security"] = Auth(auth, secure), ["saved"] = saved.Contains(ssid) || (flags & 2) != 0, ["connected"] = connected,
                            ["connectable"] = connectable, ["band"] = Band(f), ["sae"] = auth == 9, ["open"] = !secure
                        };
                    }
                }
                finally { WlanFreeMemory(list); }
            }
            bool blocked = err == ErrAccessDenied || (nets.Count == 0 && cur == null && rssi.Count == 0 && scan);
            if (err != 0 && err != ErrAccessDenied) Log.Write($"wifi: lista de redes con error {err}");
            return new
            {
                ok = true, adapter = s.Desc, current = cur, saved,
                networks = nets.Values.OrderByDescending(x => (bool)x["connected"]).ThenByDescending(x => (int)x["signal"]).ToList(),
                location = blocked   // Windows 11 24H2: sin permiso de ubicación no deja ver las redes
            };
        });
    }

    static string Esc(string s) => System.Security.SecurityElement.Escape(s);

    static string ProfileXml(string ssid, string password, bool sae, bool open)
    {
        string hex = Convert.ToHexString(Encoding.UTF8.GetBytes(ssid));
        string sec = open
            ? "<authEncryption><authentication>open</authentication><encryption>none</encryption><useOneX>false</useOneX></authEncryption>"
            : $"<authEncryption><authentication>{(sae ? "WPA3SAE" : "WPA2PSK")}</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption>" +
              $"<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{Esc(password)}</keyMaterial></sharedKey>";
        return "<?xml version=\"1.0\"?><WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">" +
               $"<name>{Esc(ssid)}</name><SSIDConfig><SSID><hex>{hex}</hex><name>{Esc(ssid)}</name></SSID></SSIDConfig>" +
               "<connectionType>ESS</connectionType><connectionMode>auto</connectionMode>" +
               $"<MSM><security>{sec}</security></MSM></WLANProfile>";
    }

    /// <summary>
    /// Conectarse. Con contraseña (o abierta) se crea su perfil en Windows; si no conecta (contraseña mala), ese perfil
    /// nuevo se borra. Sin contraseña usa el perfil guardado. Espera hasta 25 s a que quede conectada.
    /// </summary>
    public static Task<object> ConnectAsync(string ssid, string password, bool sae, bool open) => Task.Run<object>(async () =>
    {
        using var s = new Session();
        if (s.Error != 0) return new { ok = false, error = "No encontré el Wi-Fi" };
        bool created = false;
        var saved = Profiles(s);
        string profile = saved.FirstOrDefault(p => p == ssid);
        if (password != null || open || profile == null)
        {
            if (!open && string.IsNullOrEmpty(password)) return new { ok = false, error = "Falta la contraseña", needPassword = true };
            int r = WlanSetProfile(s.H, ref s.Iface, 0, ProfileXml(ssid, password, sae, open), null, true, IntPtr.Zero, out uint reason);
            if (r != 0) { Log.Write($"wifi: Windows no aceptó el perfil de «{ssid}» (código {r}, motivo {reason})"); return new { ok = false, error = "Windows no aceptó esa red" }; }
            created = profile == null; profile = ssid;
        }
        var cp = new ConnParams { Mode = 0, Profile = profile, BssType = 1 };
        int c = WlanConnect(s.H, ref s.Iface, ref cp, IntPtr.Zero);
        Log.Write($"wifi: conectando a «{ssid}»{(created ? " (red nueva)" : "")}");
        if (c != 0) return new { ok = false, error = $"Windows no pudo conectarse (código {c})" };
        for (int i = 0; i < 50; i++)
        {
            await Task.Delay(500);
            var cur = Current(s, new());
            if (cur != null && (string)cur["ssid"] == ssid)
            {
                // con dirección IP ya está lista
                if (!string.IsNullOrEmpty((string)cur["ip"]) && !((string)cur["ip"]).StartsWith("169.254.")) { Log.Write($"wifi: conectado a «{ssid}»"); return new { ok = true, current = cur }; }
            }
        }
        if (created) WlanDeleteProfile(s.H, ref s.Iface, ssid, IntPtr.Zero);
        Log.Write($"wifi: no se pudo conectar a «{ssid}»");
        return new { ok = false, error = password != null ? "No se pudo conectar. Revisa la contraseña." : "No se pudo conectar a esa red" };
    });

    public static Task<object> ForgetAsync(string name) => Task.Run<object>(() =>
    {
        using var s = new Session();
        if (s.Error != 0) return new { ok = false, error = "No encontré el Wi-Fi" };
        int r = WlanDeleteProfile(s.H, ref s.Iface, name, IntPtr.Zero);
        Log.Write($"wifi: red «{name}» olvidada{(r == 0 ? "" : $" (código {r})")}");
        return new { ok = r == 0, error = r == 0 ? null : "Windows no la pudo olvidar" };
    });

    public static Task<object> DisconnectAsync() => Task.Run<object>(() =>
    {
        using var s = new Session();
        if (s.Error != 0) return new { ok = false };
        return new { ok = WlanDisconnect(s.H, ref s.Iface, IntPtr.Zero) == 0 };
    });

    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(6) };

    /// <summary>Prueba de conexión: ¿sale a internet? (la prueba de Windows) y ¿llega a Steam?, con el tiempo de respuesta.</summary>
    public static async Task<object> TestAsync()
    {
        var sw = Stopwatch.StartNew();
        bool net = false, portal = false; long ms = 0;
        try
        {
            string body = await http.GetStringAsync("http://www.msftconnecttest.com/connecttest.txt");
            ms = sw.ElapsedMilliseconds;
            net = body.Trim() == "Microsoft Connect Test";
            portal = !net;   // contestó otra cosa: una página de inicio de sesión (hotel, aeropuerto)
        }
        catch { }
        bool steam = false;
        try { using var r = await http.GetAsync("https://store.steampowered.com/favicon.ico"); steam = r.IsSuccessStatusCode; } catch { }
        Log.Write($"wifi: prueba de conexión: internet {(net ? "sí" : portal ? "pide iniciar sesión" : "no")}, Steam {(steam ? "sí" : "no")}, {ms} ms");
        return new { ok = net, portal, steam, ms };
    }
}
