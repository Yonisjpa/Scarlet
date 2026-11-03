using Microsoft.Web.WebView2.Core;

namespace XboxScarlet;

/// <summary>Mensajes de la interfaz · Red: Wi-Fi, VPN, estado de internet y páginas web.</summary>
sealed partial class MainForm
{
    async Task<object> NetMessage(string type, JsonElement p, CoreWebView2 from)
    {
        switch (type)
        {
            // ── Red y VPN
            case "net.state": return new { internet = NetCheck.Internet, steam = NetCheck.SteamReachable };
            case "vpn.state": return await Vpn.StatusAsync(cfg);
            // Wi-Fi sin salir de Scarlet (beta 1.2)
            case "wifi.list": return await Wifi.ListAsync(P.B(p, "scan"));
            case "wifi.connect":
            {
                string pw = P.S(p, "password");
                var r = await Wifi.ConnectAsync(P.S(p, "ssid"), pw, P.B(p, "sae"), P.B(p, "open"));
                _ = NetCheck.CheckAsync();
                return r;
            }
            case "wifi.forget": return await Wifi.ForgetAsync(P.S(p, "name"));
            case "wifi.disconnect": return await Wifi.DisconnectAsync();
            case "wifi.test": return await Wifi.TestAsync();
            case "vpn.set":
            {
                bool on = P.B(p, "on");
                var r = await Vpn.SetAsync(cfg, on);
                // Steam que arrancó sin conexión no se reconecta solo: con la VPN prendida se reinicia desde cero.
                if (on && r.TryGetValue("status", out var stv) && (string)stv == "on") _ = ReconnectSteamAsync("se prendió la VPN", true);
                return r;
            }
            case "vpn.setup": { var (ok, error) = await Vpn.SetupWireGuardAsync(); ShowUi(); return new { ok, error }; }
            case "vpn.import":
            {
                var (ok, name, error) = await Task.Run(Vpn.ImportWireGuard);
                if (ok) { cfg.VpnProfileName = name; cfg.VpnProvider = "wireguard"; cfg.Save(); }
                return new { ok, name, error };
            }
            case "vpn.remove":
            {
                if (cfg.VpnProvider == "wireguard" || Vpn.WarpCli == null) await Vpn.SetAsync(cfg, false);
                Vpn.RemoveWireGuard(); cfg.VpnProfileName = null; if (cfg.VpnProvider == "wireguard") cfg.VpnProvider = ""; cfg.Save();
                return await Vpn.StatusAsync(cfg);
            }
            case "vpn.provider": { string pv = P.S(p, "provider"); if (pv is "warp" or "wireguard") { cfg.VpnProvider = pv; cfg.Save(); } return await Vpn.StatusAsync(cfg); }
            case "web.open":   // páginas de descarga (WARP, WireGuard) en el navegador de Windows
            {
                string url = P.S(p, "url");
                if (url != null && Regex.IsMatch(url, @"^https://(one\.one\.one\.one|1\.1\.1\.1|www\.wireguard\.com|protonvpn\.com|account\.protonvpn\.com)(/|$)")) { TopMost = false; Shell.Open(url); return true; }
                return false;
            }
            default: throw new NotSupportedException("Mensaje desconocido: " + type);
        }
    }
}
