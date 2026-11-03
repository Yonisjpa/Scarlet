/* ================= alpha 0.4: reproduciendo ahora (Apple Music y cualquier app que lo reporte a Windows) ================= */
const MI = {
  shuffle: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M16 4h4v4M4 20 20 4M20 16v4h-4M14.5 14.5 20 20M4 4l5 5"/></svg>',
  prev: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M6 5h2.2v14H6zM20 5.5v13L9.5 12z"/></svg>',
  next: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M15.8 5H18v14h-2.2zM4 5.5v13L14.5 12z"/></svg>',
  play: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M7 4.5v15l12.5-7.5z"/></svg>',
  pause: '<svg viewBox="0 0 24 24" fill="currentColor"><rect x="5.5" y="4.5" width="4.5" height="15" rx="1"/><rect x="14" y="4.5" width="4.5" height="15" rx="1"/></svg>'
};
let media = null, mediaTimer = 0;
/* Si la app de música no acepta el botón, el menú vuelve a mostrar cómo está de verdad (no se queda en "pausa" falsa). */
const mediaCmd = (cmd, value) => Host.has ? Host.call("media.cmd", { cmd, value: value || 0 }).then(ok => { if (ok === false) return Host.call("media.state").then(m => setMedia(m)); }).catch(() => {}) : null;
/* Adelantar o regresar la canción (tocando la barra, o LB / RB con la tarjeta de música elegida). */
function mediaSeek(sec) {
  if (!media || !media.duration) return play("bump");
  const t = clamp(sec, 0, media.duration - 1);
  media.position = t; media.at = Date.now(); play("tick");
  mediaCmd("seek", t); renderQuick();
}
function mediaButtons() {
  const m = media || {};
  return [
    { ok: !!m.canShuffle, cls: m.shuffle ? "on" : "", icon: MI.shuffle, run() { m.shuffle = !m.shuffle; mediaCmd("shuffle"); renderQuick(); } },
    { ok: m.canPrev !== false, cls: "", icon: MI.prev, run() { mediaCmd("prev"); } },
    { ok: true, cls: "big", icon: m.playing ? MI.pause : MI.play, run() { m.position = mediaPos(); m.at = Date.now(); m.playing = !m.playing; mediaCmd("toggle"); renderQuick(); } },
    { ok: m.canNext !== false, cls: "", icon: MI.next, run() { mediaCmd("next"); } }
  ];
}
/* La app manda la posición y cuándo la midió; mientras suena se avanza sola para que la barra se vea fluida. */
function mediaPos() {
  const m = media; if (!m) return 0;
  const p = m.position + (m.playing ? Math.max(0, Date.now() - (m.at || Date.now())) / 1000 : 0);
  return m.duration ? Math.min(p, m.duration) : p;
}
const mmss = s => !isFinite(s) || s < 0 ? "0:00" : `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, "0")}`;
function mediaCard(f) {
  const m = media, p = mediaPos(), pct = m.duration ? p / m.duration * 100 : 0;
  const btns = mediaButtons().map((b, j) => `<span data-m="${j}" class="qmb ${b.cls}${b.ok ? "" : " off"}${QM.m === j ? " sel" : ""}">${b.icon}</span>`).join("");
  return `<div class="qrow qmedia${f}"><div class="qmtop"><span class="qmart" style="${m.art ? `background-image:url('${esc(m.art)}')` : ""}"></span>
    <span class="qmtxt"><b>${esc(m.title)}</b><span>${esc(m.artist || m.album || "")}</span><em>${esc(m.app || "")}</em></span></div>
    ${m.duration ? `<div class="qmprog"><span class="mcur">${mmss(p)}</span><span class="qbar"><i style="width:${pct}%"></i></span><span>${mmss(m.duration)}</span></div>` : ""}
    <div class="qmbtns">${btns}</div></div>`;
}
/* Solo se mueve la barra (sin volver a dibujar todo el menú). */
function mediaTick() {
  clearInterval(mediaTimer);
  mediaTimer = setInterval(() => {
    if (!QM.open) return clearInterval(mediaTimer);
    if (!media || !media.duration) return;
    const p = mediaPos(), bar = $("#quick .qmprog .qbar i"), cur = $("#quick .qmprog .mcur");
    if (bar) bar.style.width = p / media.duration * 100 + "%";
    if (cur) cur.textContent = mmss(p);
  }, 500);
}
function paintNowPlaying() {
  const c = $("#npchip"); if (!c || OVERLAY) return;
  if (!media) { c.hidden = true; return; }
  c.hidden = false; c.classList.toggle("paused", !media.playing);
  $(".npart", c).style.backgroundImage = media.art ? `url("${media.art}")` : "";
  $("b", c).textContent = media.title + (media.artist ? " · " + media.artist.split(" — ")[0] : "");
  c.onclick = () => { if (mode === "ui" && !QM.open) openQuick(); };
}
function setMedia(m) {
  if (m && !m.title) m = null;
  if (m && m.now) m.at = (m.at || m.now) + (Date.now() - m.now);   // reloj de la app → reloj de la página
  const had = !!media; media = m;
  // Windows avisa varias veces por segundo (avance de la canción): solo se repinta si cambió algo que se ve.
  // Antes cada aviso rehacía el menú y volvía a pedir la carátula.
  const look = m ? [m.app, m.title, m.artist, m.art, m.playing, m.shuffle, m.canShuffle, m.canNext, m.canPrev, m.canSeek, Math.round(m.duration || 0)].join("|") : "";
  const same = look === setMedia.look && had === !!m; setMedia.look = look;
  if (same) return;
  if (QM.open && had !== !!m) QM.i = clamp(QM.i + (m ? 1 : -1), 0, qmRows().length - 1);
  if (QM.open) renderQuick();
  tintQuick();
  paintNowPlaying();
  // el mosaico de la app de música: "Sonando" / "En pausa"
  const sig = m ? `${m.app}|${m.playing}` : "";
  if (!OVERLAY && sig !== setMedia.sig && typeof renderHome === "function" && mode !== "boot") { setMedia.sig = sig; renderHome(); const lv = views.find(v => v.cols === 6 && v.refresh); if (lv) lv.refresh(true); }
}
Events.on("media.changed", setMedia);
function demoMedia() {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><rect width="100" height="100" fill="#0b0b0d"/><circle cx="62" cy="40" r="34" fill="#c4161c"/><rect y="70" width="100" height="30" fill="#7a0d12"/></svg>`;
  setMedia({ app: "Apple Music", title: "Canción de ejemplo", artist: "Artista · Álbum", art: "data:image/svg+xml;base64," + btoa(svg), playing: true, position: 42, duration: 215, at: Date.now(), canShuffle: true, shuffle: false });
}

/* Fondo transparente: en la interfaz con CSS; encima del juego la app hace el fondo (ver QuickOverlay). */
function applyGlass(tell = true) {
  const on = settings.quickGlass !== false;
  document.documentElement.classList.toggle("qglass", on);
  if (tell && Host.has && !OVERLAY) Host.call("quick.glass", { on }).catch(() => {});
}
Events.on("quick.glass", on => document.documentElement.classList.toggle("hostglass", !!on));
/* Encima del juego, el fondo de la app lleva los mismos colores de la carátula. */
function tellTint(cols) { if (OVERLAY && Host.has) Host.call("quick.tint", { colors: cols || null, light: document.documentElement.dataset.theme === "light" }).catch(() => {}); }
/* El menú se tiñe con los colores de la carátula (degradado que se mueve despacio, oscurecido para que se lea bien). */
let tintFor = null;
function tintQuick() {
  const q = $("#quick"), art = media && media.art;
  if (!art) { q.classList.remove("tinted"); if (tintFor !== null || !tellTint.sent) { tellTint.sent = true; tellTint(null); } tintFor = null; return; }
  if (art === tintFor) return;
  tintFor = art;
  const img = new Image(); img.crossOrigin = "anonymous";
  img.onload = () => {
    if (tintFor !== art) return;
    const cols = artColors(img);
    if (!cols) { tellTint(null); return q.classList.remove("tinted"); }
    cols.forEach((c, i) => q.style.setProperty(`--mt${i + 1}`, c));
    q.classList.add("tinted"); tellTint(cols);
  };
  img.onerror = () => q.classList.remove("tinted");
  img.src = art;
}
/* Los 3 colores con más presencia (pesando más los vivos), llevados a un tono oscuro. */
function artColors(img) {
  try {
    const c = document.createElement("canvas"); c.width = c.height = 40;
    const x = c.getContext("2d", { willReadFrequently: true }); x.drawImage(img, 0, 0, 40, 40);
    const d = x.getImageData(0, 0, 40, 40).data, bins = new Map();
    for (let i = 0; i < d.length; i += 4) {
      const r = d[i], g = d[i + 1], b = d[i + 2], key = (r >> 5) << 6 | (g >> 5) << 3 | (b >> 5);
      const mx = Math.max(r, g, b), mn = Math.min(r, g, b), sat = mx ? (mx - mn) / mx : 0;
      const e = bins.get(key) || { r: 0, g: 0, b: 0, n: 0, w: 0 };
      e.r += r; e.g += g; e.b += b; e.n++; e.w += 0.35 + sat * (mx / 255);
      bins.set(key, e);
    }
    const top = [...bins.values()].sort((a, b) => b.w - a.w), pick = [];
    for (const e of top) {
      const col = [e.r / e.n, e.g / e.n, e.b / e.n];
      if (pick.every(p => Math.hypot(p[0] - col[0], p[1] - col[1], p[2] - col[2]) > 60)) pick.push(col);
      if (pick.length === 3) break;
    }
    while (pick.length < 3) pick.push(pick[0] || [40, 40, 40]);
    // Oscurecidos parejo (se conserva qué color es más claro u oscuro) y con tope, para que el texto blanco siempre se lea.
    return pick.map(([r, g, b]) => {
      // más saturado y más brillante que antes, con tope para que el texto blanco siga leyéndose
      const avg = (r + g + b) / 3, sat = 1.35;
      [r, g, b] = [r, g, b].map(c => clamp(avg + (c - avg) * sat, 0, 255));
      const k = Math.min(0.85, 175 / Math.max(r, g, b, 1));
      return `rgb(${Math.round(r * k)},${Math.round(g * k)},${Math.round(b * k)})`;
    });
  } catch { return null; }
}

/* botón Xbox: 1 toque = menú rápido · 2 toques = menú / juego · mantener = ventanas activas (en la consola) */
let guideTimer = 0, guideHeldFired = false;
function guideDown() {
  if (guideTimer) return;
  guideHeldFired = false;
  guideTimer = setTimeout(() => { guideHeldFired = true; if (QM.open) closeQuick(true); toast("Ventanas activas: en la consola se abre el selector del modo Xbox"); }, 550);
}
function guideUp() {
  clearTimeout(guideTimer); guideTimer = 0;
  if (guideHeldFired) return;
  if (QM.open) return closeQuick();
  input("guide");
}
Events.on("guide.hold", d => { if (QM.open) { if (!(d && d.hidden)) closeQuick(); } else openQuick(); });
/* botón Xbox (app): 1 toque = menú rápido */
Events.on("quick.toggle", () => { if (!QM.open && notifAction()) return; QM.open ? closeQuick() : openQuick(); });
Events.on("quick.open", () => {
  lastActivity = performance.now(); dimmed = false; mode = "ui"; QM.open = false;
  // Cerrado, esta página estuvo congelada (beta 1.2): lo que cambió mientras tanto (la canción) se pide al abrir.
  if (OVERLAY && Host.has) Host.call("media.state").then(m => { if (QM.open) setMedia(m); }).catch(() => {});
  openQuick();
});
Events.on("quick.close", () => { if (QM.open) closeQuick(false, "botón Xbox"); else Host.call("quick.close", { why: "botón Xbox" }).catch(() => {}); });
/* Botones que manda la app cuando el juego se quedó con el foco: así el menú siempre se puede usar y cerrar. */
Events.on("pad.key", k => { if (OVERLAY && QM.open && typeof k === "string" && QM.src !== "pad") { QM.src = "host"; lastActivity = performance.now(); input(k); } });
/* El juego está en pantalla completa exclusiva: ahí el menú puede fallar. Encima del juego no se alcanza a ver, así que se avisa al volver al Inicio. */
let exclusiveWarn = null;
const flushExclusive = async () => {
  if (!exclusiveWarn) return;
  if (mode !== "ui" || views.length) return setTimeout(flushExclusive, 2000);   // en cuanto estés en el Inicio
  const g = byId(exclusiveWarn); exclusiveWarn = null; if (!g) return;
  settings.exclusiveAsked = Object.assign({}, settings.exclusiveAsked, { [g.appid]: 1 }); saveSettings();   // se pregunta una sola vez por juego
  const v = await choiceDialog(`${g.name} está en pantalla completa exclusiva: ahí el menú rápido no se puede dibujar encima.\n¿Lo hago compatible?`, [{ label: "Ahora no", value: null }, { label: "Hacer compatible", value: "fix" }], 1);
  if (v === "fix") compatFix(g);
};
Events.on("game.exclusive", d => { if (OVERLAY || !d || (settings.exclusiveAsked && settings.exclusiveAsked[d.appid])) return; exclusiveWarn = d.appid; setTimeout(flushExclusive, 900); });
/* Compatibilidad de pantalla: opciones de Windows + ventana sin bordes forzada + menú en la pantalla de Scarlet. */
async function compatFix(g) {
  const r = await Host.call("compat.fix", { appid: g.appid, borderless: true }).catch(e => ({ message: "No se pudo: " + e.message }));
  g.borderless = true;
  const v = await choiceDialog(`${(r && r.message) || ""}\n\nAhora pon ${g.name} en «ventana» en sus opciones de pantalla: Scarlet le quitará los bordes y lo estirará a toda la pantalla.\nSi el juego solo tiene pantalla completa, elige la otra opción: el menú se abrirá en la pantalla de Scarlet.`,
    [{ label: "Listo", value: null }, { label: "Solo tiene pantalla completa", value: "full" }], 0);
  if (v === "full") { g.fullMenu = true; Host.call("compat.set", { appid: g.appid, what: "fullMenu", on: true }).catch(() => {}); toast("El menú se abrirá en la pantalla de Scarlet"); }
}
async function compatMenu(g) {
  const v = await choiceDialog(`Compatibilidad de pantalla · ${g.name}`, [
    { label: g.borderless ? "Quitar «sin bordes»" : "Forzar sin bordes", value: "b" },
    { label: g.fullMenu ? "Menú encima del juego" : "Menú en Scarlet", value: "m" },
    { label: "Revisar Windows", value: "w" }], 0);
  if (v === "b") { g.borderless = !g.borderless; Host.call("compat.set", { appid: g.appid, what: "borderless", on: g.borderless }).catch(() => {}); toast(g.borderless ? "Se le quitarán los bordes al abrirlo" : "Ya no se le quitan los bordes"); }
  else if (v === "m") { g.fullMenu = !g.fullMenu; Host.call("compat.set", { appid: g.appid, what: "fullMenu", on: g.fullMenu }).catch(() => {}); toast(g.fullMenu ? "El menú se abrirá en la pantalla de Scarlet" : "El menú se abrirá encima del juego"); }
  else if (v === "w") { const r = await Host.call("compat.fix", { appid: g.appid, borderless: false }).catch(e => ({ message: "No se pudo: " + e.message })); toast((r && r.message || "").split("\n")[0]); }
}
Events.on("ui.settings", () => {
  if (mode === "game") goHome();
  setTimeout(() => { if (mode === "ui" && !(topView() && topView().isSettings)) openView(settingsView()); }, 380);
});
Events.on("ui.sleep", () => { if (mode === "game") goHome(); setTimeout(powerMenu, 380); });

