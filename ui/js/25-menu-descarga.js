/* ================= menú de descarga (como en la consola) =================
   A sobre un juego que se descarga: "Descargando datos…" con barra, porcentaje y tiempo restante, y "Opciones de descarga"
   (pausar / reanudar / cancelar instalación). Se actualiza en vivo con lo que manda Steam. */
const fmtEta = s => !(s >= 0) ? "Calculando…" : s < 60 ? "menos de 1 min" : s < 3600 ? `${Math.round(s / 60)} min` : `${Math.floor(s / 3600)} h ${Math.round(s % 3600 / 60)} min`;
const fmtRate = b => b > 0 ? (b >= 1048576 ? `${(b / 1048576).toFixed(1)} MB/s` : `${Math.max(1, Math.round(b / 1024))} KB/s`) : "";
function dlTitle(d) {
  if (!d) return "Descarga terminada";
  if (d.st === "pending") return "Preparando la descarga…";
  if (d.st === "canceling") return d.fresh || d.kind === "Descargando" ? "Cancelando la instalación…" : "Cancelando la actualización…";
  if (d.st === "error") return "Descarga detenida";
  if (d.st === "paused") return "Descarga en pausa";
  if (d.st === "queued") return "En la cola de descargas";
  if (d.st === "active" && NET.internet === false && d.phase !== "install" && d.phase !== "verify") return "Esperando conexión…";
  if (d.phase === "prealloc") return "Reservando espacio en disco…";
  if (d.phase === "verify") return "Verificando archivos…";
  if (d.phase === "install") return "Instalando archivos…";
  if (d.phase === "lan") return d.peer ? `Transfiriendo desde ${d.peer}…` : "Transfiriendo por la red de la casa…";
  return d.kind === "Actualizando" ? "Descargando actualización…" : "Descargando datos…";
}
function downloadView(g) {
  return {
    cls: "dialog", isDownload: true, appid: g.appid, cols: 1, i: 1,
    hints: [["B", "Cerrar"], ["A", "Aceptar"]],
    render(el) {
      el.innerHTML = `<div class="dbox dlg"><h3 class="dlh"></h3><p class="dls">Si abres otro juego, Scarlet te preguntará si pausar la descarga.</p>
        <div class="dlp"><i class="sp"></i><b class="bar"><i></i></b><span class="pc"></span></div><div class="dle"></div><div class="dlz"></div>
        <div class="dlo"><button class="fi" data-b="opt">Opciones de descarga</button></div>
        <div class="dbtns one"><button class="fi" data-b="close">Cerrar</button></div></div>`;
      $('[data-b="close"]', el).onclick = () => closeView();
      $('[data-b="opt"]', el).onclick = () => downloadOptions(g);
      this.paint();
    },
    paint() {
      const el = this.el; if (!el) return;   // (al abrir todavía no está en pantalla: se pinta igual)
      const d = state.downloads[g.appid];
      $(".dlh", el).textContent = dlTitle(d);
      const p = d ? d.p : 1, pending = !d || d.st === "pending";
      $(".bar i", el).style.width = (p * 100).toFixed(1) + "%";
      $(".pc", el).textContent = pending ? "" : `${Math.floor(p * 100)} %`;
      $(".sp", el).classList.toggle("still", !d || d.st === "paused" || d.st === "queued" || d.st === "error");
      $(".dle", el).textContent = !d ? "" : d.st === "error" ? (d.errText || "Steam no pudo seguir con la descarga.") : d.st === "paused" ? "En pausa" : d.st === "queued" ? "Empieza cuando terminen las de antes"
        : pending ? "Steam está preparando el juego" : $(".dlh", el).textContent === "Esperando conexión…" ? "Sigue sola cuando vuelva el internet" : `Tiempo restante: ${fmtEta(d.eta)}`;
      $(".dlz", el).textContent = d && d.total > 0 ? `${fmtBytes(d.bytes)} de ${fmtBytes(d.total)}${d.st === "active" && fmtRate(d.rate) ? " · " + fmtRate(d.rate) : ""}` : "";
    },
    items() { return fis(this.el); }
  };
}
async function downloadOptions(g) {
  const d = state.downloads[g.appid]; if (!d) return;
  const paused = d.st === "paused" || d.st === "error", upd = !d.fresh && d.kind === "Actualizando";
  const others = Object.keys(state.downloads).filter(id => state.downloads[id].st !== "canceling" && byId(id));
  const c = await choiceDialog(`Opciones de descarga\n${g.name}`, [{ label: "Atrás", value: null }, { label: upd ? "Cancelar actualización" : "Cancelar instalación", value: "cancel" },
    ...(others.length > 1 ? [{ label: `Cancelar todas (${others.length})`, value: "all" }] : []),
    { label: d.st === "error" ? "Reintentar" : paused ? "Reanudar descarga" : "Pausar descarga", value: paused ? "resume" : "pause" }], others.length > 1 ? 3 : 2);
  if (c === "all") return cancelAllDownloads(others);
  if (c === "resume" && d.st === "error") { const r = await steamDl("download.now", g.appid); return toast(r && r.ok ? "Steam lo vuelve a intentar" : "No se pudo reintentar"); }
  if (c === "pause" || c === "resume") return setDownloadPaused(g, c === "pause");
  if (c === "cancel") return cancelDownload(g);
}
/* Cancela todas las descargas: las instalaciones nuevas se borran; las actualizaciones solo se quitan de la lista. */
async function cancelAllDownloads(ids) {
  const ok = await confirmDialog(`¿Cancelar todas las descargas (${ids.length})?\nLas instalaciones nuevas se borran; las actualizaciones se quitan de la lista y los juegos se quedan como están.`, "Cancelar todas");
  if (!ok) return;
  for (const id of ids) { const g = byId(id); if (g && state.downloads[id]) await cancelDownload(g, true); }
}
async function setDownloadPaused(g, pause) {
  const d = state.downloads[g.appid];
  const r = await steamDl(pause ? "download.pause" : "download.resume", g.appid);
  if (!r || !r.ok) return toast(`No se pudo ${pause ? "pausar" : "reanudar"} la descarga`);
  for (const id in state.downloads) if (!["pending", "error", "canceling"].includes(state.downloads[id].st)) state.downloads[id].st = pause ? "paused" : "active";
  if (d) d.st = pause ? "paused" : "active";
  play(pause ? "off" : "on"); toast(pause ? "Descarga en pausa" : "Descarga reanudada");
  renderHome(); refreshLibrary();
  const v = topView(); if (v && v.isDownload) v.paint();
}
async function cancelDownload(g, quiet) {
  if ((state.downloads[g.appid] || {}).st === "canceling") { toast(`Ya se está cancelando ${g.name}…`); return false; }
  const cur = state.downloads[g.appid], upd = !!cur && !cur.fresh && cur.kind === "Actualizando";
  if (!quiet) {
    const ok = await confirmDialog(upd ? `¿Cancelar la actualización de ${g.name}?\nSe quita de las descargas y el juego se queda como está. Steam puede pedirla otra vez para jugar.`
      : `¿Cancelar la instalación de ${g.name}?\nSe borrará lo que ya se descargó.`, upd ? "Cancelar actualización" : "Cancelar instalación");
    if (!ok) return false;
  }
  const was = state.downloads[g.appid], prevSt = was && was.st;
  if (was) { was.st = "canceling"; paintDownloads(); const v = topView(); if (v && v.isDownload && v.appid == g.appid) v.paint(); }
  toast(`Cancelando ${g.name}…`);
  const r = await steamDl("download.cancel", g.appid);
  if (!r || !r.ok) {
    if (was && state.downloads[g.appid] === was && was.st === "canceling") was.st = prevSt;
    paintDownloads();
    await alertDialog(`No se pudo cancelar la ${upd ? "actualización" : "instalación"} de ${g.name}.\n${(r && r.error) || ""}`); return false;
  }
  canceledAt[g.appid] = Date.now(); launchAfter.delete(g.appid);
  delete state.downloads[g.appid];
  if (r.update || upd) { g.installed = true; }
  else { g.installed = false; unpin(g.appid); }
  const v = views.find(x => x.isDownload && x.appid == g.appid); if (v) dropView(v);
  renderHome(); refreshLibrary(); toast(r.update || upd ? `Se canceló la actualización de ${g.name}` : `Se canceló la instalación de ${g.name}`);
  return true;
}
/* Desinstalar: Scarlet ya preguntó, así que Steam lo hace sin su ventanita. "Desinstalado" sale cuando de verdad se borró. */
const uninstalling = new Map();
async function uninstallGame(g, after) {
  if (!Host.has) { g.installed = false; renderHome(); if (after) after(); return notify({ icon: I.cloud, title: "Desinstalado", text: `${g.name} · ${fmtSize(gameSize(g))} liberados` }); }
  const size = gameSize(g);
  const r = await Host.call("game.uninstall", { appid: g.appid, source: g.source }).catch(e => ({ ok: false, error: e.message }));
  if (!r || !r.ok) return alertDialog(`No se pudo desinstalar ${g.name}.\n${(r && r.error) || ""}`);
  if (r.mode === "steam") return toast("Steam te pedirá confirmar la desinstalación");
  if (r.mode === "gone") { g.installed = false; renderHome(); refreshLibrary(); if (after) after(); return; }
  uninstalling.set(g.appid, { size, after });
  toast(`Desinstalando ${g.name}…`); renderHome(); if (after) after();
}
Events.on("game.uninstalled", d => {
  const u = uninstalling.get(d.appid), g = byId(d.appid); if (!g) return;
  if (d.pending) return notify({ key: "unin:" + g.appid, icon: I.cloud, title: "Desinstalando", text: `Steam se reinicia unos segundos para terminar de borrar ${g.name}.` });
  uninstalling.delete(d.appid);
  if (d.ok) {
    g.installed = false; delete state.downloads[g.appid]; delete state.updates[g.appid]; unpin(g.appid);
    const size = d.freed > 0 ? d.freed / 1073741824 : u ? u.size : 0;
    notify({ key: "unin:" + g.appid, icon: I.cloud, title: "Desinstalado", text: `${g.name}${size > 0 ? " · " + fmtSize(size) + " liberados" : ""}` });
  } else alertDialog(`No se pudo desinstalar ${g.name}.\nIntenta desde Steam.`);
  renderHome(); refreshLibrary(); if (u && u.after) u.after();
});

