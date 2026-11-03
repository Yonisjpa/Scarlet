using Microsoft.Web.WebView2.Core;

namespace XboxScarlet;

/// <summary>Mensajes de la interfaz · La interfaz: menú rápido, avisos encima del juego, álbum, sonidos, cuentas, actualizaciones y la app.</summary>
sealed partial class MainForm
{
    async Task<object> UiMessage(string type, JsonElement p, CoreWebView2 from)
    {
        switch (type)
        {
            // ── Álbum
            case "album.list": album.Watch(); return await Task.Run(() => album.List());   // por si apareció una carpeta nueva (Steam ya dio tu cuenta)
            case "album.delete": return album.Delete(P.S(p, "path"));
            case "album.trim": return await album.TrimAsync(P.S(p, "path"), P.D(p, "start"), P.D(p, "end"));
            case "album.share":
            {
                var list = p.TryGetProperty("paths", out var arr) && arr.ValueKind == JsonValueKind.Array
                    ? arr.EnumerateArray().Select(x => x.GetString()).Where(album.IsAlbumFile).ToList() : new List<string>();
                var r = share.Start(list);
                _ = BringFirewallPromptAsync();
                return r;
            }
            case "album.shareStop": share.Stop(); return true;
            case "album.copy": return CopyToClipboard(P.S(p, "path"));
            case "app.open": await OpenAlongsideAsync(P.L(p, "appid"), P.S(p, "source")); return true;
            case "app.icon": return await AppIconAsync(P.L(p, "appid"));
            case "app.info": return new { startup, updated, rolledBack = UpdateService.TakeRolledBackNotice(), version = Application.ProductVersion, dolby = DolbyInstalled() };
            case "update.check": return await UpdateService.CheckAsync();
            case "update.install":
                return await UpdateService.InstallAsync(pr => Emit("update.progress", new { p = pr }), () => BeginInvoke(new Action(() => { Log.Write("cerrando para actualizar"); Close(); })));
            case "quick.glass":
                cfg.QuickGlass = P.B(p, "on"); cfg.Save();
                quick?.SetGlass(cfg.QuickGlass);
                return true;
            case "quick.tint":
            {
                string[] cols = p.TryGetProperty("colors", out var ca) && ca.ValueKind == JsonValueKind.Array ? ca.EnumerateArray().Select(x => x.GetString()).ToArray() : null;
                quick?.SetTint(cols, P.B(p, "light"));
                return true;
            }
            // ── Ventana y notificaciones
            case "ui.show": ShowUi(); return true;
            case "quick.close": quick?.HideNow(true, P.S(p, "why")); return true;
            case "quick.shown": quick?.Ack(); return true;
            case "quick.action":
            {
                // Botones del menú rápido encima del juego que necesitan la pantalla principal.
                quick?.HideNow(false, "botón " + P.S(p, "action"));
                string action = P.S(p, "action");
                if (action == "settings") { BringUiToFront(); Emit("ui.settings", null); }
                else if (action == "sleep") { BringUiToFront(); Emit("ui.sleep", null); }
                else if (action == "hibernate") { BringUiToFront(); Emit("ui.hibernate", null); }
                return true;
            }
            case "overlay.notify":
                ToastForm.Pop(P.S(p, "title"), P.S(p, "text"), P.S(p, "letter"), P.S(p, "color"), P.S(p, "image"),
                    wide: P.B(p, "wide"), video: P.B(p, "video"), dur: P.S(p, "dur"), saving: P.B(p, "saving"));
                return true;
            // Menú rápido en vivo: cada dato por separado (si uno falla, los demás sí llegan); se pide cada 2 s.
            // Al abrir (full) se vuelve a leer todo desde cero; cada dato tiene un tiempo límite y, si no llega, la interfaz
            // se queda con lo último que sabía (antes una lectura atorada dejaba el menú sin barras ni Salida).
            case "quick.live":
            {
                bool full = P.B(p, "full");
                IntPtr hint = from == quick?.View ? quick.ReturnTo : lastForeign;
                var r = new Dictionary<string, object>();
                var mix = Timed("mezclador", () => MixerState(hint), 1500);
                var atm = Timed("Dolby", () => AtmosActive() && AudioMixer.Sessions().Any(x => x.active && !NotApps.Contains(x.exe)), 1500);
                var bat = full ? Timed("batería", BatteryInfo, 2500) : null;
                var med = full ? Timed("música", () => media.State, 800) : null;
                var stt = full ? sys.StateAsync() : null;
                var ssid = full ? Timed("wifi", ShareServer.WifiName, 1500) : null;
                // Siempre (cada 2 s): volumen y brillo reales, por si los cambias con los botones de la consola con el menú abierto.
                var vol = !full ? Timed("volumen", AudioMixer.GetMaster, 800) : null;
                var bri = !full ? Timed("brillo", SystemService.GetBrightness, 1200) : null;
                if (await mix is (true, var mv) && mv != null) r["mixer"] = mv;
                if (await atm is (true, var av)) r["atmos"] = av;
                if (bat != null && await bat is (true, var bv)) r["battery"] = bv;
                if (med != null && await med is (true, var mdv)) r["media"] = mdv;
                if (ssid != null && await ssid is (true, var sn)) r["wifiName"] = sn;
                if (stt != null) try { if (await Task.WhenAny(stt, Task.Delay(1500)) == stt) r["system"] = await stt; } catch (Exception ex) { Log.Error("menú (sistema)", ex); }
                if (vol != null)
                {
                    var live = new Dictionary<string, object>();
                    if (await vol is (true, int vv)) live["volume"] = vv;
                    if (await bri is (true, int briLive)) live["brightness"] = briLive;
                    if (live.Count > 0) r["system"] = live;
                }
                return r;
            }
            // ── App
            case "app.setup": OpenSetup(); return true;
            case "setup.get": return SetupState();
            case "setup.set": return SetupSet(P.S(p, "field"), p);
            case "sounds.list": return SoundsList();
            case "sounds.info": return new { pack = SoundPack.Current() };
            case "sounds.import":
            {
                var r = await Task.Run(SoundPack.Import);
                if (r.ok) EmitAll("sounds.changed", SoundsList());
                return r;
            }
            case "sounds.reset": SoundPack.Remove(); EmitAll("sounds.changed", SoundsList()); return true;
            case "app.reload": steam.Reset(); web.CoreWebView2?.Reload(); return true;
            case "app.exit": BeginInvoke(new Action(Close)); return true;
            default: throw new NotSupportedException("Mensaje desconocido: " + type);
        }
    }
}
