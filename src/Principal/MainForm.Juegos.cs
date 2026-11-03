using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace XboxScarlet;

/// <summary>Abrir, adoptar y seguir juegos (pantalla de carga, listo, cerrado).</summary>
sealed partial class MainForm
{
    // ───────────────────────────── Juegos ─────────────────────────────

    async Task LaunchAsync(long appid, string source)
    {
        // Si ese mismo juego ya está abierto, se reanuda: nunca se cierra para volver a lanzarlo.
        // Todavía abriendo (por ejemplo, esperando la nube): no se da por listo; la pantalla de carga sigue esperando
        // el aviso real (en la 1.2 decía «se reanuda» antes de que el juego existiera).
        if (tracker.Active && tracker.AppId == appid && !tracker.IsReady)
        {
            Log.Write($"{appid} todavía está abriendo: se sigue esperando");
            TopMost = true;
            return;
        }
        if (tracker.Active && tracker.AppId == appid)
        {
            Log.Write($"{appid} ya está abierto: se reanuda");
            FocusGame();
            Emit("game.ready", new { appid });
            return;
        }
        if (!tracker.Active && TryAdoptRunning(appid, source)) { FocusGame(); Emit("game.ready", new { appid }); return; }
        if (tracker.Active) { long was = tracker.AppId; GameFreezer.Resume(); FlushPlay(); tracker.Close(); WatchCloud(was); }
        playKey = null;
        AudioFocus.RestoreAll();
        TopMost = true;   // la pantalla de carga tapa las ventanitas que abren algunos juegos al arrancar
        ShowUi();
        if (source == "app")
        {
            var a = apps.FirstOrDefault(x => x.AppId == appid) ?? throw new InvalidOperationException("No encontré esa app en apps.json");
            cfg.AppLastUsed[a.Id ?? a.Name ?? ""] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            cfg.Save();
            playKey = a.Id ?? a.Name ?? "";
            Native.AllowAnyForeground();
            a.Start();
            BeginGame(appid, false);
        }
        else if (source == "ext")
        {
            var g = cfg.ExternalGames.FirstOrDefault(x => x.Id == appid) ?? throw new InvalidOperationException("No encontré ese juego externo");
            cfg.AppLastUsed[ExternalGames.Key(g)] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            cfg.Save();
            playKey = ExternalGames.Key(g);
            Native.AllowAnyForeground();
            ExternalGames.Launch(g);
            tracker.Begin(appid, false, ExternalGames.DirOf(g), slowStart: true);
        }
        else if (source == "xbox")
        {
            var g = xbox.Find(appid) ?? throw new InvalidOperationException("No encontré ese juego de Xbox");
            cfg.AppLastUsed["xbox:" + g.Pfn] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            cfg.Save();
            playKey = "xbox:" + g.Pfn;
            Native.AllowAnyForeground();
            xbox.Launch(g);
            // ZZZ y otros juegos de la Xbox Store corren desde XboxGames, no desde la carpeta del paquete: se detecta por su ventana.
            tracker.Begin(appid, false, null, slowStart: true, owner: g.Dir, xboxGame: !g.IsApp);
        }
        else
        {
            await CloseBigPictureAsync();   // con Big Picture abierto, Steam lo pone al frente al lanzar el juego
            Shell.Open("steam://rungameid/" + appid);
            BeginGame(appid, true);
        }
    }

    /// <summary>¿El juego ya corre aunque Scarlet no lo esté siguiendo (lo perdió, o se abrió por fuera)? Se toma y se reanuda.</summary>
    bool TryAdoptRunning(long appid, string source)
    {
        try
        {
            string dir = source == "xbox" ? xbox.Find(appid)?.Dir : source == "app" ? null
                : source == "ext" ? (cfg.ExternalGames.FirstOrDefault(x => x.Id == appid) is ExtGame eg ? ExternalGames.DirOf(eg) : null) : steam.InstallDirOf(appid);
            IntPtr w = GameTracker.RunningWindowIn(dir);
            if (w == IntPtr.Zero && source != "xbox" && source != "app" && steam.RunningAppId() == appid && cfg.GameExes.TryGetValue(appid.ToString(), out var exe))
                w = GameTracker.RunningWindowIn(Path.GetDirectoryName(exe));   // juegos que corren desde otra carpeta
            bool st = source != "xbox" && source != "app" && source != "ext";
            if (w == IntPtr.Zero || !tracker.AdoptRunning(appid, st, dir, w)) return false;
            if (st) steam.SetRunning(appid);
            return true;
        }
        catch (Exception ex) { Log.Error("juego ya abierto", ex); return false; }
    }

    void BeginGame(long appid, bool isSteam)
    {
        tracker.Begin(appid, isSteam, isSteam ? steam.InstallDirOf(appid) : null);
        if (isSteam) steam.SetRunning(appid);
    }

    async void OnGameReady(long appid)
    {
        if (playKey != null) playStart = DateTime.UtcNow;
        try
        {
            IntPtr w = tracker.Window;
            string exe = w != IntPtr.Zero ? Native.WindowProcessPath(w, out _) : null;
            if (!string.IsNullOrEmpty(exe) && (!cfg.GameExes.TryGetValue(appid.ToString(), out var old) || old != exe)) { cfg.GameExes[appid.ToString()] = exe; cfg.Save(); }
        }
        catch { }
        Emit("game.ready", new { appid });
        await Task.Delay(900);   // la interfaz desvanece la pantalla de carga
        TopMost = false;
        if (!tracker.Focus()) Native.SendToBottom(Handle);
        HideSoon();
        // El juego suele cambiar la pantalla al arrancar: el menú se prepara de nuevo para que no salga en blanco.
        quick?.Refresh("el juego ya está en pantalla");
        if (quickPending)
        {
            quickPending = false;
            await Task.Delay(1500);   // que termine de recargarse
            if (tracker.Active && quick != null && !quick.IsOpen)
            {
                var fg = Native.GetForegroundWindow();
                bool keep = cfg.MenuKeepsFocus.Contains(tracker.AppId);
                Log.Write("el juego ya cargó: se abre el menú que pediste");
                if (quick.ShowOver(fg, keep)) { xin.ForwardKeys = true; if (!keep) WarnExclusive(); }
            }
        }
    }

    void OnGameExited(long appid)
    {
        quickPending = false;
        GameFreezer.Forget();
        FlushPlay(); playKey = null;
        steam.SetRunning(0);
        TopMost = false;
        ShowUi();
        Emit("game.exited", new { appid });
        WatchCloud(appid);   // ahora sube su partida a la nube
        if (reconnectPending) _ = Task.Delay(3000).ContinueWith(_ => ReconnectSteamAsync("ya se cerró el juego", true));
    }
}
