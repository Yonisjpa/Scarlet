namespace XboxScarlet;

/// <summary>Cambios reversibles para que la consola arranque en Scarlet. Nada toca archivos del sistema.</summary>
static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string GameBarKey = @"Software\Microsoft\GameBar";
    const string Name = "Scarlet", OldName = "XboxScarlet";

    public static bool IsAutostart()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(Name) != null;
    }

    /// <summary>Si estaba activado con el nombre anterior (Xbox Scarlet), se pasa al nuevo apuntando a Scarlet.exe.</summary>
    public static void MigrateAutostart()
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (k.GetValue(OldName) == null) return;
        k.DeleteValue(OldName, false);
        k.SetValue(Name, $"\"{Environment.ProcessPath}\" --startup");
    }

    public static void SetAutostart(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(Name, $"\"{Environment.ProcessPath}\" --startup");
        else k.DeleteValue(Name, false);
    }

    // Dos interruptores de Windows, los mismos de Configuración › Juegos:
    //  · UseNexusForGameBarEnabled = 0          → en el escritorio, el botón Xbox no abre la Game Bar.
    //  · LegacyGamepadXboxGuideButtonEnabled = 1 → en el modo Xbox (pantalla completa), tampoco.
    const string Nexus = "UseNexusForGameBarEnabled", Legacy = "LegacyGamepadXboxGuideButtonEnabled";

    /// <summary>¿La Game Bar ya no se abre con el botón Xbox?</summary>
    public static bool IsGameBarButtonOff()
    {
        using var k = Registry.CurrentUser.OpenSubKey(GameBarKey);
        return k?.GetValue(Nexus) is int n && n == 0 && k.GetValue(Legacy) is int l && l == 1;
    }

    public static void SetGameBarButtonOff(bool off, Config cfg)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(GameBarKey))
        {
            if (off)
            {
                cfg.GameBarNexusBackup ??= k.GetValue(Nexus) is int v ? v : 1;
                cfg.GameBarLegacyBackup ??= k.GetValue(Legacy) is int w ? w : 0;
                k.SetValue(Nexus, 0, RegistryValueKind.DWord);
                k.SetValue(Legacy, 1, RegistryValueKind.DWord);
            }
            else
            {
                k.SetValue(Nexus, cfg.GameBarNexusBackup ?? 1, RegistryValueKind.DWord);
                k.SetValue(Legacy, cfg.GameBarLegacyBackup ?? 0, RegistryValueKind.DWord);
                cfg.GameBarNexusBackup = null;
                cfg.GameBarLegacyBackup = null;
            }
        }
        Log.Write("game bar: botón Xbox " + (off ? "liberado" : "devuelto a la Game Bar"));
        RestartGameBar();
    }

    /// <summary>La Game Bar lee esos valores al arrancar; se cierra y Windows la vuelve a abrir cuando hace falta.</summary>
    public static void RestartGameBar()
    {
        foreach (var name in new[] { "GameBar", "GameBarFTServer" })
            foreach (var p in Process.GetProcessesByName(name))
                try { p.Kill(); } catch { } finally { p.Dispose(); }
    }
}

/// <summary>Primera vez y "Cuentas y arranque": claves de Steam / SteamGridDB y cómo arranca la consola.</summary>
sealed class SetupForm : Form
{
    readonly Config cfg;
    readonly TextBox steamId, steamKey, sgdbKey;
    readonly CheckBox autostart, gamebar;
    static readonly Color Bg = Color.FromArgb(43, 43, 47), Field = Color.FromArgb(56, 56, 61), Accent = Color.FromArgb(58, 215, 255), Muted = Color.FromArgb(169, 169, 176);

