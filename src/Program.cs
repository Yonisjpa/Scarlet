using Microsoft.Web.WebView2.Core;

namespace XboxScarlet;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "Scarlet.SingleInstance", out bool first);
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, "Scarlet.Show");
        if (!first && args.Contains("--restarted"))
        {
            // Reinicio tras un error: se espera a que la copia anterior termine de cerrarse.
            try { first = mutex.WaitOne(10000); } catch (AbandonedMutexException) { first = true; }
        }
        if (!first) { show.Set(); return; }   // ya está abierta: que se ponga al frente

        Log.Write($"--- Scarlet alpha {Application.ProductVersion} {string.Join(" ", args)}");
        // Modo seguro: si la versión nueva ya falló dos veces al abrir, se regresa a la anterior y esta copia no sigue.
        if (UpdateService.StartupCheck()) return;
        CursorHider.Restore();   // por si la vez anterior se cerró de golpe con el puntero escondido
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("ui", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Write("ERROR FATAL: " + e.ExceptionObject);
            // Versión recién actualizada que vuelve a tronar: se regresa a la anterior en vez de reiniciar la que falla.
            CursorHider.Restore();
            if (!UpdateService.RollbackNowIfFailing()) Restart("error fatal");
            // Sin esperar al reporte de errores de Windows (tardaba ~10 s): la copia nueva entra de inmediato.
            try { Process.GetCurrentProcess().Kill(); } catch { }
        };
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("tarea", e.Exception); e.SetObserved(); };
        ApplicationConfiguration.Initialize();

        try { CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show("Falta el componente WebView2 de Microsoft (viene con Windows 11). Se abrirá la página para instalarlo.", "Scarlet");
            Shell.Open("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
            return;
        }

        if (!Directory.Exists(Paths.Web))
        {
            MessageBox.Show("No encontré la carpeta \"web\" junto a Scarlet.exe. Copia la carpeta completa de la app.", "Scarlet");
            return;
        }

        var cfg = Config.Load();
        try { Startup.MigrateAutostart(); } catch (Exception ex) { Log.Error("inicio automático", ex); }
        // Si ya habías liberado el botón Xbox con una versión anterior, se completa el ajuste del modo Xbox.
        try { if (cfg.GameBarNexusBackup != null && !Startup.IsGameBarButtonOff()) { Startup.SetGameBarButtonOff(true, cfg); cfg.Save(); } }
        catch (Exception ex) { Log.Error("game bar", ex); }
        if (string.IsNullOrEmpty(cfg.SteamId)) cfg.SteamId = SteamService.DetectSteamId(SteamService.FindSteamPath()) ?? "";
        if (!cfg.SetupDone)
        {
            using var setup = new SetupForm(cfg);
            if (setup.ShowDialog() != DialogResult.OK) return;
        }

        var main = new MainForm(cfg, args.Contains("--startup"), args.Contains("--updated"));
        var listener = new Thread(() =>
        {
            try
            {
                while (show.WaitOne())
                {
                    if (main.IsDisposed) return;
                    main.ShowFromOtherInstance();
                }
            }
            catch { }   // la app se está cerrando
        }) { IsBackground = true, Name = "show-request" };
        listener.Start();
        Application.Run(main);
        AudioFocus.RestoreAllNow();   // al salir, nada se queda silenciado (ni Steam); se espera a que quede hecho
        GameFreezer.Resume();      // ni en pausa
        CursorHider.Restore();     // y el puntero vuelve
        AudioFocus.MuteSteam(false);
        Log.Write("Scarlet se cerró normalmente");
    }

    static int restarting;

    /// <summary>
    /// Abre una copia nueva antes de que esta termine, para que la consola nunca se quede sin menú
    /// (con AnyFSE, si la app se cierra, Windows saldría al escritorio). No reintenta si ya falló hace menos de un minuto.
    /// </summary>
    public static void Restart(string why)
    {
        if (Interlocked.Exchange(ref restarting, 1) == 1) return;
        try
        {
            string stamp = Path.Combine(Paths.Local, "last-restart.txt");
            if (File.Exists(stamp) && DateTime.Now - File.GetLastWriteTime(stamp) < TimeSpan.FromMinutes(1))
            {
                Log.Write("no se reinicia: ya falló hace menos de un minuto");
                return;
            }
            File.WriteAllText(stamp, why);
            Log.Write("reiniciando Scarlet (" + why + ")");
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--restarted") { UseShellExecute = false });
        }
        catch (Exception ex) { Log.Error("reiniciar", ex); }
    }
}
