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
const ensureDl = () => { const w = window; if (!w.__scarletDl) { w.__scarletDl = { items: [], ov: null }; SteamClient.Downloads.RegisterForDownloadItems((a, b) => { w.__scarletDl.items = Array.isArray(b) ? b : Array.isArray(a) ? a : (a && a.item_data ? [a] : []); }); SteamClient.Downloads.RegisterForDownloadOverview(o => { w.__scarletDl.ov = o; }); } return w.__scarletDl; };
" + DlNorm + @"
const queued = id => { try { return flat(ensureDl().items).map(dlItem).some(i => i.a == id && !i.done); } catch (e) { return false; } };";

    // Los nombres de Steam cambian entre versiones (ya pasó con rgApps): se lee cada dato con sus nombres posibles.
    const string DlNorm = @"
const pick = (o, ...ks) => { for (const k of ks) if (o && o[k] !== undefined && o[k] !== null) return o[k]; return undefined; };
const dlItem = i => ({ a: Number(pick(i, 'appid', 'appID', 'nAppID', 'unAppID', 'app_id')) || 0, on: !!pick(i, 'active', 'bActive'), done: !!pick(i, 'completed', 'bCompleted'),
  p: !!pick(i, 'paused', 'bPaused'), q: Number(pick(i, 'queue_index', 'nQueueIndex', 'queueIndex') ?? -1), b: Number(pick(i, 'downloaded_bytes', 'nBytesDownloaded', 'bytes_downloaded')) || 0,
  t: Number(pick(i, 'total_bytes', 'nBytesTotal', 'bytes_total')) || 0, inst: (Number(pick(i, 'buildid', 'nBuildID', 'build_id')) || 0) > 0,
  g: sumProg(((pick(i, 'update_type_info') || []).find(u => u && u.has_update) || (pick(i, 'update_type_info') || [])[0] || i)) });
