using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace XboxScarlet;

/// <summary>
/// Conexión directa con Steam, sin abrir sus ventanas: Steam trae un puerto de depuración local (127.0.0.1:8080) que,
/// una vez activado, deja hablar con su "SharedJSContext", el mismo motor que usan su Big Picture y la Steam Deck
/// (también lo usan Decky Loader y Millennium). Por ahí Scarlet:
///   · abre el asistente de instalación de Steam escondido, elige el disco y ve el espacio,
///   · muestra y acepta el contrato de licencia del juego,
///   · lee el progreso de las descargas en vivo.
/// Para activarlo, Steam necesita el archivo ".cef-enable-remote-debugging" en su carpeta y reiniciarse una vez.
/// El puerto solo escucha en este equipo (127.0.0.1), no en la red.
/// Es una función interna de Steam (no oficial): si una actualización de Steam la cambia, Scarlet regresa sola a la
/// ventanita de instalación de Steam de siempre.
/// </summary>
static class SteamCdp
{
    const int Port = 8080;
    public const string Marker = ".cef-enable-remote-debugging";
    static readonly HttpClient http = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
    static readonly HttpClient web = new() { Timeout = TimeSpan.FromSeconds(10) };
    static readonly SemaphoreSlim connectGate = new(1, 1), sendGate = new(1, 1);
    static readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    static ClientWebSocket ws;
    static int nextId;
    static DateTime lastFail = DateTime.MinValue;
    static bool wasUp;

    public static bool MarkerPresent(string steamPath) => steamPath != null && File.Exists(Path.Combine(steamPath, Marker));
    public static bool Connected => ws is { State: WebSocketState.Open };
    public static bool SteamRunning => Process.GetProcessesByName("steam").Length > 0;
    /// <summary>Millennium (otro mod de Steam) borra el archivo al arrancar Steam: la conexión no se podría activar.</summary>
    public static bool MillenniumInstalled(string steamPath) => steamPath != null && File.Exists(Path.Combine(steamPath, "millennium.dll"));

    // ───── conexión ─────

    static async Task<string> FindTargetAsync()
    {
        string json = await http.GetStringAsync($"http://127.0.0.1:{Port}/json");
        using var d = JsonDocument.Parse(json);
        string fallback = null;
        foreach (var t in d.RootElement.EnumerateArray())
        {
            string title = t.TryGetProperty("title", out var x) ? x.GetString() ?? "" : "";
            string url = t.TryGetProperty("webSocketDebuggerUrl", out var w) ? w.GetString() : null;
            if (string.IsNullOrEmpty(url)) continue;
            if (title == "SharedJSContext") return url;
            if (fallback == null && title == "Steam") fallback = url;
        }
        return fallback;
    }

    static async Task<ClientWebSocket> ConnectAsync()
    {
        var cur = ws;
        if (cur is { State: WebSocketState.Open }) return cur;
        await connectGate.WaitAsync();
        try
        {
            cur = ws;
            if (cur is { State: WebSocketState.Open }) return cur;
            if (DateTime.UtcNow - lastFail < TimeSpan.FromSeconds(4)) throw new IOException("Steam no tiene abierta la conexión");
            try
            {
                string url = await FindTargetAsync() ?? throw new IOException("Steam todavía no carga su interfaz");
                var s = new ClientWebSocket();
                s.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                using (var cts = new CancellationTokenSource(4000)) await s.ConnectAsync(new Uri(url), cts.Token);
                try { cur?.Dispose(); } catch { }
                ws = s;
                _ = Task.Run(() => ReceiveLoop(s));
                if (!wasUp) { wasUp = true; Log.Write("Steam: conexión directa lista"); }
                return s;
            }
            catch
            {
                lastFail = DateTime.UtcNow;
                if (wasUp) { wasUp = false; Log.Write("Steam: se perdió la conexión directa"); }
                throw;
            }
        }
        finally { connectGate.Release(); }
    }

    static async Task ReceiveLoop(ClientWebSocket s)
    {
        var buf = new byte[1 << 16];
        using var ms = new MemoryStream();
        try
        {
            while (s.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult r;
                do
                {
                    r = await s.ReceiveAsync(buf, CancellationToken.None);
                    if (r.MessageType == WebSocketMessageType.Close) throw new IOException("Steam cerró la conexión");
                    ms.Write(buf, 0, r.Count);
                    if (ms.Length > 32 << 20) throw new IOException("respuesta demasiado grande");
                } while (!r.EndOfMessage);
                try
                {
                    using var d = JsonDocument.Parse(ms.ToArray());
                    if (d.RootElement.TryGetProperty("id", out var idv) && idv.TryGetInt32(out int id) && pending.TryRemove(id, out var tcs))
                        tcs.TrySetResult(d.RootElement.Clone());
                }
                catch (JsonException) { }
            }
        }
        catch (Exception) { }
        finally
        {
            if (ReferenceEquals(ws, s)) { ws = null; lastFail = DateTime.UtcNow; }
            foreach (var k in pending.Keys) if (pending.TryRemove(k, out var t)) t.TrySetException(new IOException("se perdió la conexión con Steam"));
            try { s.Dispose(); } catch { }
        }
    }

