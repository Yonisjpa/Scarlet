/* ================= beta 1.1: la nube de Steam como descarga (naranja) =================
   Al cerrar un juego Steam sube su partida a la nube (y al abrirlo la baja); mientras, no deja abrir otro juego. Se ve en
   el mosaico como una descarga, con la barra naranja y su porcentaje (si Steam no lo da, la barra va y viene). */
Events.on("cloud.progress", d => {
  if (!d || !d.appid) return;
  const had = state.cloud[d.appid];
  state.cloud[d.appid] = { p: typeof d.pct === "number" ? d.pct : -1, since: had ? had.since : Date.now() };
  if (!had) renderHome(); else paintCloud();
});
Events.on("cloud.done", d => {
  if (!d || !state.cloud[d.appid]) return;
  delete state.cloud[d.appid]; renderHome(); refreshLibrary();
  const g = byId(d.appid);
  notify(d.ok ? { kind: "dl", cloud: true, game: g, key: "cloud:" + d.appid, icon: I.cloud, bg: "#f08a24", title: "Partida guardada en la nube", text: g ? g.name : "Steam Cloud" }
    : { kind: "err", cloud: true, game: g, key: "cloud:" + d.appid, icon: I.cloud, bg: "#f08a24", title: "No se pudo sincronizar la nube", text: `${g ? g.name + ": " : ""}Steam lo intentará de nuevo` });
});
/* El juego que abres espera la nube de Steam (normalmente la partida que otro juego aún sube): se avisa en la carga. */
Events.on("game.cloudwait", d => {
  const L = $("#launch"); if (!L || !d) return;
  const from = d.from && byId(d.from);
  $(".lcloud", L).textContent = "Esperando la nube de Steam…" + (from ? ` (${from.name} aún sube su partida)` : "");
  L.classList.toggle("cwait", !!d.on && mode === "launching");   // puede llegar antes de que la carga termine de aparecer
});
Events.on("game.ready", () => $("#launch").classList.remove("cwait"));
Events.on("game.failed", () => $("#launch").classList.remove("cwait"));
/* El botón Xbox abrió Big Picture: es un ajuste de Steam. Se avisa una vez dónde apagarlo. */
Events.on("steam.guidebp", d => {
  if (d && d.changed) return notify({ kind: "err", icon: I.gear, title: "Se apagó un ajuste de Steam", text: "El botón Xbox ya no abrirá Big Picture", key: "guidebp" });
  // 1.6.1: si Big Picture se reabre solo (pasa al encender), se vuelve a explicar cómo quitarlo, aunque ya se hubiera dicho
  if (store.get("guidebpTold") && !(d && d.loop)) return; store.set("guidebpTold", 1);
  notify({ kind: "err", icon: I.gear, title: "El botón Xbox abrió Big Picture", text: "En Steam › Ajustes › Mando, apaga «El botón Guía enfoca Steam»", important: true, key: "guidebp" });
});
Events.on("cloud.ask", d => { const g = d && byId(d.appid); notify({ kind: "err", icon: I.cloud, bg: "#f08a24", title: "Steam necesita que elijas", text: `${g ? g.name + " · " : ""}partidas en la nube`, important: true }); });
function paintCloud() {
  $$("[data-cl]").forEach(el => { const c = state.cloud[el.dataset.cl]; if (!c) return el.remove(); el.classList.toggle("indet", c.p < 0); el.querySelector("i").style.width = (c.p < 0 ? 35 : c.p) + "%"; el.setAttribute("aria-label", "Sincronizando con la nube" + (c.p >= 0 ? " " + Math.round(c.p) + " %" : "")); });
}
Events.on("download.progress", d => {
  if (Date.now() - (canceledAt[d.appid] || 0) < 15000) return;
  const prev = state.downloads[d.appid], g = byId(d.appid);
  if (prev && prev.st === "canceling") return;
  // Actualización pendiente que Steam no está bajando: no es una descarga (sin franja); al jugar se pregunta.
  if (d.state === "needs") {
    if (!state.updates[d.appid]) { state.updates[d.appid] = { since: Date.now() }; }
    if (prev) { delete state.downloads[d.appid]; closeDlView(d.appid); renderHome(); refreshLibrary(); }
    return;
  }
  delete state.updates[d.appid];
  // Sombreadores o workshop de un juego que ya tiene su contenido: Steam lo hace solo y se puede jugar. Sin franja ni aviso.
  if (d.phase === "extra") {
    if (prev) finishDownload(d.appid, { extra: true });
    return;
  }
  extraDone.delete(d.appid);
  // Lo que sigue justo después de terminar una instalación (por ejemplo, su DLC) cuenta como la misma descarga.
  const fresh = !!d.fresh || (prev && prev.fresh) || Date.now() - (recentFresh[d.appid] || 0) < 15 * 60000;
  if (pendingDone[d.appid]) { clearTimeout(pendingDone[d.appid].t); delete pendingDone[d.appid]; }
  const now = state.downloads[d.appid] = { p: clamp(+d.progress || 0, 0, 1), kind: fresh ? "Descargando" : d.kind || (prev && prev.kind) || "Actualizando", st: d.state || "active",
    bytes: +d.bytes || 0, total: +d.total || 0, eta: d.eta == null ? -1 : +d.eta, rate: +d.rate || 0, phase: d.phase || "download", peer: d.peer || "", fresh,
    err: +d.err || 0, errText: d.errText || "" };
  // Steam detuvo la descarga por un problema (sin espacio, sin conexión…): se avisa una vez.
  if (now.st === "error" && errShown[d.appid] !== now.err) {
    errShown[d.appid] = now.err;
    notify({ kind: "err", game: g, icon: I.cloud, title: "Descarga detenida", text: `${g ? g.name : "Un juego"}: ${now.errText || "Steam no pudo seguir."}`, sound: "error" });
  } else if (now.st !== "error") delete errShown[d.appid];
  if (!prev || prev.st !== now.st) { renderHome(); refreshLibrary(); } else paintDownloads();
  const v = topView(); if (v && v.isDownload && v.appid == d.appid) v.paint();
});
Events.on("download.done", d => finishDownload(d.appid, { extra: d.extra, fresh: d.fresh }));
Events.on("download.removed", d => {   // la quitaste de la lista de Steam (cancelada)
  canceledAt[d.appid] = Date.now();
  delete state.updates[d.appid];
  if (!state.downloads[d.appid]) return;
  delete state.downloads[d.appid]; unpin(d.appid); closeDlView(d.appid);
  renderHome(); refreshLibrary();
});
/* Lo descargado de una instalación cancelada: Steam lo borra; si no, Scarlet lo borra con Steam cerrado unos segundos. */
const cleanupShown = new Set();
Events.on("download.cleanup", d => {
  const g = byId(d.appid), name = g ? g.name : "el juego";
  if (d.state === "restarting") { cleanupShown.add(d.appid); notify({ key: "clean:" + d.appid, icon: I.cloud, title: "Borrando lo descargado", text: `Steam se reinicia unos segundos para borrar lo de ${name}.` }); }
  else if (d.state === "done" && cleanupShown.delete(d.appid)) toast(`Se borró lo descargado de ${name}`);
  else if (d.state === "failed") { cleanupShown.delete(d.appid); notify({ key: "clean:" + d.appid, kind: "err", icon: I.cloud, title: "No se pudo borrar todo", text: `Lo descargado de ${name} sigue en el disco. Puedes desinstalarlo desde Steam.` }); }
  if (g && d.state !== "kept") { g.installed = false; refreshLibrary(); }
});
