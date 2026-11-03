using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace XboxScarlet;

/// <summary>Sistema: energía, pantallas y cuentas desde la interfaz.</summary>
sealed partial class MainForm
{
    // ───────────────────────────── Sistema ─────────────────────────────

    void OnPower(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) { Ui(FlushPlay); return; }   // el rato dormida no cuenta como juego
        if (e.Mode != PowerModes.Resume) return;
        Ui(() =>
        {
            if (playKey != null && tracker != null && tracker.IsReady && !GameFreezer.IsFrozen) playStart = DateTime.UtcNow;
            if (cfg.HideCursor) CursorHider.Hide(force: true);   // al despertar, Windows regresa los cursores
            TopMost = false;
            ShowUi();
            Emit("system.resumed", null);
        });
    }

    void OnDisplay(object sender, EventArgs e)
    {
        Ui(() =>
        {
            Bounds = Screen.PrimaryScreen.Bounds;
            if (cfg.HideCursor) CursorHider.Hide(force: true);
            quick?.Refresh("cambió la pantalla");
            bool tv = Screen.AllScreens.Length > 1 || Screen.PrimaryScreen.DeviceName != cfg.InternalDisplay;
            Emit("display.changed", new { tv });
        });
    }

    // ───── Cuentas y arranque desde la interfaz (la ventana de Windows se congelaba detrás de la pantalla completa) ─────

    object SetupState() => new
    {
        steamId = cfg.SteamId,
        steamKey = !string.IsNullOrEmpty(cfg.SteamKey),
        sgdbKey = !string.IsNullOrEmpty(cfg.SgdbKey),
        autostart = Startup.IsAutostart(),
        gamebar = Startup.IsGameBarButtonOff()
    };

    object SetupSet(string field, JsonElement p)
    {
        string text = (P.S(p, "value") ?? "").Trim();
        switch (field)
        {
            case "steamId":
                if (!Regex.IsMatch(text, @"^7656119\d{10}$")) throw new InvalidOperationException("El SteamID64 son 17 dígitos y empieza con 7656119.");
                cfg.SteamId = text;
                break;
            case "steamKey":
                text = text.ToUpperInvariant();
                if (!Regex.IsMatch(text, "^[0-9A-F]{32}$")) throw new InvalidOperationException("La API key de Steam son 32 caracteres (0-9 y A-F).");
                cfg.SteamKey = text;
                break;
            case "sgdbKey":
                if (!Regex.IsMatch(text, "^[0-9A-Za-z]{20,64}$")) throw new InvalidOperationException("Esa API key de SteamGridDB no parece válida.");
                cfg.SgdbKey = text;
                break;
            case "autostart":
                Startup.SetAutostart(P.B(p, "value"));
                break;
            case "gamebar":
                bool on = P.B(p, "value");
                if (on && !Startup.IsGameBarButtonOff()) Startup.SetGameBarButtonOff(true, cfg);
                else if (!on && cfg.GameBarNexusBackup != null) Startup.SetGameBarButtonOff(false, cfg);
                break;
            default: throw new NotSupportedException("Ajuste desconocido: " + field);
        }
        cfg.SetupDone = true;
        cfg.Save();
        Log.Write("ajuste guardado: " + field);
        return SetupState();
    }

    void OpenSetup()
    {
        BeginInvoke(new Action(() =>
        {
            TopMost = false;
            using var f = new SetupForm(cfg);
            if (f.ShowDialog(this) == DialogResult.OK)
            {
                steam.Reset();
                web.CoreWebView2?.Reload();
            }
            ShowUi();
        }));
    }
}