    /// <summary>Corre JavaScript dentro de Steam y devuelve el resultado (las promesas se esperan).</summary>
    public static async Task<JsonElement> EvalAsync(string expr, int timeoutMs = 8000)
    {
        var s = await ConnectAsync();
        int id = Interlocked.Increment(ref nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = tcs;
        byte[] msg = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["id"] = id, ["method"] = "Runtime.evaluate",
            ["params"] = new Dictionary<string, object> { ["expression"] = expr, ["awaitPromise"] = true, ["returnByValue"] = true }
        });
        await sendGate.WaitAsync();
        try { await s.SendAsync(msg, WebSocketMessageType.Text, true, CancellationToken.None); }
        catch { pending.TryRemove(id, out _); throw; }
        finally { sendGate.Release(); }
        if (await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)) != tcs.Task)
        {
            pending.TryRemove(id, out _);
            throw new TimeoutException("Steam no contestó a tiempo");
        }
        var root = await tcs.Task;
        if (root.TryGetProperty("error", out var err)) throw new IOException("Steam rechazó la orden: " + err);
        var res = root.GetProperty("result");
        if (res.TryGetProperty("exceptionDetails", out var ex))
        {
            string why = ex.TryGetProperty("exception", out var e) && e.TryGetProperty("description", out var de) ? de.GetString() : ex.TryGetProperty("text", out var tx) ? tx.GetString() : "error";
            throw new IOException("Steam: " + (why ?? "error").Split('\n')[0]);
        }
        var v = res.GetProperty("result");
        return v.TryGetProperty("value", out var val) ? val.Clone() : default;
    }

    public enum Probe { NoConnection, NoApi, Ready }

    /// <summary>¿Se puede hablar con Steam ahora mismo? NoApi: hay conexión pero Steam ya no trae lo que Scarlet usa.</summary>
    public static async Task<Probe> ProbeAsync()
    {
        try
        {
            var v = await EvalAsync("typeof SteamClient === 'object' && !!SteamClient.Installs && !!SteamClient.InstallFolder && !!SteamClient.Downloads", 3000);
            return v.ValueKind == JsonValueKind.True ? Probe.Ready : Probe.NoApi;
        }
        catch { return Probe.NoConnection; }
    }

    public static async Task<bool> ReadyAsync() => await ProbeAsync() == Probe.Ready;

    // ───── activar: archivo + reiniciar Steam una vez ─────

    /// <summary>Crea el archivo en la carpeta de Steam, reinicia Steam (callado) y espera a que abra la conexión (hasta ~75 s).</summary>
    public static async Task<(bool ok, string error)> EnableAsync(string steamPath, Action startSteam)
    {
        if (steamPath == null) return (false, "No se encontró Steam en este equipo.");
        try { File.WriteAllText(Path.Combine(steamPath, Marker), ""); }
        catch (Exception ex) { Log.Error("Steam: crear " + Marker, ex); return (false, "Windows no dejó escribir en la carpeta de Steam."); }
        Log.Write("Steam: se activa la conexión directa; Steam se reinicia una vez");
        if (!await CloseSteamAsync(steamPath)) return (false, "Steam no se cerró. Ciérralo y vuelve a intentarlo.");
        lastFail = DateTime.MinValue;
        startSteam();
        for (int i = 0; i < 75; i++)
        {
            await Task.Delay(1000);
            if (i % 2 == 1) lastFail = DateTime.MinValue;
            if (await ReadyAsync()) { Log.Write("Steam: conexión directa activada"); return (true, null); }
        }
        bool still = MarkerPresent(steamPath);
        Log.Write("Steam: la conexión directa no abrió" + (still ? "" : " (algo borró el archivo: ¿Millennium?)"));
        return (false, still ? "Steam no abrió la conexión. Se usará la ventanita de Steam." : "Algo (por ejemplo, Millennium) borró el archivo al abrir Steam. Se usará la ventanita de Steam.");
    }

    /// <summary>Cierra Steam por las buenas (como "Salir" de su menú) y espera hasta 30 s.</summary>
    public static async Task<bool> CloseSteamAsync(string steamPath)
    {
        if (!SteamRunning) return true;
        try { Process.Start(new ProcessStartInfo(Path.Combine(steamPath, "steam.exe"), "-shutdown") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Steam: cerrar", ex); }
        for (int i = 0; i < 60 && SteamRunning; i++) await Task.Delay(500);
        if (SteamRunning) return false;
        try { ws?.Abort(); } catch { }
        await Task.Delay(1500);
        return true;
    }

    /// <summary>Reinicia Steam desde cero (para que detecte la conexión) y espera a que vuelva (hasta ~60 s).</summary>
    public static async Task<bool> RestartSteamAsync(string steamPath, Action startSteam, Action whileClosed = null)
    {
        if (steamPath == null) return false;
        if (!await CloseSteamAsync(steamPath)) { Log.Write("Steam: no se cerró para reiniciarse"); return false; }
        try { whileClosed?.Invoke(); } catch (Exception ex) { Log.Error("Steam: con Steam cerrado", ex); }
        lastFail = DateTime.MinValue;
        startSteam();
        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(1000);
            if (!MarkerPresent(steamPath)) { if (i > 15 && SteamRunning) return true; continue; }
            if (i % 2 == 1) lastFail = DateTime.MinValue;
            if (await ReadyAsync()) return true;
        }
        return SteamRunning;
    }

    // ───── instalar ─────

    // Estados del asistente de Steam (EInstallMgrState).
    const int ShowConfig = 7, ShowEULAs = 8, CreateApps = 9, Complete = 14, Failed = 15, Canceled = 16;
    static readonly int[] NeedsSteam = { 3, 4, 6, 10, 11, 12, 13 };   // licencia gratis, clave, contraseña, discos, registro…
    public static bool NeedsSteamUi(int state) => NeedsSteam.Contains(state);

    // Tolerante: la lista de apps puede venir como números u objetos, y el estado como número o como nombre.
    const string Js = @"const I = SteamClient.Installs, sleep = ms => new Promise(r => setTimeout(r, ms));
const NAMES = ['None','Setup','WaitLicense','FreeLicense','ShowCDKey','WaitAppInfo','ShowPassword','ShowConfig','ShowEULAs','CreateApps','ReadFromMedia','ShowChangeMedia','WaitLegacyCDKeys','ShowSignup','Complete','Failed','Canceled'];
const sNum = v => typeof v === 'number' ? v : Math.max(0, NAMES.indexOf(String(v)));
const appsOf = info => ((info && (info.rgApps || info.rgAppIDs)) || []).map(a => a && typeof a === 'object' ? (a.nAppID ?? a.unAppID ?? a.appid ?? a.appId) : a);
const mine = (info, id) => !!info && (appsOf(info).some(a => a == id) || info.currentAppID == id);
const raw = info => { try { return JSON.stringify(info).slice(0, 700); } catch (e) { return String(info); } };
const pack = info => info ? { s: sNum(info.eInstallState), need: Number(info.nDiskSpaceRequired) || 0, folder: info.iInstallFolder ?? -1, canChange: info.bCanChangeInstallFolder !== false, err: Number(info.eAppError) || 0, detail: info.errorDetail || '', cur: info.currentAppID || 0, raw: raw(info) } : { s: 0, raw: 'null' };
" + DlNorm + @"
const queued = id => { try { return dlLocal().items.map(dlItem).some(i => i.a == id && !i.done && (i.on || i.q >= 0)); } catch (e) { return false; } };";

    // Cómo lo lee la propia pantalla de descargas de Steam (su código, SteamTracking/steamui):
    //  · Steam manda lista y avance POR DISPOSITIVO (remote_client_id); "0" es esta consola. Con otra PC conectada (la laptop
    //    que le pasa el juego por la red de la casa) también llegan los de ella: antes Scarlet se quedaba con el último que
    //    llegaba y mostraba el avance de la laptop (fotos 27-oct: 92.70 GB de 78.80 GB, 1 h 2 min fijo).
    //  · Cada juego trae update_type_info por tipo: 0 juego, 1 workshop, 2 sombreadores; cada uno con 7 fases fijas:
    //    0 verificar instalados, 1 reservar espacio, 2 descargar, 3 desempacar, 4 verificar, 5 copiar, 6 acomodar.
    //  · Porcentaje del que está bajando = overall_percent_complete del avance (0–100); bytes = fase 2; tiempo = el general.
    //  · Todas sus funciones de descargas e instalación llevan el dispositivo al final ("0"); sin él algunas no hacen nada.
    //  · Los nombres pueden cambiar entre versiones: se lee cada dato con sus nombres posibles.
    const string DlNorm = @"
const LOCAL = '0';
const pick = (o, ...ks) => { for (const k of ks) if (o && o[k] !== undefined && o[k] !== null) return o[k]; return undefined; };
const ensureDl = () => {
  const w = window;
  if (!w.__scarletDl2) {
    const d = w.__scarletDl2 = { items: {}, ov: {} };
    SteamClient.Downloads.RegisterForDownloadItems((a, b) => {
      const gs = Array.isArray(b) ? b : Array.isArray(a) ? a : (a && a.item_data ? [a] : []);
      let grouped = false;
      for (const g of gs) if (g && Array.isArray(g.item_data)) { grouped = true; d.items[String(g.remote_client_id ?? LOCAL)] = g.item_data; }
      if (!grouped && gs.length) d.items[LOCAL] = gs;   // versiones viejas: lista sin grupos
    });
    SteamClient.Downloads.RegisterForDownloadOverview(o => { for (const x of (Array.isArray(o) ? o : [o])) if (x) d.ov[String(x.remote_client_id ?? LOCAL)] = x; });
  }
  return w.__scarletDl2;
};
const dlLocal = () => { const d = ensureDl(); return { items: d.items[LOCAL] || [], ov: d.ov[LOCAL] || {}, others: Object.keys(d.ov).filter(k => k != LOCAL).length }; };
const fase = (u, n) => { const x = u && Array.isArray(u.progress) ? u.progress[n] : null; return { d: Number(pick(x, 'bytes_in_progress', 'bytes_done')) || 0, t: Number(pick(x, 'bytes_total')) || 0, e: Number(pick(x, 'estimated_time_remaining_sec') ?? -1) }; };
const tipo = (ut, n) => { const u = ut[n] || {}; return { has: !!u.has_update, done: !!pick(u, 'completed', 'completed_update'), pct: Number(u.overall_percent_complete) || 0 }; };
const dlItem = i => {
  const ut = pick(i, 'update_type_info') || [];
  let d = 0, t = 0; for (const u of ut) { const f = fase(u, 2); d += f.d; t += f.t; }
  const c = tipo(ut, 0), w = tipo(ut, 1), s = tipo(ut, 2), bid = Number(pick(i, 'buildid', 'nBuildID', 'build_id')) || 0, tb = Number(pick(i, 'target_buildid')) || 0;
  return { a: Number(pick(i, 'appid', 'appID', 'nAppID', 'unAppID', 'app_id')) || 0, on: !!pick(i, 'active', 'bActive'), done: !!pick(i, 'completed', 'bCompleted'),
    p: !!pick(i, 'paused', 'bPaused'), q: Number(pick(i, 'queue_index', 'nQueueIndex', 'queueIndex') ?? -1), def: Number(pick(i, 'deferred_time')) || 0,
    b: Number(pick(i, 'downloaded_bytes', 'nBytesDownloaded', 'bytes_downloaded') ?? d) || 0, t: Number(pick(i, 'total_bytes', 'nBytesTotal', 'bytes_total') ?? t) || 0,
    inst: bid > 0, fresh: bid == 0 && tb > 0, pct: c.pct,
    game: ut.length ? c.has && !c.done : true, extra: (w.has && !w.done) || (s.has && !s.done),
    err: Number(pick(i, 'update_result')) || 0, errText: String(pick(i, 'update_error') || '') };
};
const dlOv = o => ({ cur: Number(pick(o, 'update_appid', 'unAppID', 'appid')) || 0, state: String(pick(o, 'update_state') ?? ''), paused: !!pick(o, 'paused', 'bPaused'),
  rate: Number(pick(o, 'update_network_bytes_per_second')) || 0, pct: Number(pick(o, 'overall_percent_complete')) || 0,
  eta: Number(pick(o, 'overall_estimated_time_remaining_sec', 'update_seconds_remaining') ?? -1), lan: String(pick(o, 'lan_peer_hostname') || ''),
  install: !!o.update_is_install, shader: !!o.update_is_shader, workshop: !!o.update_is_workshop, upload: !!o.update_is_upload,
  ph: [0, 1, 2, 3, 4, 5, 6].map(n => { const f = fase(o, n); return { done: f.d, total: f.t, eta: f.e }; }),
  dl: Number(pick(o, 'update_bytes_downloaded')) || 0, dlTotal: Number(pick(o, 'update_bytes_to_download')) || 0 });";

    static readonly SemaphoreSlim installGate = new(1, 1);

    /// <summary>Abre el asistente de Steam escondido y devuelve tamaño, discos y estado.</summary>
    public static async Task<JsonElement> PrepareAsync(long appid)
    {
        await installGate.WaitAsync();
        try
        {
            return await EvalAsync($@"(async () => {{ {Js}
const id = {appid};
let info = await I.GetInstallManagerInfo();
// Ya está en las descargas de Steam (por ejemplo, la franja se había perdido): no se abre el asistente otra vez.
if (queued(id)) return Object.assign(pack(info), {{ s: {Complete}, mine: true, fresh: true, how: 'ya en descargas', folders: [] }});
const s0 = info ? sNum(info.eInstallState) : 0;
if (s0 > 0 && s0 < {Complete}) {{ I.CancelInstall(); await sleep(500); }}
// En la Steam de escritorio, OpenInstallWizard no hace nada (log-7: siempre estado 0); el enlace steam://install sí abre el
// asistente. Se usa desde dentro de Steam (su ventanita la esconde el guardián de Scarlet) y la función como respaldo.
let tries = 0, how = '';
const openWizard = () => {{
  tries++;
  const url = SteamClient.URL && SteamClient.URL.ExecuteSteamURL;
  if (url && tries % 2 === 1) {{ try {{ SteamClient.URL.ExecuteSteamURL('steam://install/' + id); how += 'url '; return; }} catch (e) {{}} }}
  try {{ I.OpenInstallWizard([id], '0'); how += 'api '; }} catch (e) {{ how += 'error '; }}
}};
openWizard();
const t0 = Date.now();
let last = -1, since = Date.now(), opened = Date.now(), fresh = false;
while (Date.now() - t0 < 15000) {{
  await sleep(200);
  info = await I.GetInstallManagerInfo();
  const s = info ? sNum(info.eInstallState) : 0, need = Number(info && info.nDiskSpaceRequired) || 0;
  // Lo que quedó de un intento anterior del mismo juego (cancelado o terminado) no es la respuesta: se espera a ver
  // al asistente nuevo en camino. Steam recién abierto a veces ignora la orden: se repite cada 2.5 s.
  if (!fresh) {{
    if (mine(info, id) && ![0, {Complete}, {Failed}, {Canceled}].includes(s)) fresh = true;
    else {{ if (Date.now() - opened > 2500) {{ opened = Date.now(); openWizard(); }} continue; }}
  }}
  if (s !== last) {{ last = s; since = Date.now(); }}
  if (Number(info.eAppError) > 0 && s !== {ShowConfig}) break;
  if (s === {ShowConfig} && (need > 0 || Date.now() - t0 > 4000)) break;
  if (s > {ShowConfig} || [{string.Join(",", NeedsSteam)}].includes(s)) break;
  // estado que Scarlet no conoce (Steam pudo cambiar sus números): si ya trae el tamaño y no se mueve, es la pantalla del disco
  if (need > 0 && ![1, 2, 5].includes(s) && Date.now() - since > 1200) break;
}}
let folders = [];
try {{ folders = (await SteamClient.InstallFolder.GetInstallFolders()) || []; }} catch (e) {{}}
return Object.assign(pack(info), {{ mine: mine(info, id), fresh, how: how.trim(), folders: folders.map(f => ({{ i: f.nFolderIndex, path: f.strFolderPath || '', label: f.strUserLabel || '', drive: f.strDriveName || '', free: String(f.strFreeSpace || ''), def: !!f.bIsDefaultFolder, mounted: f.bIsMounted !== false }})) }});
}})()", 26000);
        }
        finally { installGate.Release(); }
    }

    /// <summary>Cambia el disco del asistente (sin tocar tus opciones de accesos directos).</summary>
    public static async Task<JsonElement> SetFolderAsync(long appid, int folder)
    {
        return await EvalAsync($@"(async () => {{ {Js}
const id = {appid};
let info = await I.GetInstallManagerInfo();
const sc = info ? sNum(info.eInstallState) : 0;
if (!mine(info, id) || sc === 0 || sc >= {Complete}) return Object.assign(pack(info), {{ stale: true }});
I.SetInstallFolder({folder});
I.SetCreateShortcuts(!!info.bDesktopShortcut, !!info.bSystemMenuShortcut);
await sleep(350);
return pack(await I.GetInstallManagerInfo());
}})()", 6000);
    }

    /// <summary>Espera a que el asistente pase a algo que requiera respuesta (contrato, listo, falla…).</summary>
    const string WaitNext = @"
