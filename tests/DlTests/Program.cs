// Pruebas del lector de descargas (SteamCdp.ParseDownloads) con escenarios de Steam: dos dispositivos, sombreadores,
// actualizaciones, cola, pendientes, errores, subida, reservar espacio y formato viejo. Si algo falla, no se publica.
using System.Text.Json;
using XboxScarlet;

var fx = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures.json"))).RootElement;
int ok = 0, bad = 0;
void Check(string name, bool cond, string info = "")
{
    if (cond) { ok++; Console.WriteLine("OK    " + name); }
    else { bad++; Console.WriteLine("FALLA " + name + (info.Length > 0 ? " — " + info : "")); }
}
(List<SteamCdp.Dl> live, List<long> done, string line) P(string n) => SteamCdp.ParseDownloads(fx.GetProperty(n));
static bool Near(double a, double b) => Math.Abs(a - b) < 1e-6 * Math.Max(1, Math.Abs(b));
string D(SteamCdp.Dl d) => d == null ? "nada" : $"{d.AppId} {d.State} {d.Phase} {d.Kind} p={d.Progress:0.###} {d.Bytes / 1e9:0.##}/{d.Total / 1e9:0.##} eta={d.Eta} peer={d.Peer} fresh={d.Fresh} err={d.Err}";

{
    var (live, _, line) = P("lan_dos_dispositivos");
    var d = live.SingleOrDefault();
    Check("Dos dispositivos: 83 %, 65.6 de 78.8 GB y 14:36 de ESTA consola (no los de la laptop)",
        d != null && Near(d.Progress, .83) && Near(d.Bytes, 65.6e9) && Near(d.Total, 78.8e9) && d.Eta == 876 && d.Phase == "lan" && d.Peer == "YONIS"
        && d.Fresh && d.Kind == "Descargando" && d.State == "active" && Near(d.Rate, 16.3e6), D(d));
    Check("… y el log lo dice", line != null && line.Contains("desde YONIS") && line.Contains("otros dispositivos 1"), line);
}
{
    var (live, _, _) = P("lista_solo_remota");
    Check("Lista solo de la otra PC: la descarga de esta consola sigue", live.Count == 1 && live[0].AppId == 1091500 && Near(live[0].Progress, .1), string.Join("; ", live.Select(D)));
}
{
    var d = P("sombreadores").live.SingleOrDefault();
    Check("Sombreadores después de instalar: trabajo extra (sin franja ni aviso)", d != null && d.Phase == "extra" && !d.Fresh && d.State == "active", D(d));
}
{
    var (live, _, _) = P("actualizacion_y_cola");
    var a = live.FirstOrDefault(x => x.AppId == 570); var b = live.FirstOrDefault(x => x.AppId == 440);
    Check("Actualización bajando: 40 %, 2 min, es actualización", a != null && Near(a.Progress, .4) && a.Eta == 120 && a.Kind == "Actualizando" && !a.Fresh && a.State == "active" && Near(a.Bytes, 4e8), D(a));
    Check("La siguiente en cola", b != null && b.State == "queued" && b.Rate == 0 && b.Eta == -1, D(b));
}
{
    var (live, done, _) = P("pendientes");
    var u = live.FirstOrDefault(x => x.AppId == 10); var s = live.FirstOrDefault(x => x.AppId == 20);
    Check("Actualización sin cola: pendiente (se pregunta al jugar)", u != null && u.State == "needs" && u.Kind == "Actualizando", D(u));
    Check("Actualización programada para después: también pendiente", s != null && s.State == "needs", D(s));
    Check("Instalación a medias sin cola: no cuenta como descarga", !live.Any(x => x.AppId == 30), string.Join("; ", live.Select(D)));
    Check("Terminada: va a terminadas", done.Contains(40) && !live.Any(x => x.AppId == 40));
}
{
    var (live, _, _) = P("error_y_pausa");
    var e = live.FirstOrDefault(x => x.AppId == 50); var p = live.FirstOrDefault(x => x.AppId == 60);
    Check("Steam la detuvo (sin poder escribir): error con su código", e != null && e.State == "error" && e.Err == 12 && e.ErrText == "Disk write failure", D(e));
    Check("Todo en pausa: la de la cola sale en pausa", p != null && p.State == "paused", D(p));
}
{
    var (live, _, line) = P("solo_subida");
    Check("Solo le pasa un juego a otra PC: no hay descarga aquí", live.Count == 0 && line != null && line.Contains("SUBIDA"), line);
}
{
    var d = P("reservando").live.SingleOrDefault();
    Check("Reservando espacio: «X de Y» de ese paso y aún 0 %", d != null && d.Phase == "prealloc" && Near(d.Bytes, 1e9) && Near(d.Total, 89e9) && d.Progress == 0 && d.Fresh, D(d));
}
{
    var d = P("formato_viejo").live.SingleOrDefault();
    Check("Formato viejo de Steam: se lee igual", d != null && d.AppId == 292030 && Near(d.Progress, .3) && Near(d.Bytes, 3e9) && Near(d.Total, 1e10) && d.Kind == "Descargando", D(d));
}
{
    // Nunca «X de Y» con X mayor que Y, aunque Steam mande bytes de más.
    var all = fx.EnumerateObject().SelectMany(o => SteamCdp.ParseDownloads(o.Value).live);
    Check("Nunca más bytes que el total ni avance fuera de 0–100 %", all.All(d => (d.Total <= 0 || d.Bytes <= d.Total) && d.Progress >= 0 && d.Progress <= 1));
}
Console.WriteLine($"\n{ok}/{ok + bad} pruebas de descargas OK");
return bad == 0 ? 0 : 1;
