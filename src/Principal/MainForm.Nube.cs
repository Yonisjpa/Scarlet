using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace XboxScarlet;

/// <summary>Nube de Steam: la subida de partidas se ve como una descarga naranja.</summary>
sealed partial class MainForm
{
    // ───── Nube de Steam: la subida de partidas se ve como una descarga (naranja) ─────
    readonly Dictionary<long, DateTime> cloudWatch = new();      // juegos a revisar y hasta cuándo
    readonly Dictionary<long, double> cloudLive = new();          // los que están sincronizando ahora (y su %)
    readonly HashSet<long> cloudAsked = new();

    long lastClosed; DateTime lastClosedAt;      // el último juego de Steam que se cerró (el que suele estar subiendo su partida)
    long waitApp, waitFrom;                        // el que se está abriendo y espera la nube, y de quién es la nube que espera

    /// <summary>Un juego de Steam se cerró: durante 15 min se revisa si sube su partida.</summary>
    void WatchCloud(long appid)
    {
        if (appid <= 0 || steam.InstallDirOf(appid) == null) return;
        cloudWatch[appid] = DateTime.Now.AddMinutes(15);
        lastClosed = appid; lastClosedAt = DateTime.Now;
    }

    async Task CloudTick()
    {
        try
        {
            var now = DateTime.Now;
            foreach (var k in cloudWatch.Where(kv => kv.Value < now && !cloudLive.ContainsKey(kv.Key)).Select(kv => kv.Key).ToList()) cloudWatch.Remove(k);
            var ids = cloudWatch.Keys.ToList();
            bool launching = tracker != null && tracker.Active && !tracker.IsReady && tracker.AppId > 0;
            if (launching && !ids.Contains(tracker.AppId)) ids.Add(tracker.AppId);   // se está abriendo: puede quedarse esperando la nube
            if (ids.Count == 0 && waitApp == 0) return;
            var r = ids.Count == 0 ? new List<SteamCdp.Cloud>() : await SteamCdp.CloudAsync(ids);
            if (r == null) return;

            // ¿El juego que se abre espera la nube? Steam lo deja en «SynchronizingCloud» mientras otro juego (el que se
            // acaba de cerrar) sube su partida: la barra naranja va en ESE otro juego y en la pantalla de carga se avisa.
            var me = launching ? r.FirstOrDefault(c => c.AppId == tracker.AppId) : null;
            bool waiting = me != null && me.Waiting;
            if (waiting && waitApp != tracker.AppId)
            {
                waitApp = tracker.AppId;
                waitFrom = lastClosed != 0 && lastClosed != waitApp && now - lastClosedAt < TimeSpan.FromMinutes(15) ? lastClosed : 0;
                Log.Write($"nube: {waitApp} espera la nube de Steam{(waitFrom != 0 ? $" (la de {waitFrom}, que se acaba de cerrar)" : "")}");
                Emit("game.cloudwait", new { appid = waitApp, from = waitFrom, on = true });
                if (waitFrom != 0 && !cloudLive.ContainsKey(waitFrom)) { cloudLive[waitFrom] = -1; Emit("cloud.progress", new { appid = waitFrom, pct = -1 }); }
            }
            else if (!waiting && waitApp != 0)
            {
                Log.Write($"nube: {waitApp} ya no espera la nube");
                Emit("game.cloudwait", new { appid = waitApp, from = waitFrom, on = false });
                long from = waitFrom; waitApp = 0; waitFrom = 0;
                var fc = r.FirstOrDefault(c => c.AppId == from);
                if (from != 0 && cloudLive.ContainsKey(from) && (fc == null || !fc.Syncing))
                {
                    cloudLive.Remove(from); cloudWatch.Remove(from);
                    Emit("cloud.done", new { appid = from, ok = fc == null || !fc.Error });
                }
            }
            if (waiting && tracker.Active && !tracker.IsReady) tracker.KeepWaiting();

            foreach (var c in r)
            {
                if (c.Ask && cloudAsked.Add(c.AppId))
                {
                    // Steam pregunta algo de verdad (por ejemplo, qué partida conservar): se enseña su ventanita.
                    Log.Write($"Steam pregunta algo al abrir {c.AppId}: se enseña su ventana");
                    Ui(() =>
                    {
                        foreach (var h in hiddenSteam.ToList())
                            if (Native.IsWindow(h) && Native.Title(h) != "Chrome Legacy Window") { Native.ShowWindowAsync(h, 5 /* SW_SHOW */); Native.ForceForeground(h); }
                        TopMost = false;
                    });
                    Emit("cloud.ask", new { appid = c.AppId });
                }
                if (!c.Ask) cloudAsked.Remove(c.AppId);
                if (c.Syncing)
                {
                    // El que se está abriendo baja su partida: eso no es una «subida», no lleva barra.
                    if (launching && c.AppId == tracker.AppId) continue;
                    bool first = !cloudLive.ContainsKey(c.AppId);
                    if (first) Log.Write($"nube: {c.AppId} sincronizando su partida{(c.Pct >= 0 ? $" ({c.Pct:0} %)" : "")}");
                    if (first || Math.Abs(cloudLive[c.AppId] - c.Pct) >= 0.5) Emit("cloud.progress", new { appid = c.AppId, pct = c.Pct });
                    cloudLive[c.AppId] = c.Pct;
                    cloudWatch[c.AppId] = now.AddMinutes(15);
                }
                else if (c.AppId == waitFrom) { }   // su barra se queda mientras el otro juego siga esperando su nube
                else if (cloudLive.Remove(c.AppId))
                {
                    Log.Write($"nube: {c.AppId} terminó{(c.Error ? " con error" : "")}");
                    cloudWatch.Remove(c.AppId);
                    Emit("cloud.done", new { appid = c.AppId, ok = !c.Error });
                }
                else if (c.Error && cloudWatch.ContainsKey(c.AppId))
                {
                    Log.Write($"nube: {c.AppId} con error de sincronización");
                    cloudWatch.Remove(c.AppId);
                    Emit("cloud.done", new { appid = c.AppId, ok = false });
                }
            }
        }
        catch (Exception ex) { Log.Error("nube", ex); }
    }
}