/* Actualizar ya (actualización pendiente que Steam no había puesto a bajar). */
async function updateNow(g) {
  const r = await steamDl("download.now", g.appid);
  if (!r || !r.ok) return toast(`No se pudo actualizar ${g.name}`);
  delete state.updates[g.appid];
  if (!state.downloads[g.appid]) state.downloads[g.appid] = { p: 0, kind: "Actualizando", st: "pending" };
  renderHome(); refreshLibrary(); openView(downloadView(g));
  setTimeout(() => { const d = state.downloads[g.appid]; if (d && d.st === "pending") { delete state.downloads[g.appid]; closeDlView(g.appid); renderHome(); toast(`Steam no empezó a actualizar ${g.name}`); } }, 60000);
}
const steamDl = (type, appid) => Host.has ? Host.call(type, { appid }).catch(e => ({ ok: false, error: e.message })) : wait(500).then(() => ({ ok: true }));
/* Abrir un juego con descargas en curso: pausar y jugar, cancelar y jugar, o seguir descargando (no abre el juego). */
const autoPaused = new Set();
async function askDownloadsBeforePlay(g) {
  const act = Object.keys(state.downloads).filter(id => id != g.appid && ["active", "pending", "queued"].includes(state.downloads[id].st)).map(id => byId(id)).filter(Boolean);
  // (las actualizaciones pendientes y lo que Steam hace después de instalar no cuentan: no están bajando)
  if (!act.length) return true;
  const one = act.length === 1 ? act[0] : null;
  const opts = [{ label: "Seguir descargando", value: "keep" }, ...(one ? [{ label: "Cancelar y jugar", value: "cancel" }] : []), { label: "Pausar y jugar", value: "pause" }];
  const c = await choiceDialog(`${one ? one.name + " se está descargando" : `Se están descargando ${act.length} juegos`}.\n¿Qué hacemos con la descarga para abrir ${g.name}?`, opts, opts.length - 1);
  if (!c || c === "keep") return false;   // sigue la descarga y el juego no se abre
  if (c === "cancel") return cancelDownload(one);
  const r = await steamDl("download.pause", one ? one.appid : act[0].appid);
  if (!r || !r.ok) { toast("No se pudo pausar la descarga"); return false; }
  act.forEach(x => { autoPaused.add(x.appid); state.downloads[x.appid].st = "paused"; });
  renderHome();
  return true;
}
/* Al terminar de jugar, lo que se pausó para jugar sigue solo. */
function resumeAfterPlay() {
  if (!autoPaused.size) return;
  const ids = [...autoPaused]; autoPaused.clear();
  setTimeout(() => {
    steamDl("download.resume", ids[0]).then(r => {
      if (!r || !r.ok) return toast("No se pudo reanudar la descarga");
      ids.forEach(id => { if (state.downloads[id]) state.downloads[id].st = "active"; });
      renderHome(); notify({ icon: I.cloud, title: "Descarga reanudada", text: ids.map(id => (byId(id) || {}).name).filter(Boolean).join(", ") });
    });
  }, 1500);
}