const waitNext = async (id, from, ms) => {
  const t0 = Date.now(); let info;
  while (Date.now() - t0 < ms) {
    await sleep(250);
    info = await I.GetInstallManagerInfo();
    if (queued(id)) { const r = pack(info); r.started = true; return r; }   // ya está en las descargas de Steam
    const s = info ? sNum(info.eInstallState) : 0;
    if (!info || s === 0 || s >= 14) break;
    if (s !== from && s !== 9 && s !== 1 && s !== 2 && s !== 5) break;
  }
  const r = pack(info);
  if (queued(id)) r.started = true;
  if (r.s === 8) { try { r.eulas = await SteamClient.Apps.LoadEula(r.cur || id); } catch (e) { r.eulas = []; } }
  return r;
};";

    /// <summary>"Instalar": elige el disco y le dice a Steam que siga.</summary>
    public static async Task<JsonElement> ContinueAsync(long appid, int folder)
    {
        await installGate.WaitAsync();
        try
        {
            return await EvalAsync($@"(async () => {{ {Js} {WaitNext}
const id = {appid};
let info = await I.GetInstallManagerInfo();
const sc = info ? sNum(info.eInstallState) : 0;
if (!mine(info, id) || sc === 0 || sc >= {Complete}) return Object.assign(pack(info), {{ stale: true }});
if ({folder} >= 0 && info.iInstallFolder !== {folder}) {{ I.SetInstallFolder({folder}); I.SetCreateShortcuts(!!info.bDesktopShortcut, !!info.bSystemMenuShortcut); await sleep(300); }}
I.ContinueInstall();
return await waitNext(id, sc, 20000);
}})()", 26000);
        }
        finally { installGate.Release(); }
    }

    /// <summary>Contrato de licencia: aceptar (y seguir) o rechazar (y cancelar).</summary>
    public static async Task<JsonElement> EulaAsync(long appid, bool accept, IEnumerable<(string id, int version)> eulas)
    {
        await installGate.WaitAsync();
        try
        {
            string marks = string.Join("\n", eulas.Select(e => $"SteamClient.Apps.MarkEulaAccepted(app, {JsonSerializer.Serialize(e.id)}, {e.version});"));
            return await EvalAsync($@"(async () => {{ {Js} {WaitNext}
const id = {appid};
let info = await I.GetInstallManagerInfo();
const se = info ? sNum(info.eInstallState) : 0;
if (!info || se === 0 || se >= {Complete}) return Object.assign(pack(info), {{ stale: true }});
const app = info.currentAppID || id;
if (!{(accept ? "true" : "false")}) {{ I.CancelInstall(); await sleep(300); return Object.assign(pack(await I.GetInstallManagerInfo()), {{ s: {Canceled} }}); }}
{marks}
await sleep(200);
I.ContinueInstall();
const r = await waitNext(id, se, 12000);
if (r.s === {ShowEULAs} && JSON.stringify((r.eulas || []).map(e => e.id)) === JSON.stringify({JsonSerializer.Serialize(eulas.Select(e => e.id))})) r.stuck = true;
return r;
}})()", 20000);
        }
        finally { installGate.Release(); }
    }

    public static async Task CancelAsync()
    {
        try { await EvalAsync($@"(async () => {{ {Js} const i = await I.GetInstallManagerInfo(); const s = i ? sNum(i.eInstallState) : 0; if (s > 0 && s < {Complete}) I.CancelInstall(); return true; }})()", 4000); }
        catch (Exception ex) { Log.Write("Steam: cancelar instalación: " + ex.Message); }
    }

    /// <summary>Texto del contrato: la página de Steam (la interfaz la convierte en texto; nunca se ejecuta).</summary>
    public static async Task<string> EulaHtmlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http")) return "";
        try
        {
            if (u.Host.EndsWith("steampowered.com", StringComparison.OrdinalIgnoreCase) && !u.Query.Contains("l="))
                u = new Uri(url + (u.Query.Length > 0 ? "&" : "?") + "l=spanish");
            using var req = new HttpRequestMessage(HttpMethod.Get, u);
            req.Headers.AcceptLanguage.ParseAdd("es-MX,es;q=0.9,en;q=0.5");
            using var resp = await web.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return "";
            await using var s = await resp.Content.ReadAsStreamAsync();
            var buf = new char[1_500_000];
            using var rd = new StreamReader(s, Encoding.UTF8);
            int n = await rd.ReadBlockAsync(buf, 0, buf.Length);
            return new string(buf, 0, n);
        }
        catch (Exception ex) { Log.Write("Steam: contrato de licencia: " + ex.Message); return ""; }
    }

    public static string ErrorText(int code, string detail) => code switch
    {
        5 => "Este juego no está en tu cuenta de Steam.",
        6 => "Steam no tiene conexión a internet.",
        7 => "Steam tardó demasiado en contestar.",
        11 => "No se pudo escribir en el disco.",
        12 or 41 => "No hay espacio suficiente en ese disco.",
        15 => "Esa carpeta de instalación no sirve.",
        16 => "El juego está abierto.",
        21 => "Steam no encontró de dónde descargarlo.",
        25 => "Este juego todavía no sale.",
        26 => "Este juego no está disponible en tu región.",
        34 => "Tu licencia del juego todavía se está procesando.",
        _ => "Steam no pudo instalarlo" + (string.IsNullOrWhiteSpace(detail) ? $" (código {code})." : $": {detail}")
    };

    // ───── descargas en vivo ─────

    static string DownloadsJs => @"(() => {
