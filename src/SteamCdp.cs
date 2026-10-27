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
    public static async Task<bool> RestartSteamAsync(string steamPath, Action startSteam)
    {
        if (steamPath == null) return false;
        if (!await CloseSteamAsync(steamPath)) { Log.Write("Steam: no se cerró para reiniciarse"); return false; }
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
const ensureDl = () => { const w = window; if (!w.__scarletDl) { w.__scarletDl = { items: [], ov: null }; SteamClient.Downloads.RegisterForDownloadItems((on, items) => { w.__scarletDl.items = items || []; }); SteamClient.Downloads.RegisterForDownloadOverview(o => { w.__scarletDl.ov = o; }); } return w.__scarletDl; };
" + DlNorm + @"
const queued = id => { try { return (ensureDl().items || []).map(dlItem).some(i => i.a == id && !i.done); } catch (e) { return false; } };";

    // Los nombres de Steam cambian entre versiones (ya pasó con rgApps): se lee cada dato con sus nombres posibles.
    const string DlNorm = @"
const pick = (o, ...ks) => { for (const k of ks) if (o && o[k] !== undefined && o[k] !== null) return o[k]; return undefined; };
const dlItem = i => ({ a: Number(pick(i, 'appid', 'appID', 'nAppID', 'unAppID', 'app_id')) || 0, on: !!pick(i, 'active', 'bActive'), done: !!pick(i, 'completed', 'bCompleted'),
  p: !!pick(i, 'paused', 'bPaused'), q: Number(pick(i, 'queue_index', 'nQueueIndex', 'queueIndex') ?? -1), b: Number(pick(i, 'downloaded_bytes', 'nBytesDownloaded', 'bytes_downloaded')) || 0,
  t: Number(pick(i, 'total_bytes', 'nBytesTotal', 'bytes_total')) || 0, inst: (Number(pick(i, 'buildid', 'nBuildID', 'build_id')) || 0) > 0 });";

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
const s0 = info ? sNum(info.eInstallState) : 0;
if (s0 > 0 && s0 < {Complete}) {{ I.CancelInstall(); await sleep(500); }}
I.OpenInstallWizard([id]);
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
    else {{ if (Date.now() - opened > 2500) {{ opened = Date.now(); I.OpenInstallWizard([id]); }} continue; }}
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
return Object.assign(pack(info), {{ mine: mine(info, id), fresh, folders: folders.map(f => ({{ i: f.nFolderIndex, path: f.strFolderPath || '', label: f.strUserLabel || '', drive: f.strDriveName || '', free: String(f.strFreeSpace || ''), def: !!f.bIsDefaultFolder, mounted: f.bIsMounted !== false }})) }});
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
  SteamClient.Downloads.RegisterForDownloadItems((on, items) => { w.__scarletDl.items = items || []; });
  SteamClient.Downloads.RegisterForDownloadOverview(o => { w.__scarletDl.ov = o; });
}
const d = w.__scarletDl, ov = d.ov || {};
" + DlNorm + @"
let sample = '';
try { sample = JSON.stringify({ item: (d.items || [])[0] || null, ov: d.ov }).slice(0, 900); } catch (e) {}
return { paused: !!pick(ov, 'paused', 'bPaused'), cur: Number(pick(ov, 'update_appid', 'unAppID', 'appid')) || 0, curDl: Number(pick(ov, 'update_bytes_downloaded')) || 0, curTotal: Number(pick(ov, 'update_bytes_to_download')) || 0,
  items: (d.items || []).map(dlItem).filter(i => i.a > 0), sample };
})()";

    public sealed record Dl(long AppId, double Progress, string Kind, string State);
    static bool sampleLogged;

    /// <summary>Descargas según Steam en vivo; null si no hay conexión.</summary>
    public static async Task<(List<Dl> live, List<long> done, bool ok)> DownloadsAsync()
    {
        JsonElement v;
        try { v = await EvalAsync(DownloadsJs, 3000); }
        catch { return (null, null, false); }
        if (v.ValueKind != JsonValueKind.Object) return (null, null, false);
        static double N(JsonElement o, string k) => o.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.Number ? x.GetDouble() : 0;
        static bool B(JsonElement o, string k) => o.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.True;
        if (!sampleLogged && v.TryGetProperty("sample", out var smp) && smp.ValueKind == JsonValueKind.String && smp.GetString().Contains("\"item\":{"))
        {
            sampleLogged = true;
            Log.Write("Steam: así llegan las descargas · " + smp.GetString());
        }
        bool allPaused = B(v, "paused");
        long cur = (long)N(v, "cur");
        double curDl = N(v, "curDl"), curTotal = N(v, "curTotal");
        var live = new List<Dl>(); var done = new List<long>();
        if (!v.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return (live, done, true);
        foreach (var i in items.EnumerateArray())
        {
            long a = (long)N(i, "a");
            if (a <= 0) continue;
            if (B(i, "done")) { done.Add(a); continue; }
            bool on = B(i, "on");
            int q = (int)N(i, "q");
            if (!on && q < 0) continue;   // actualizaciones programadas para después: no se muestran
            double b = N(i, "b"), t = N(i, "t");
            double p = t > 0 ? b / t : 0;
            if (on && a == cur && curTotal > 0) p = curDl / curTotal;
            string state = B(i, "p") || allPaused ? "paused" : on ? "active" : "queued";
            live.Add(new Dl(a, Math.Clamp(p, 0, 1), B(i, "inst") ? "Actualizando" : "Descargando", state));
        }
        return (live, done, true);
    }
}
