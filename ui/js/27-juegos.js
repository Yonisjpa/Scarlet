/* ================= juegos: iniciar, reanudar, volver al menú ================= */
function echo(el) {
  const r = el.getBoundingClientRect(), br = getComputedStyle(el).borderRadius;
  for (let k = 0; k < 2; k++) {
    const e = document.createElement("div"); e.className = "echo";
    Object.assign(e.style, { left: r.left + "px", top: r.top + "px", width: r.width + "px", height: r.height + "px", borderRadius: br, animationDelay: k * 160 + "ms" });
    document.body.append(e); setTimeout(() => e.remove(), 1100);
  }
  el.animate([{ scale: "1" }, { scale: "1.05" }, { scale: "1" }], { duration: 380, easing: "cubic-bezier(.2,.8,.2,1)" });
}
function waitReady(g) {
  return new Promise(res => {
    if (!Host.has) return setTimeout(() => res(true), 3200 + Math.random() * 1400);
    let t;
    const off = Events.on("game.ready", d => { if (!d || d.appid == g.appid) { delete goneAt[g.appid]; clearTimeout(t); off(); off2(); res(true); } });
    const off2 = Events.on("game.failed", d => { if (!d || d.appid == g.appid) { clearTimeout(t); off(); off2(); off3(); res(false); } });
    // Steam se puso a actualizarlo antes de abrirlo (actualización obligatoria): se muestra la descarga.
    const off3 = Events.on("game.updating", d => { if (d && d.appid == g.appid) { clearTimeout(t); off(); off2(); off3(); res("updating"); } });
    t = setTimeout(() => { off(); off2(); off3(); res(false); }, 90000);
  });
}
function showGame(g, sub) {
  const gm = $("#game"); if (g.appid < 990000 || g.appid > 990999) Art.apply($("img", gm), g, "hero"); else $("img", gm).removeAttribute("src"); $("#gname").textContent = g.name;
  $(".ginfo span", gm).textContent = sub || (g.kind === "app" ? "Aplicación abierta · simulación del prototipo" : "Juego en ejecución · simulación del prototipo");
  gm.className = "show";
}
/* apps del sistema: la tienda y el chat de Steam se abren en Big Picture; Explorador y Ajustes como ventanas a pantalla completa */
const SYS = {
  store: { appid: 990001, name: "Tienda de Steam", sub: "Abierta en Steam Big Picture", url: "steam://store" },
  news: { appid: 990002, name: "Noticias de Steam", sub: "Abiertas en Steam Big Picture", url: "steam://open/news" },
  chat: { appid: 990003, name: "Amigos y chat de Steam", sub: "Abierto en Steam Big Picture", url: "steam://open/friends" },
  explorer: { appid: 990004, name: "Explorador de archivos", sub: "Ventana de Windows a pantalla completa", url: "explorer" },
  winsettings: { appid: 990005, name: "Ajustes de Windows", sub: "Ventana de Windows a pantalla completa", url: "ms-settings:" },
  bluetooth: { appid: 990006, name: "Bluetooth de Windows", sub: "Ajustes de Bluetooth y dispositivos", url: "ms-settings:bluetooth" },
  armoury: { appid: 990007, name: "Armoury Crate", sub: "Panel de ASUS: controles, energía y pantalla", url: "armoury" }
};
async function openSystemApp(o, el) {
  if (mode !== "ui") return;
  mode = "launching"; play("select");
  if (el && el.isConnected) echo(el);
  await wait(280);
  const L = $("#launch"); L.classList.add("show"); await wait(380);
  (o.url === "armoury" ? Host.call("system.armoury") : Host.call("system.open", { uri: o.url })).catch(() => {});
  state.overlay = o; showGame(o, o.sub + (Host.has ? "" : " · simulación del prototipo"));
  L.classList.remove("show", "cwait"); await wait(380);
  mode = "game";
}
async function launchGame(g, el, opt = {}) {
  if (mode !== "ui") return;
  if (state.running === g.appid) return resumeGame(el);
  const d = state.downloads[g.appid];
  if (d) return openView(downloadView(g), el);
  if (uninstalling.has(g.appid)) return toast(`Se está desinstalando ${g.name}…`);
  if (!g.installed) return startInstall(g);
  if (state.updates[g.appid] && !opt.updAsked) {   // Steam tiene una actualización pendiente: la pedirá para jugar
    const c = await choiceDialog(`${g.name} tiene una actualización pendiente.\nSteam la pide para jugar.`,
      [{ label: "Atrás", value: null }, { label: "Jugar así", value: "play" }, { label: "Actualizar ahora", value: "now" }], 2);
    if (!c) return;
    if (c === "now") return updateNow(g);
    opt = Object.assign({}, opt, { updAsked: true });   // si Steam la exige al abrir, se ve como descarga y abre al terminar
  }
  if (g.kind !== "app" && !opt.dlAsked) { const go = await askDownloadsBeforePlay(g); if (!go) return; }
  if (state.running) {
    const r = runningGame();
    // Con un juego abierto, las apps (Apple Music, YouTube para ver una guía…) se abren junto a él: el juego se queda
    // congelado y callado detrás, con su "Jugando", y regresas a él desde el Inicio.
    if (r && r.kind !== "app" && g.kind === "app") return openAlongside(g, el);
    if (r && r.kind === "app") {   // música, YouTube…: se puede dejar sonando mientras juegas
      const v = await choiceDialog(`${r.name} está abierta.\n¿La cierras o la dejas en segundo plano (por ejemplo, para seguir oyendo música)?`,
        [{ label: "Cancelar", value: null }, { label: "Dejar abierta", value: "keep" }, { label: "Cerrar y abrir", value: "close" }], 1);
      if (!v) return;
      if (v === "keep") { Host.call("game.detach").catch(() => {}); state.running = null; renderHome(); }
      else closeRunning(true);
      el = null;
    } else {
      const ok = await confirmDialog(`Se cerrará ${r.name}.\nSe perderá el progreso que no hayas guardado.`, "Cerrar y abrir");
      if (!ok) return;
      closeRunning(true); el = null;
    }
  }
  mode = "launching"; play("launch");
  if (el && el.isConnected) echo(el);
  await wait(450);
  checkAtmos();   // el aviso de Dolby en la pantalla de carga, solo si la salida usa Atmos
  const L = $("#launch"); L.classList.add("show");
  await wait(420);
  closeAllViews(); L.classList.add("logo");
  if (!opt.joined) Data.launch(g);
  const ok = await waitReady(g);
  if (ok === "updating") {
    L.classList.remove("logo", "show"); mode = "ui";
    launchAfter.add(g.appid);
    if (!state.downloads[g.appid]) state.downloads[g.appid] = { p: 0, kind: "Actualizando", st: "pending" };
    renderHome(); openView(downloadView(g));
    notify({ icon: I.cloud, title: "Primero se actualiza", text: `Steam está actualizando ${g.name}. Se abrirá cuando termine.` });
    return;
  }
  if (!ok) {
    L.classList.remove("logo", "show"); mode = "ui";
    notify({ icon: I.power, title: "No se pudo iniciar", text: g.name, sound: "error" });
    return;
  }
  games.splice(games.indexOf(g), 1); games.unshift(g); g.last = "ahora"; g.lastTs = Math.floor(Date.now() / 1000);
  state.running = g.appid; home.zone = "games"; home.g = 0; home.offset = 0;
  renderHome(); showGame(g);
  L.classList.remove("logo"); await wait(250);
  L.classList.remove("show", "cwait"); await wait(400);
  mode = "game";
  if (!Host.has && hasAch(g) && g.ach[0] < g.ach[1]) setTimeout(() => {   // logro de demostración
    if (state.running !== g.appid) return;
    const a = mockAch(g).list.find(x => !x.unlocked); g.ach[0]++;
    notifyAchievement(g, a ? a.name : "Nuevo logro");
  }, 9000);
}
async function openAlongside(g, el) {
  mode = "launching"; play("launch");
  if (el && el.isConnected) echo(el);
  await wait(300);
  state.side = g.appid; renderHome();
  try { await Host.call("app.open", { appid: g.appid, source: g.source }); }
  catch (e) { state.side = null; renderHome(); notify({ icon: I.power, title: "No se pudo abrir", text: g.name, sound: "error" }); }
  mode = "ui"; updateFocus();
}
Events.on("app.closed", d => { if (d && d.appid == state.side) { state.side = null; if (mode !== "boot") renderHome(); } });
async function resumeGame(el) {
  const g = runningGame(); if (!g || mode !== "ui") return;
  mode = "launching"; play("select");
  if (el && el.isConnected) echo(el);
  await wait(260);
  const L = $("#launch"); L.classList.add("show"); await wait(380);
  closeAllViews();
  Host.call("game.focus", { appid: g.appid }).catch(() => {});
  showGame(g);
  L.classList.remove("show", "cwait"); await wait(380);
  mode = "game";
}
function goHome() {
  if (mode !== "game") return;
  const fromOverlay = !!state.overlay; state.overlay = null;
  const gm = $("#game"); gm.className = "hide"; setTimeout(() => { if (gm.className === "hide") gm.className = ""; }, 340);
  Host.call("ui.show").catch(() => {});
  if (!fromOverlay) { home.zone = "games"; home.g = 0; home.offset = 0; }
  mode = "ui"; renderHome(); play("home");
  syncRunning();
}
/* Pregunta a la app qué juego sigue abierto: si la interfaz lo había "olvidado", vuelve a mostrar su "Jugando" (y al
   entrar se reanuda en vez de lanzarse otra vez). */
