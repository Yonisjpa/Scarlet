using Microsoft.Web.WebView2.Core;

namespace XboxScarlet;

/// <summary>Mensajes de la interfaz · Windows y la consola: brillo, volumen, energía, audio, música, batería, mandos y Bluetooth.</summary>
sealed partial class MainForm
{
    async Task<object> SystemMessage(string type, JsonElement p, CoreWebView2 from)
    {
        switch (type)
        {
            case "capture.take": return false;   // en la app real las capturas vienen de la Game Bar / Steam
            case "media.state": return media.State;
            case "media.cmd": return await media.CommandAsync(P.S(p, "cmd"), P.D(p, "value"));
            case "cursor.hide":
                cfg.HideCursor = P.B(p, "on"); cfg.Save();
                if (cfg.HideCursor) CursorHider.Hide(); else CursorHider.Restore();
                return true;
            case "audio.mutebehind":
                cfg.MuteBehind = P.B(p, "on"); cfg.Save();
                if (!cfg.MuteBehind) AudioFocus.RestoreAll();
                return true;
            case "rumble": xin.Rumble(P.D(p, "strong"), P.D(p, "weak"), (int)P.L(p, "ms")); return true;
            // ── Sistema
            case "system.sleep":
                _ = Task.Delay(500).ContinueWith(_ => Application.SetSuspendState(PowerState.Suspend, false, false));
                return true;
            case "system.hibernate":
                _ = Task.Delay(600).ContinueWith(_ =>
                {
                    // Si la hibernación está desactivada en Windows, se suspende normal.
                    if (!Application.SetSuspendState(PowerState.Hibernate, false, false))
                    {
                        Log.Write("hibernar no está disponible; se suspende");
                        Application.SetSuspendState(PowerState.Suspend, false, false);
                    }
                });
                return true;
            case "system.restart":
                Log.Write("reiniciando el equipo");
                Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false });
                return true;
            // 1.8: Ajustes › Pantalla
            case "display.state": return await Task.Run(() => DisplayService.State(cfg));
            case "display.set": return await Task.Run(() => DisplayService.Set((int)P.L(p, "w"), (int)P.L(p, "h"), (int)P.L(p, "hz")));
            case "display.keep": return DisplayService.Keep();
            case "display.revert": return DisplayService.Revert();
            case "display.hdr": return await Task.Run(() => DisplayService.SetHdr(cfg, P.S(p, "mode")));
            case "system.autobright": return await Task.Run(() => SystemService.SetAutoBright(P.B(p, "on")));
            case "system.shutdown":
                Log.Write("apagando el equipo");
                Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });
                return true;
            case "system.open": await OpenSystemAsync(P.S(p, "uri")); return true;
            case "system.armoury": return await sys.OpenArmouryAsync();
            case "system.state":
            {
                return await sys.StateAsync();
            }
            case "audio.atmos": return await Task.Run(AtmosActive);
            case "audio.outputs": return await Task.Run(() => AudioMixer.Outputs());
            case "battery.info": return await Task.Run(BatteryInfo);
            case "diag": return await Task.Run(Diagnostics);
            case "audio.reset": return await Task.Run(() => { int n = AudioMixer.ResetAll(); AudioFocus.ForgetMuted(); return n; });
            case "mixer.set": { string exe = P.S(p, "exe"), dir = AppDirOf(exe); AudioMixer.Queue("app:" + exe, (int)P.L(p, "value"), v => AudioMixer.SetVolume(exe, dir, v)); return true; }
            case "audio.output": return await Task.Run(() => AudioMixer.SetOutput(P.S(p, "id")));
            case "system.brightness": AudioMixer.Queue("brillo", (int)P.L(p, "value"), SystemService.SetBrightness); return true;   // solo el último valor
            case "system.volume": AudioMixer.Queue("sistema", (int)P.L(p, "value"), AudioMixer.SetMaster); return true;   // todas las salidas que suenan; solo el último valor
            case "system.power": SystemService.SetPower(P.S(p, "profile")); return true;
            case "system.radio": return await sys.SetRadiosAsync(P.B(p, "wifi"), P.B(p, "airplane"));
            case "storage.info": return SystemService.Storage(steam.SteamPath);
            // ── Mandos y Bluetooth
            case "pads.list": return pads.List();
            case "pad.player":
                cfg.PadOrder[P.S(p, "key") ?? ""] = (int)P.L(p, "player");
                cfg.Save();
                return true;
            case "pad.rumble":
            {
                string key = P.S(p, "key");
                if (!pads.Rumble(key)) xin.Rumble(.7, .7, 600);
                return true;
            }
            case "pad.disconnect": return await pads.UnpairAsync(P.S(p, "key"));
            case "bt.scan": pads.Scan(); return true;
            case "bt.pair": return await pads.PairAsync(P.S(p, "id"));
            case "clipboard.get": return Clipboard.ContainsText() ? Clipboard.GetText() : "";
            default: throw new NotSupportedException("Mensaje desconocido: " + type);
        }
    }
}
