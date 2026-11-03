using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace XboxScarlet;

/// <summary>Rendimiento mientras juegas: modo eficiencia, memoria y muestras para Diagnóstico.</summary>
sealed partial class MainForm
{
    // ───── Rendimiento mientras juegas (beta 1.2) ─────
    bool wasGaming;
    DateTime menuToggledAt;

    /// <summary>Jugando = un juego ya en pantalla (no mientras abre) y la interfaz de Scarlet escondida.</summary>
    bool Gaming => tracker != null && tracker.Active && tracker.IsReady && !web.Visible;

    /// <summary>Scarlet y todos los procesos de sus vistas web (la interfaz y el menú encima del juego).</summary>
    List<int> ScarletPids()
    {
        var l = new List<int> { Environment.ProcessId };
        try { if (env != null) foreach (var pi in env.GetProcessInfos()) l.Add(pi.ProcessId); } catch { }
        return l;
    }

    /// <summary>Jugando, la interfaz escondida usa la menor memoria posible; al volver, lo normal (beta 1.3).</summary>
    void MainMemory(bool normal)
    {
        try { if (web.CoreWebView2 != null) web.CoreWebView2.MemoryUsageTargetLevel = normal ? CoreWebView2MemoryUsageTargetLevel.Normal : CoreWebView2MemoryUsageTargetLevel.Low; }
        catch { }
    }

    void PerfTick()
    {
        bool gaming = Gaming, menu = quick != null && quick.IsOpen;
        xin.Gaming = gaming;
        if (gaming != wasGaming)
        {
            wasGaming = gaming;
            if (gaming) Perf.Reset(); else if (Perf.EcoOn) Perf.Eco(false, ScarletPids(), "ya no hay juego al frente");
            MainMemory(!gaming);
            if (!gaming) Log.Write("rendimiento del último juego: " + Perf.Describe());
        }
        if (!gaming) return;
        var pids = ScarletPids();
        // La muestra que incluye el abrir o cerrar del menú (su animación) se mide pero no se suma: en la 1.2 esos picos
        // de 5–7 % salían como «menú cerrado».
        Perf.Sample(pids, menu, DateTime.Now - menuToggledAt > TimeSpan.FromSeconds(slow.Interval / 1000.0 + 1));
        if (cfg.EcoWhilePlaying && !menu && !Perf.EcoOn) Perf.Eco(true, pids, "juego al frente, menú cerrado");
    }
}