    public SetupForm(Config cfg)
    {
        this.cfg = cfg;
        Text = "Scarlet · Cuentas y arranque";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        BackColor = Bg; ForeColor = Color.White;
        Font = new Font("Segoe UI", 11f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, 720);
        TopMost = true;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath); } catch { }

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(28, 22, 28, 12) };
        Controls.Add(flow);

        flow.Controls.Add(Heading("Tu cuenta de Steam"));
        flow.Controls.Add(Note("El SteamID se detecta solo. Con la API key de Steam la consola ve tus juegos, amigos y logros; tu perfil y \"Detalles de juego\" deben estar en Público. Las claves se guardan cifradas con tu usuario de Windows."));
        steamId = AddField(flow, "SteamID64", cfg.SteamId, false);
        steamKey = AddField(flow, "API key de Steam", cfg.SteamKey, true);
        flow.Controls.Add(LinkTo("Obtener mi API key de Steam (escribe cualquier dominio, p. ej. localhost)", "https://steamcommunity.com/dev/apikey"));
        sgdbKey = AddField(flow, "API key de SteamGridDB (opcional: portadas más bonitas)", cfg.SgdbKey, true);
        flow.Controls.Add(LinkTo("Obtener mi API key de SteamGridDB", "https://www.steamgriddb.com/profile/preferences/api"));

        flow.Controls.Add(Heading("Preparar la consola (todo se puede deshacer aquí mismo)"));
        autostart = AddCheck(flow, "Abrir Scarlet al iniciar sesión en Windows", Startup.IsAutostart());
        gamebar = AddCheck(flow, "Quitarle el botón Xbox a la Game Bar, también en modo Xbox (para el doble toque y el menú rápido)", Startup.IsGameBarButtonOff() || cfg.GameBarNexusBackup != null);
        flow.Controls.Add(Note("Además, una sola vez en Steam › Configuración: en Control desactiva \"El botón Guía activa Steam\", y en Interfaz desactiva \"Iniciar Steam en modo Big Picture\". Big Picture se abre solo para la tienda y se cierra al lanzar un juego."));
        flow.Controls.Add(Heading("Que reemplace a la app de Xbox"));
        flow.Controls.Add(Note("Windows solo deja elegir como \"app de inicio\" del modo Xbox a apps registradas para eso. La forma sencilla es AnyFSE (gratis, en Microsoft Store): en AnyFSE eliges \"Personalizada\" y buscas Scarlet.exe; luego en Configuración › Juegos › Experiencia de pantalla completa eliges AnyFSE como app de inicio. Así la consola arranca directo en Scarlet, sin la app de Xbox ni el escritorio detrás."));
        flow.Controls.Add(LinkTo("Abrir AnyFSE en Microsoft Store", "ms-windows-store://pdp/?ProductId=XPFM6KZ5SD704G"));
        flow.Controls.Add(LinkTo("Abrir Configuración › Juegos", "ms-settings:gaming"));

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Width = 690, Height = 64, Margin = new Padding(0, 16, 0, 0) };
        var save = MakeButton("Guardar", true);
        var cancel = MakeButton("Cancelar", false);
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        flow.Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;
    }

    static Label Heading(string t) => new() { Text = t, AutoSize = true, Font = new Font("Segoe UI Semibold", 14f), Margin = new Padding(0, 14, 0, 6) };

    static Label Note(string t) => new() { Text = t, AutoSize = true, MaximumSize = new Size(690, 0), ForeColor = Muted, Margin = new Padding(0, 0, 0, 8) };

    static TextBox AddField(FlowLayoutPanel flow, string label, string value, bool secret)
    {
        flow.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 0, 2) });
        var tb = new TextBox { Text = value ?? "", Width = 690, BackColor = Field, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = secret, Font = new Font("Segoe UI", 13f) };
        flow.Controls.Add(tb);
        return tb;
    }

    static CheckBox AddCheck(FlowLayoutPanel flow, string label, bool value)
    {
        var cb = new CheckBox { Text = label, Checked = value, AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
        flow.Controls.Add(cb);
        return cb;
    }

    static LinkLabel LinkTo(string text, string url)
    {
        var l = new LinkLabel { Text = text, AutoSize = true, LinkColor = Accent, ActiveLinkColor = Color.White, Margin = new Padding(0, 2, 0, 6) };
        l.LinkClicked += (_, _) => Shell.Open(url);
        return l;
    }

    static Button MakeButton(string text, bool primary) => new()
    {
        Text = text, Width = 160, Height = 46, FlatStyle = FlatStyle.Flat, Margin = new Padding(10, 0, 0, 0),
        BackColor = primary ? Accent : Field, ForeColor = primary ? Color.FromArgb(6, 34, 44) : Color.White,
        Font = new Font("Segoe UI Semibold", 12f)
    };

    void Save()
    {
        string id = steamId.Text.Trim();
        if (id.Length > 0 && !Regex.IsMatch(id, @"^7656119\d{10}$"))
        {
            MessageBox.Show(this, "El SteamID64 son 17 dígitos y empieza con 7656119.", "Scarlet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        cfg.SteamId = id;
        cfg.SteamKey = steamKey.Text;
        cfg.SgdbKey = sgdbKey.Text;
        try { Startup.SetAutostart(autostart.Checked); } catch (Exception ex) { Log.Error("inicio automático", ex); }
        try
        {
            if (gamebar.Checked && !Startup.IsGameBarButtonOff()) Startup.SetGameBarButtonOff(true, cfg);
            else if (!gamebar.Checked && cfg.GameBarNexusBackup != null) Startup.SetGameBarButtonOff(false, cfg);
        }
        catch (Exception ex) { Log.Error("game bar", ex); }
        cfg.SetupDone = true;
        cfg.Save();
        DialogResult = DialogResult.OK;
        Close();
    }
}
