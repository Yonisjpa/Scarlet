using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace XboxScarlet;

/// <summary>Descargas en vivo desde Steam.</summary>
sealed partial class MainForm
{
    /// <summary>Progreso de descargas en vivo desde Steam (cada segundo; cada 5 s mientras juegas).</summary>
    async Task DownloadLoop()
    {
        var last = new Dictionary<long, (double p, string state, DateTime at, string phase, bool fresh)>();
        int tick = 0;
        List<LocalApp> acf = new();
        while (!IsDisposed)
        {
            // Jugando: Steam se lee cada 5 s en vez de cada segundo (beta 1.2). Abriendo un juego, cada segundo.
            await Task.Delay(wasGaming && !(quick != null && quick.IsOpen) ? 5000 : 1000);
            try
            {
                if (!SteamCdp.MarkerPresent(steam.SteamPath) || enabling) { await Task.Delay(4000); continue; }
                if (!SteamCdp.Connected && !SteamCdp.SteamRunning) { await Task.Delay(4000); continue; }
                var (live, done, ok) = await SteamCdp.DownloadsAsync();
                if (!ok) { await Task.Delay(4000); continue; }
                cdpLiveAt = DateTime.UtcNow;
                if (wasGaming || tick % 2 == 0) await CloudTick();
                // Respaldo: lo que dicen los archivos de Steam (cada 3 s), por si su lista en vivo no trae algún juego.
                // Solo las que Steam marca como descargándose (256 en curso, 512 en pausa, 1024 empezada, 1048576 bajando).
                if (tick++ % 3 == 0) acf = steam.LocalApps().Where(a => a.BytesToDownload > 0 && a.BytesDownloaded < a.BytesToDownload && (a.StateFlags & (256 | 512 | 1024 | 1048576)) != 0).ToList();
                acf.RemoveAll(a => steam.Canceled.ContainsKey(a.AppId));
                live.RemoveAll(d => steam.Canceled.ContainsKey(d.AppId));
                foreach (var a in acf)
                    if (!live.Any(x => x.AppId == a.AppId) && !done.Contains(a.AppId))
                        live.Add(new SteamCdp.Dl(a.AppId, Math.Clamp((double)a.BytesDownloaded / a.BytesToDownload, 0, 1), a.FullyInstalled ? "Actualizando" : "Descargando",
                            SteamCdp.PausedAll ? "paused" : "active", a.BytesDownloaded, a.BytesToDownload, -1, 0, "download"));
                var now = new HashSet<long>();
                foreach (var d in live)
                {
                    now.Add(d.AppId);
                    // Se manda si cambió el avance o el estado, y cada 3 s de todos modos (tiempo restante y velocidad).
                    bool had = last.TryGetValue(d.AppId, out var prev);
                    if (had && Math.Abs(prev.p - d.Progress) < 0.001 && prev.state == d.State && prev.phase == d.Phase && DateTime.Now - prev.at < TimeSpan.FromSeconds(3)) continue;
                    if (!had || prev.state != d.State || prev.phase != d.Phase)
                        Log.Write($"Steam: descarga de {d.AppId}: {d.Kind}, {d.State}, paso {d.Phase}" + (d.Err != 0 ? $", error {d.Err} {d.ErrText}" : ""));
                    // Abriste un juego y Steam se puso a actualizarlo antes (actualización obligatoria): el juego no va a
                    // aparecer hasta que termine. Scarlet deja de esperarlo y muestra la descarga; al terminar lo abre.
                    if (d.State == "active" && d.Phase != "extra" && tracker != null && tracker.Active && !tracker.IsReady && tracker.AppId == d.AppId)
                    {
                        Log.Write($"Steam: {d.AppId} se está actualizando antes de abrirse");
                        Ui(() => { tracker.Stop(); steam.SetRunning(0); ShowUi(); });
                        Emit("game.updating", new { appid = d.AppId });
                    }
                    // Una instalación nueva lo sigue siendo hasta el final aunque Steam cambie de paso (sombreadores, DLC…).
                    bool fresh = d.Fresh || (had && prev.fresh);
                    last[d.AppId] = (d.Progress, d.State, DateTime.Now, d.Phase, fresh);
                    Emit("download.progress", new { appid = d.AppId, progress = d.Progress, kind = fresh ? "Descargando" : d.Kind, state = d.State, bytes = d.Bytes, total = d.Total,
                        eta = d.Eta, rate = d.Rate, phase = d.Phase, peer = d.Peer, fresh, err = d.Err, errText = d.Err != 0 ? SteamCdp.ErrorText(d.Err, d.ErrText) : "" });
                }
                foreach (long id in last.Keys.Where(k => !now.Contains(k)).ToList())
                {
                    var gone = last[id];
                    last.Remove(id);
                    if (gone.state == "needs" && !done.Contains(id)) { Emit("download.removed", new { appid = id }); continue; }   // ya no hay actualización pendiente
                    bool installed = done.Contains(id) || steam.LocalApps().Any(a => a.AppId == id && a.FullyInstalled && a.BytesToDownload == 0);
                    if (installed) { var man = steam.LocalApps().FirstOrDefault(a => a.AppId == id); if (man != null) Ui(() => steam.Installed[id] = man); }
                    Log.Write($"Steam: descarga de {id} " + (installed ? "terminada" : "quitada de la lista"));
                    Emit(installed ? "download.done" : "download.removed", new { appid = id, fresh = gone.fresh, extra = gone.phase == "extra" });
                }
            }
            catch (Exception ex) { Log.Error("descargas en vivo", ex); await Task.Delay(5000); }
        }
    }
}
