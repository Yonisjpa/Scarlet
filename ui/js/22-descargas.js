/* ================= descargas y actualizaciones ================= */
/* "Descargando 40 %", "En cola"… (título del juego, propiedades y lectores de pantalla) */
function dlLabel(d) {
  if (!d) return "";
  if (d.st === "pending") return "Preparando…";
  if (d.st === "queued") return "En cola";
  if (d.st === "paused") return "En pausa";
  if (d.st === "canceling") return "Cancelando…";
  return `${Math.round(d.p * 100)} %`;
}
function paintDownloads() {
  $$("[data-dl]").forEach(el => {
    const d = state.downloads[el.dataset.dl]; if (!d) return el.remove();
    el.querySelector("i").style.width = d.p * 100 + "%";
    el.classList.toggle("paused", d.st === "paused" || d.st === "canceling"); el.classList.toggle("queued", d.st === "queued");
    el.setAttribute("aria-label", dlLabel(d));
  });
  paintTitleState();
}
/* La descarga ya no existe (terminó o se quitó): su menú "Descargando datos…" se cierra. */
function closeDlView(appid) { const v = views.find(x => x.isDownload && x.appid == appid); if (v) dropView(v); }
/* Terminó. El aviso sale una sola vez por juego: se espera unos segundos por si Steam sigue con algo del mismo juego
   (DLC justo después de instalar: es la misma descarga), y si terminan varias a la vez se avisan juntas. Lo que Steam
   hace después (sombreadores, workshop) no avisa. Si Steam la exigió al abrir el juego, el juego se abre solo. */
const recentFresh = {}, pendingDone = {};
let doneBatch = [], doneBatchT = 0;
function finishDownload(appid, info = {}) {
  const g = byId(appid), d = state.downloads[appid];
  delete state.updates[appid];
  if (!d) { if (g && !info.extra) g.installed = true; return; }   // ya se había avisado (o era trabajo extra)
  delete state.downloads[appid]; closeDlView(appid);
  if (g) g.installed = true;
  const fresh = d.fresh || d.kind === "Descargando" || info.fresh;
  if (fresh) recentFresh[appid] = Date.now();
  renderHome(); refreshLibrary();
  if (!g) return;
  if (launchAfter.delete(appid) && mode === "ui" && !state.running) { toast(`Abriendo ${g.name}…`); setTimeout(() => launchGame(g, null, { dlAsked: true, updAsked: true }), 600); return; }
  if (info.extra) extraDone.add(appid);
  if (pendingDone[appid]) clearTimeout(pendingDone[appid].t);
  pendingDone[appid] = { fresh, t: setTimeout(() => {
    delete pendingDone[appid];
    if (state.downloads[appid]) return;   // siguió bajando algo del mismo juego: se avisa al final
    doneBatch.push({ g, fresh });
    clearTimeout(doneBatchT);
    doneBatchT = setTimeout(() => {
      const b = doneBatch; doneBatch = [];
      if (b.length === 1) { const g1 = b[0].g; notify({ kind: "dl", game: g1, icon: I.cloud, title: b[0].fresh ? "Descarga completada" : "Actualización completada", text: g1.name, act: { label: "Jugar", run: () => { if (mode === "ui" && !state.running) { closeAllViews(); launchGame(g1); } } } }); }
      else if (b.length > 1) notify({ kind: "dl", icon: I.cloud, title: b.every(x => !x.fresh) ? `${b.length} juegos actualizados` : `${b.length} descargas completadas`, text: b.map(x => x.g.name).join(", ") });
    }, 1500);
  }, fresh ? 6000 : 1500) };
}
function refreshLibrary() { const lv = views.find(v => v.refresh && v.list && v.cols === 6); if (lv) lv.refresh(true); }
function startDownload(g, kind, st = "active") {
  state.downloads[g.appid] = { p: 0, kind, st };
  renderHome(); refreshLibrary();
}
/* El juego que empiezas a instalar pasa al primer lugar (y se queda ahí si de verdad se instala). */
function pinFirst(g) {
  settings.installedAt = Object.assign({}, settings.installedAt, { [g.appid]: Date.now() }); saveSettings();
  if (!views.length) { home.zone = "games"; home.g = 0; }
  renderHome();
}
function unpin(appid) {
  const g = byId(appid);
  if (g && !g.installed && settings.installedAt && settings.installedAt[appid]) { delete settings.installedAt[appid]; saveSettings(); }
}
/* La ventanita de Steam de siempre (sin conexión directa, o cuando Steam pide algo que Scarlet no maneja). */
function installViaSteam(g) {
  pinFirst(g);
  // Si cancelaste en Steam (no empezó a descargar), deja de estar primero.
  setTimeout(() => { if (!state.downloads[g.appid] && !g.installed) { unpin(g.appid); renderHome(); } }, 180000);
  Data.open("steam://install/" + g.appid, "la instalación de " + g.name);
  if (!Host.has) startDownload(g, "Descargando");
}
const canceledAt = {};   // recién cancelada: un avance atrasado de Steam no la revive
const launchAfter = new Set();   // se abre solo al terminar su actualización (Steam la exigió al abrirlo)
const extraDone = new Set();     // ya se avisó que terminó; lo que sigue (sombreadores, workshop) no es otra descarga
const errShown = {};