// log-9: el avance llega en fases (progress: 7 entradas) + overall_percent_complete (0–100) y su tiempo restante.
// Los bytes que se están moviendo pueden estar en cualquier fase (al copiar desde otra PC de la casa no es la primera).
const sumProg = u => {
  const ph = (Array.isArray(u && u.progress) ? u.progress : []).map(x => ({ d: Number(pick(x, 'bytes_in_progress', 'bytes_done')) || 0, t: Number(pick(x, 'bytes_total')) || 0, e: Number(pick(x, 'estimated_time_remaining_sec')) }));
  const best = ph.filter(x => x.t > 0).sort((a, b) => b.d - a.d || b.t - a.t)[0] || { d: 0, t: 0, e: -1 };
  const pct = Number(pick(u, 'overall_percent_complete')), eta = Number(pick(u, 'overall_estimated_time_remaining_sec'));
  return { pct: isFinite(pct) ? pct : -1, eta: isFinite(eta) && eta >= 0 ? eta : (isFinite(best.e) ? best.e : -1), d: best.d, t: best.t || Math.max(0, ...ph.map(x => x.t)) };
};
// log-8: Steam agrupa por dispositivo { remote_client_id, item_data: [...] }; el ""0"" es esta consola.
const local = x => x && (x.remote_client_id === undefined || x.remote_client_id == 0);
const flat = items => { const out = []; for (const x of (items || [])) { if (x && Array.isArray(x.item_data)) { if (local(x)) out.push(...x.item_data); } else if (x) out.push(x); } return out; };
const ovLocal = o => Array.isArray(o) ? (o.find(local) || o[0] || {}) : (o || {});";

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
  try {{ I.OpenInstallWizard([id]); how += 'api '; }} catch (e) {{ how += 'error '; }}
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
const w = window;
if (!w.__scarletDl) {
  w.__scarletDl = { items: [], ov: null };
  SteamClient.Downloads.RegisterForDownloadItems((a, b) => { w.__scarletDl.items = Array.isArray(b) ? b : Array.isArray(a) ? a : (a && a.item_data ? [a] : []); });
  SteamClient.Downloads.RegisterForDownloadOverview(o => { w.__scarletDl.ov = o; });
}
const d = w.__scarletDl;
" + DlNorm + @"
const ov = ovLocal(d.ov), raw = flat(d.items);
const ph = (Array.isArray(ov.progress) ? ov.progress : []).map(x => ({ done: Number(pick(x, 'bytes_in_progress', 'bytes_done')) || 0, total: Number(pick(x, 'bytes_total')) || 0, eta: Number(pick(x, 'estimated_time_remaining_sec')) }));
const pct = Number(pick(ov, 'overall_percent_complete')), og = sumProg(ov);
let sItem = '', sOv = '';
try { if (raw.length) sItem = JSON.stringify(raw[0]).slice(0, 1500); } catch (e) {}
try { if (ov.update_appid) sOv = JSON.stringify(ov).slice(0, 1500); } catch (e) {}
return { og, lan: String(pick(ov, 'lan_peer_hostname') || ''), paused: !!pick(ov, 'paused', 'bPaused', 'update_paused'), cur: Number(pick(ov, 'update_appid', 'unAppID', 'appid')) || 0, state: String(pick(ov, 'update_state') ?? ''),
  rate: Number(pick(ov, 'update_network_bytes_per_second')) || 0, pct: isFinite(pct) ? pct : -1, ph,
  curDl: Number(pick(ov, 'update_bytes_downloaded')) || 0, curTotal: Number(pick(ov, 'update_bytes_to_download')) || 0,
  items: raw.map(dlItem).filter(i => i.a > 0), sItem, sOv };
})()";

    /// <summary>Una descarga de Steam: avance (0–1), bytes, tiempo restante (s, -1 si no se sabe), velocidad (B/s) y fase.</summary>
    public sealed record Dl(long AppId, double Progress, string Kind, string State, double Bytes, double Total, double Eta, double Rate, string Phase, string Peer = "");
    static bool itemLogged, ovLogged;
    /// <summary>Scarlet pausó todas las descargas (para jugar o desde el menú de descarga).</summary>
    public static volatile bool PausedAll;

    /// <summary>Descargas según Steam en vivo; null si no hay conexión.</summary>
    public static async Task<(List<Dl> live, List<long> done, bool ok)> DownloadsAsync()
    {
        JsonElement v;
        try { v = await EvalAsync(DownloadsJs, 3000); }
        catch { return (null, null, false); }
        if (v.ValueKind != JsonValueKind.Object) return (null, null, false);
        static double N(JsonElement o, string k, double def = 0) => o.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.Number ? x.GetDouble() : def;
        static bool B(JsonElement o, string k) => o.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.True;
        static string S(JsonElement o, string k) => o.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : "";
        // Una vez por sesión, cómo llegan de verdad (para afinar si Steam cambia algo).
        if (!itemLogged && S(v, "sItem") is { Length: > 0 } si) { itemLogged = true; Log.Write("Steam: así llega una descarga · " + si); }
        if (!ovLogged && S(v, "sOv") is { Length: > 0 } so) { ovLogged = true; Log.Write("Steam: así llega el avance · " + so); }

        bool allPaused = B(v, "paused") || PausedAll;
        long cur = (long)N(v, "cur");
        double rate = N(v, "rate");
        string ovState = S(v, "state"), lan = S(v, "lan");
        // log-9: el avance real es overall_percent_complete (0–100) con su tiempo restante; los bytes que se mueven
        // pueden estar en cualquier fase (copiando desde otra PC de la casa no es la primera). Ver sumProg en DlNorm.
        static (double pct, double eta, double d, double t) G(JsonElement o, string k)
        {
            if (!o.TryGetProperty(k, out var g) || g.ValueKind != JsonValueKind.Object) return (-1, -1, 0, 0);
            return (N(g, "pct", -1), N(g, "eta", -1), N(g, "d"), N(g, "t"));
        }
        var og = G(v, "og");
        var ph = new List<(double done, double total, double eta)>();
        if (v.TryGetProperty("ph", out var pa) && pa.ValueKind == JsonValueKind.Array)
            foreach (var x in pa.EnumerateArray()) ph.Add((N(x, "done"), N(x, "total"), N(x, "eta", -1)));
        var live = new List<Dl>(); var done = new List<long>();
        // Avance, bytes y tiempo con lo que haya: porcentaje de Steam primero, luego bytes.
        static (double p, double b, double t, double eta) Mix((double pct, double eta, double d, double t) g, double b, double t)
        {
            if (g.t > 0) t = g.t;
            if (g.d > 0) b = g.d;
            double p = g.pct > 0 ? g.pct / 100 : t > 0 ? b / t : 0;
            if (b <= 0 && t > 0 && p > 0) b = p * t;   // copiando por la red de la casa los bytes no vienen en las fases
            return (Math.Clamp(p, 0, 1), b, t, g.eta);
        }
        bool installing = System.Text.RegularExpressions.Regex.IsMatch(ovState, "commit|stag|verif|install", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (v.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        foreach (var i in items.EnumerateArray())
        {
            long a = (long)N(i, "a");
            if (a <= 0) continue;
            if (B(i, "done")) { done.Add(a); continue; }
            bool on = B(i, "on");
            int q = (int)N(i, "q", -1);
            if (!on && q < 0 && a != cur) continue;   // actualizaciones programadas para después: no se muestran
            var (p, b, t, eta) = Mix(G(i, "g"), N(i, "b"), N(i, "t"));
            string phase = "download";
            if (a == cur)
            {
                on = true;
                // El avance general manda (es lo que Steam muestra); el del juego, si el general aún no trae nada.
                var m = Mix(og, b, t);
                if (m.p > 0) p = m.p;
                if (m.b > 0) b = m.b;
                if (m.t > 0) t = m.t;
                if (og.eta >= 0) eta = og.eta;
                if (installing || (t > 0 && b >= t))   // ya bajó todo: está instalando / acomodando archivos
                {
                    phase = "install";
                    var inst = ph.Skip(1).FirstOrDefault(x => x.total > 0 && x.done < x.total);
                    if (eta < 0 && inst.total > 0) eta = inst.eta;
                }
                else if (lan.Length > 0) phase = "lan";
                if (eta < 0) eta = ph.Where(x => x.eta >= 0).Select(x => x.eta).DefaultIfEmpty(-1).Max();
            }
            string state = B(i, "p") || allPaused ? "paused" : on ? "active" : "queued";
            live.Add(new Dl(a, Math.Clamp(p, 0, 1), B(i, "inst") ? "Actualizando" : "Descargando", state, b, t, eta, a == cur ? rate : 0, phase, a == cur ? lan : ""));
        }
        // Si la lista no trae el juego pero el avance general sí dice cuál está bajando, se usa ese.
        if (cur > 0 && !live.Any(x => x.AppId == cur) && !done.Contains(cur) && (og.t > 0 || og.pct > 0))
        {
            var m = Mix(og, 0, 0);
            live.Add(new Dl(cur, m.p, "Descargando", allPaused ? "paused" : "active", m.b, m.t, m.eta, rate, installing ? "install" : lan.Length > 0 ? "lan" : "download", lan));
        }
        return (live, done, true);
    }

    // ───── pausar, reanudar y cancelar descargas ─────

    /// <summary>Pausa las descargas de Steam (como su botón de pausa).</summary>
    public static async Task<bool> PauseAsync(long appid)
    {
        try
        {
            var r = await EvalAsync($@"(() => {{ const D = SteamClient.Downloads; let how = '';
try {{ D.EnableAllDownloads(false); how += 'todas '; }} catch (e) {{}}
if (!how) {{ try {{ D.PauseAppUpdate({appid}); how += 'app '; }} catch (e) {{}} }}
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
try {{ D.EnableAllDownloads(true); }} catch (e) {{}}
try {{ D.ResumeAppUpdate({appid}); }} catch (e) {{}}
return true; }})()", 4000);
            PausedAll = false;
            Log.Write($"Steam: descarga de {appid} reanudada");
            return true;
        }
        catch (Exception ex) { Log.Write("Steam: reanudar descarga: " + ex.Message); return false; }
    }

    /// <summary>
    /// Cancelar instalación = desinstalar en Steam: se quita de sus descargas (eso detiene la descarga al instante) y se pide
    /// desinstalar. En la versión de escritorio esa petición a veces no hace nada (log-9) o tarda; Scarlet revisa después.
    /// </summary>
    public static async Task<string> CancelDownloadAsync(long appid)
    {
        try
        {
            var r = await EvalAsync($@"(async () => {{ const D = SteamClient.Downloads, I = SteamClient.Installs; let how = '';
try {{ D.RemoveFromDownloadList({appid}); how += 'lista '; }} catch (e) {{}}
await new Promise(r => setTimeout(r, 400));
try {{ I.OpenUninstallWizard([{appid}], true); how += 'desinstalar '; }} catch (e) {{ how += 'error '; }}
return how.trim(); }})()", 6000);
            return r.ValueKind == JsonValueKind.String ? r.GetString() : "";
        }
        catch (Exception ex) { Log.Write("Steam: cancelar instalación: " + ex.Message); return null; }
    }
}