" + DlNorm + @"
const L = dlLocal();
let sItem = '', sOv = '';
try { if (L.items.length) sItem = JSON.stringify(L.items[0]).slice(0, 1500); } catch (e) {}
try { if (L.ov.update_appid) sOv = JSON.stringify(Object.assign({}, L.ov, { history: undefined })).slice(0, 1500); } catch (e) {}
return { ov: dlOv(L.ov), items: L.items.map(dlItem).filter(i => i.a > 0), others: L.others, sItem, sOv };
})()";

    /// <summary>
    /// Una descarga de Steam en esta consola. State: active / paused / queued / error / needs (actualización pendiente que
    /// Steam no ha puesto a bajar: al jugar la pedirá). Phase: download / lan / prealloc / verify / install / extra
    /// (sombreadores o workshop de un juego ya listo: no es descarga del juego). Fresh: instalación nueva (no actualización).
    /// </summary>
    public sealed record Dl(long AppId, double Progress, string Kind, string State, double Bytes, double Total, double Eta, double Rate, string Phase,
        string Peer = "", bool Fresh = false, int Err = 0, string ErrText = "");
    static bool itemLogged, ovLogged;
    static DateTime lastDlLog;
    static string lastDlKey = "";
    /// <summary>Scarlet pausó todas las descargas (solo cuenta si no hay conexión directa para preguntarle a Steam).</summary>
    public static volatile bool PausedAll;

    /// <summary>Descargas de esta consola según Steam en vivo; ok = false si no hay conexión directa.</summary>
    public static async Task<(List<Dl> live, List<long> done, bool ok)> DownloadsAsync()
    {
        JsonElement v;
        try { v = await EvalAsync(DownloadsJs, 3000); }
        catch { return (null, null, false); }
        if (v.ValueKind != JsonValueKind.Object) return (null, null, false);
        // Una vez por sesión, cómo llegan de verdad (para afinar si Steam cambia algo).
        if (!itemLogged && S(v, "sItem") is { Length: > 0 } si) { itemLogged = true; Log.Write("Steam: así llega una descarga · " + si); }
        if (!ovLogged && S(v, "sOv") is { Length: > 0 } so) { ovLogged = true; Log.Write("Steam: así llega el avance · " + so); }
        var (live, done, line) = ParseDownloads(v);
        if (line != null)
        {
            string key = line.Split(',')[0] + "|" + (line.Contains("sombreadores") || line.Contains("workshop")) + "|" + line.Contains("SUBIDA");
            if (key != lastDlKey || DateTime.Now - lastDlLog > TimeSpan.FromSeconds(60)) { lastDlKey = key; lastDlLog = DateTime.Now; Log.Write(line); }
        }
        return (live, done, true);
    }

    static double N(JsonElement o, string k, double def = 0) => o.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.Number ? x.GetDouble() : def;
    static bool B(JsonElement o, string k) => o.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.True;
    static string S(JsonElement o, string k) => o.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : "";

    /// <summary>
    /// Lo que mandó Steam (ya normalizado por DownloadsJs) → descargas de esta consola, terminadas y una línea para el log
    /// (null si no hay nada bajando). Sin efectos: se prueba en la compilación (tests/DlTests) con datos reales.
    /// </summary>
    internal static (List<Dl> live, List<long> done, string logLine) ParseDownloads(JsonElement v)
    {
        var live = new List<Dl>(); var done = new List<long>();
        if (!v.TryGetProperty("ov", out var ov) || ov.ValueKind != JsonValueKind.Object) return (live, done, null);
        string ovState = S(ov, "state"), lan = S(ov, "lan");
        // Si esta consola le está pasando un juego a otra PC (subida), ese avance no es una descarga de aquí.
        bool upload = B(ov, "upload");
        long cur = upload ? 0 : (long)N(ov, "cur");
        bool allPaused = B(ov, "paused");
        double rate = N(ov, "rate"), pct = N(ov, "pct"), oEta = N(ov, "eta", -1);
        bool extraNow = B(ov, "shader") || B(ov, "workshop");
        var ph = new List<(double done, double total, double eta)>();
        if (ov.TryGetProperty("ph", out var pa) && pa.ValueKind == JsonValueKind.Array)
            foreach (var x in pa.EnumerateArray()) ph.Add((N(x, "done"), N(x, "total"), N(x, "eta", -1)));
        (double done, double total, double eta) F(int n) => n < ph.Count ? ph[n] : (0d, 0d, -1d);
        // Qué está haciendo Steam (update_state) y en cuál de sus 7 fases van los bytes de ese paso.
        var (phase, idx) = ovState switch
        {
            "Preallocating" => ("prealloc", 1),
            "Validating" or "VerifyingFiles" => ("verify", 0),
            "Verifying" => ("verify", 4),
            "Unpacking" or "Patching" or "Installing" or "Staging" => ("install", 3),
            "Copying" => ("install", 5),
            "Finalizing" or "Committing" => ("install", 6),
            "Transferring" => ("lan", 2),
            _ => (lan.Length > 0 ? "lan" : "download", 2)
        };

        if (v.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        foreach (var i in items.EnumerateArray())
        {
            long a = (long)N(i, "a");
            if (a <= 0) continue;
            if (B(i, "done")) { done.Add(a); continue; }
            bool active = a == cur, on = B(i, "on") || active;
            int q = (int)N(i, "q", -1), err = (int)N(i, "err");
            bool fresh = active ? B(ov, "install") : B(i, "fresh");
            // Sombreadores / workshop de un juego que ya tiene su contenido: trabajo extra, no una descarga del juego.
            bool extra = active ? extraNow && !B(i, "game") : !B(i, "game") && B(i, "extra");
            double b = N(i, "b"), t = N(i, "t"), eta = -1, p;
            string itPhase = "download";
            if (active)
            {
                var dl = F(2);
                if (dl.total > 0) { b = dl.done; t = dl.total; }
                else if (N(ov, "dlTotal") > 0) { b = N(ov, "dl"); t = N(ov, "dlTotal"); }
                p = pct > 0 ? pct / 100 : t > 0 ? b / t : 0;
                eta = oEta >= 0 ? oEta : dl.eta;
                itPhase = phase;
                // Reservando espacio, desempacando o verificando: «X de Y» es de ese paso (como lo muestra Steam).
                var step = F(idx);
                if (phase is "prealloc" or "install" or "verify" && step.total > 0) { b = step.done; t = step.total; if (eta < 0) eta = step.eta; }
            }
            else p = N(i, "pct") > 0 ? N(i, "pct") / 100 : t > 0 ? b / t : 0;
            if (extra) itPhase = "extra";
            if (t > 0) b = Math.Min(b, t);   // nunca «X de Y» con X mayor que Y
            string state;
            if (err != 0 && !active) state = "error";
            else if (B(i, "p") || (allPaused && (on || q >= 0))) state = "paused";
            else if (active || B(i, "on")) state = "active";
            else if (q >= 0 && N(i, "def") == 0) state = "queued";
            else state = "needs";   // sin cola (o programada para después): al jugar, Steam pedirá actualizar
            if (state == "needs" && (fresh || extra)) continue;   // solo una actualización pendiente del juego cuenta así
            live.Add(new Dl(a, Math.Clamp(p, 0, 1), fresh ? "Descargando" : "Actualizando", state, b, t, eta, active ? rate : 0, itPhase,
                active ? lan : "", fresh, err, S(i, "errText")));
        }
        // La lista aún no trae el juego pero el avance general sí dice cuál está bajando.
        if (cur > 0 && !live.Any(x => x.AppId == cur) && !done.Contains(cur) && F(2).total > 0)
        {
            var dl = F(2);
            live.Add(new Dl(cur, pct > 0 ? pct / 100 : Math.Clamp(dl.done / dl.total, 0, 1), B(ov, "install") ? "Descargando" : "Actualizando",
                allPaused ? "paused" : "active", Math.Min(dl.done, dl.total), dl.total, oEta, rate, extraNow ? "extra" : phase, lan, B(ov, "install")));
        }
        // Para el log (cada cambio de paso y, mientras baja, cada minuto): estado, %, GB por fase, tipo y de dónde.
        string line = cur > 0 || upload
            ? $"Steam: avance de {(long)N(ov, "cur")} ({ovState}), {pct} %, fases " + string.Join(" ", ph.Select(x => x.total > 0 ? $"{x.done / 1e9:0.##}/{x.total / 1e9:0.##}" : "-"))
                + $", resta {oEta} s" + (B(ov, "install") ? ", instalación" : "") + (B(ov, "shader") ? ", sombreadores" : "") + (B(ov, "workshop") ? ", workshop" : "")
                + (upload ? ", SUBIDA (se ignora)" : "") + (lan.Length > 0 ? $", desde {lan}" : "") + $", otros dispositivos {N(v, "others")}"
            : null;
        return (live, done, line);
    }

    // ───── pausar, reanudar, cancelar, actualizar ya y desinstalar ─────
    // Como los botones de Steam: todas sus funciones llevan el dispositivo ("0" = esta consola). Sin él, la de desinstalar
    // tomaba el "true" como dispositivo y no hacía nada (por eso Cancelar instalación no borraba en el log-9).

    /// <summary>Pausa las descargas de Steam (como su botón de pausa).</summary>
    public static async Task<bool> PauseAsync(long appid)
    {
        try
        {
            var r = await EvalAsync($@"(() => {{ const D = SteamClient.Downloads; let how = '';
try {{ D.EnableAllDownloads(false, '0'); how += 'todas '; }} catch (e) {{}}
if (!how) {{ try {{ D.PauseAppUpdate({appid}, '0'); how += 'app '; }} catch (e) {{}} }}
return how.trim(); }})()", 4000);
            PausedAll = true;
            Log.Write($"Steam: descarga de {appid} en pausa ({(r.ValueKind == JsonValueKind.String ? r.GetString() : "?")})");
            return true;
        }
        catch (Exception ex) { Log.Write("Steam: pausar descarga: " + ex.Message); return false; }
    }

    public static async Task<bool> ResumeAsync(long appid)
    {
        try
        {
            await EvalAsync($@"(() => {{ const D = SteamClient.Downloads;
try {{ D.EnableAllDownloads(true, '0'); }} catch (e) {{}}
try {{ D.ResumeAppUpdate({appid}, '0'); }} catch (e) {{}}
return true; }})()", 4000);
            PausedAll = false;
            Log.Write($"Steam: descarga de {appid} reanudada");
            return true;
        }
        catch (Exception ex) { Log.Write("Steam: reanudar descarga: " + ex.Message); return false; }
    }

    /// <summary>Actualizar ya: la pone primera en la cola y la arranca (como "Actualizar" en Steam).</summary>
    public static async Task<bool> UpdateNowAsync(long appid)
    {
        try
        {
            var r = await EvalAsync($@"(() => {{ const D = SteamClient.Downloads; let how = '';
try {{ D.QueueAppUpdate({appid}, '0'); how += 'cola '; }} catch (e) {{}}
try {{ D.SetQueueIndex({appid}, 0, '0'); how += 'primera '; }} catch (e) {{}}
try {{ D.ResumeAppUpdate({appid}, '0'); how += 'arranca '; }} catch (e) {{}}
try {{ D.EnableAllDownloads(true, '0'); }} catch (e) {{}}
return how.trim(); }})()", 4000);
            PausedAll = false;
            Log.Write($"Steam: actualizar {appid} ya ({(r.ValueKind == JsonValueKind.String ? r.GetString() : "?")})");
            return true;
        }
        catch (Exception ex) { Log.Write("Steam: actualizar ya: " + ex.Message); return false; }
    }

    /// <summary>
    /// Quita una descarga de la lista de Steam (la detiene al instante; si era la primera y todo estaba en pausa, la
    /// siguiente sigue, como en Steam). Con uninstall, además pide desinstalar sin preguntar: solo para una instalación
    /// nueva que cancelas. Una actualización cancelada NUNCA desinstala el juego.
    /// </summary>
    public static async Task<string> CancelDownloadAsync(long appid, bool uninstall)
    {
        try
        {
            var r = await EvalAsync($@"(async () => {{ {DlNorm} const D = SteamClient.Downloads, I = SteamClient.Installs; let how = '';
const L = dlLocal(), it = L.items.map(dlItem).find(i => i.a == {appid});
try {{ D.RemoveFromDownloadList({appid}, '0'); how += 'lista '; }} catch (e) {{}}
if (it && it.q === 0 && L.ov.paused) {{ try {{ D.EnableAllDownloads(true, '0'); }} catch (e) {{}} }}
if ({(uninstall ? "true" : "false")}) {{
  await new Promise(r => setTimeout(r, 400));
  try {{ I.OpenUninstallWizard([{appid}], '0', true); how += 'desinstalar '; }} catch (e) {{ how += 'error '; }}
}}
return how.trim(); }})()", 6000);
            return r.ValueKind == JsonValueKind.String ? r.GetString() : "";
        }
        catch (Exception ex) { Log.Write("Steam: cancelar descarga: " + ex.Message); return null; }
    }

    /// <summary>Desinstalar sin la ventanita de Steam (Scarlet ya preguntó): la misma función que llama su botón "Desinstalar".</summary>
    public static async Task<bool> UninstallAsync(long appid)
    {
        try
        {
            await EvalAsync($@"(() => {{ SteamClient.Installs.OpenUninstallWizard([{appid}], '0', true); return true; }})()", 4000);
            Log.Write($"Steam: desinstalar {appid} (sin preguntar)");
            return true;
        }
        catch (Exception ex) { Log.Write("Steam: desinstalar: " + ex.Message); return false; }
    }
}