const goneAt = {};   // juegos que se acaban de cerrar: una respuesta atrasada no debe "revivirlos"
function syncRunning() {
  if (!Host.has) return;
  Host.call("game.state").then(gs => {
    // Solo cuenta como «Jugando» cuando ya está en pantalla (mientras abre, o mientras espera la nube, todavía no).
    if (gs && gs.appid && gs.ready !== false && mode !== "launching" && gs.appid !== state.running && byId(gs.appid) && !(Date.now() - (goneAt[gs.appid] || 0) < 8000)) { state.running = gs.appid; if (mode !== "boot") renderHome(); }
  }).catch(() => {});
}
function closeRunning(silent) {
  const g = runningGame(); if (!g) return;
  resumeAfterPlay();
  goneAt[g.appid] = Date.now();
  Host.call("game.close", { appid: g.appid }).catch(() => {});
  state.running = null; renderHome();
  if (!silent) toast(`Se cerró ${g.name}`);
}
/* El juego dejó de responder: esperar o cerrarlo (en vez de quedarte viendo la pantalla negra). */
Events.on("game.hung", async d => {
  if (OVERLAY) return;
  if (QM.open) closeQuick(true);
  if (mode === "game") goHome();
  await wait(400);
  const name = (d && d.name) || "El juego";
  const v = await choiceDialog(`${name} no responde.\n¿Esperas un poco más o lo cierras?`, [{ label: "Esperar", value: "wait" }, { label: "Cerrar juego", value: "kill" }], 0);
  if (v === "kill") { Host.call("game.kill").catch(() => {}); toast(`Se cerró ${name}`); }
  else { Host.call("game.wait").catch(() => {}); const g = runningGame(); if (g) { showGame(g); mode = "game"; } }
});
Events.on("game.exited", d => {
  resumeAfterPlay();
  if (d && d.appid) goneAt[d.appid] = Date.now();
  if (!state.running || (d && d.appid != state.running)) return;
  state.running = null;      // ya se cerró solo: no hace falta pedir a Windows que lo cierre
  if (mode === "game") goHome(); else renderHome();
});
let lastGuide = 0, guideSingleT = 0;
function guidePress() {   // (teclado / demo) 1 toque = menú rápido · 2 toques = menú / juego
  const now = performance.now();
  if (now - lastGuide < 330) { lastGuide = 0; clearTimeout(guideSingleT); onGuideDouble(); }
  else { lastGuide = now; guideSingleT = setTimeout(() => { lastGuide = 0; if (!QM.open && notifAction()) return; QM.open ? closeQuick() : openQuick(); }, 330); }
}
function onGuideDouble() {
  if (mode === "game") goHome();
  else if (mode === "ui" && state.running) resumeGame();
  else if (mode === "ui") bump(homeEl());
}
// La app avisa "hidden" cuando tuvo que traer el menú al frente (venías de un juego u otra ventana): ahí siempre se va al menú.
Events.on("guide.double", d => {
  setTimeout(flushExclusive, 900);
  if (!(d && d.hidden)) return onGuideDouble();
  if (mode === "game") goHome(); else if (mode === "ui") { play("select"); updateFocus(); }
});

