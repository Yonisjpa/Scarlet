/* ================= beta 1.4: pantalla táctil =================
   - Las ayudas de abajo (y las del menú rápido) se tocan: «B Atrás», «A Aceptar», X, Y, ☰, LB/RB, LT/RT.
   - La fila del Inicio se desliza con el dedo (con inercia); al soltar, se elige el juego más cercano.
   - Deslizar desde la orilla izquierda regresa (como B), con una flechita que sigue al dedo.
   - Toque largo: en un juego abre sus opciones (como X); en el Álbum empieza a elegir capturas.
   Las listas (biblioteca, Álbum, Ajustes…) ya se deslizan solas: son listas normales de la página.
   Solo cuenta el dedo (y la pluma): el mouse sigue como antes. */
const TOUCH = { EDGE: 28, BACK: 90, LONG: 520, SLOP: 10 };
const isFinger = e => e.pointerType === "touch" || e.pointerType === "pen";
let touchSwallowClick = 0;   // tras deslizar o un toque largo, el «click» que manda el navegador no cuenta

/* Ayudas tocables: cualquier cosa con data-key manda ese botón. */
document.addEventListener("click", e => {
  const t = e.target.closest("[data-key]");
  if (!t || Date.now() < touchSwallowClick) return;
  e.stopPropagation(); e.preventDefault();
  t.classList.add("tap"); setTimeout(() => t.classList.remove("tap"), 160);
  if (touchExit(t.dataset.key)) return;
  input(t.dataset.key);
});
/* (1.8) Pantallas donde B es un botón más (Probar botones: hay que mantenerlo): con el dedo, «Atrás» sí sale. */
function touchExit(key) { const v = topView(); if (key === "b" && v && v.touchExit) { closeView(); return true; } return false; }
/* Un click justo después de deslizar o de un toque largo no debe abrir nada. */
document.addEventListener("click", e => { if (Date.now() < touchSwallowClick) { e.stopPropagation(); e.preventDefault(); } }, true);

/* ----- deslizar la fila del Inicio ----- */
const rowDrag = { on: false };
function rowDragStart(e) {
  if (!isFinger(e) || mode !== "ui" || views.length || QM.open) return;
  rowDrag.on = true; rowDrag.moved = false; rowDrag.x0 = e.clientX; rowDrag.y0 = e.clientY; rowDrag.off0 = home.offset || 0;
  rowDrag.samples = [[performance.now(), e.clientX]];
}
function rowDragMove(e) {
  if (!rowDrag.on) return;
  const dx = e.clientX - rowDrag.x0, dy = e.clientY - rowDrag.y0;
  if (!rowDrag.moved) {
    if (Math.abs(dx) < TOUCH.SLOP) return;
    if (Math.abs(dy) > Math.abs(dx)) { rowDrag.on = false; return; }   // gesto vertical: no es para la fila
    rowDrag.moved = true; $("#track").classList.add("dragging");
  }
  const max = Math.max(0, $("#track").scrollWidth - $("#rowwrap").clientWidth + rem() * 4.6);
  let x = rowDrag.off0 - dx;
  if (x < 0) x = x / 3; else if (x > max) x = max + (x - max) / 3;   // resistencia en las orillas
  rowDrag.x = x;
  $("#track").style.transform = `translateX(${-x}px)`;
  rowDrag.samples.push([performance.now(), e.clientX]); if (rowDrag.samples.length > 6) rowDrag.samples.shift();
}
function rowDragEnd() {
  if (!rowDrag.on) return;
  rowDrag.on = false;
  if (!rowDrag.moved) return;
  touchSwallowClick = Date.now() + 350;
  $("#track").classList.remove("dragging");
  // inercia: la velocidad de los últimos movimientos lleva la fila un poco más
  const s = rowDrag.samples, a = s[0], b = s[s.length - 1], dt = Math.max(1, b[0] - a[0]);
  const v = (b[1] - a[1]) / dt;                       // px por ms (positivo = hacia la derecha)
  const target = rowDrag.x - v * 220;
  // se elige el juego cuyo centro quede más cerca del lugar donde suele estar el elegido (a la izquierda)
  const tiles = homeTiles(); if (!tiles.length) return;
  const anchor = target + rem() * 4.6 + tiles[0].offsetWidth / 2;
  let best = 0, bd = Infinity;
  tiles.forEach((t, i) => { const c = t.offsetLeft + t.offsetWidth / 2, d = Math.abs(c - anchor); if (d < bd) { bd = d; best = i; } });
  home.zone = "games"; home.g = best;
  home.offset = Math.max(0, Math.min(target, Math.max(0, $("#track").scrollWidth - $("#rowwrap").clientWidth)));
  play("move"); updateFocus();
}

/* ----- orilla izquierda = Atrás ----- */
const edge = { on: false };
function edgeStart(e) {
  if (!isFinger(e) || e.clientX > TOUCH.EDGE || mode !== "ui") return false;
  if (!views.length && !QM.open) return false;          // en el Inicio no hay a dónde regresar
  edge.on = true; edge.x0 = e.clientX; edge.y0 = e.clientY;
  let ind = $("#edgeback");
  if (!ind) { ind = document.createElement("div"); ind.id = "edgeback"; ind.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="M14.5 6 8.5 12l6 6"/></svg>'; document.body.append(ind); }
  ind.style.top = e.clientY + "px"; ind.style.setProperty("--p", 0); ind.className = "on";
  return true;
}
function edgeMove(e) {
  if (!edge.on) return;
  const dx = Math.max(0, e.clientX - edge.x0), p = Math.min(1, dx / TOUCH.BACK);
  const ind = $("#edgeback"); ind.style.setProperty("--p", p); ind.classList.toggle("ready", p >= 1);
  ind.style.top = e.clientY + "px";
}
function edgeEnd(e) {
  if (!edge.on) return;
  edge.on = false;
  const ind = $("#edgeback"), go = ind.classList.contains("ready") && Math.abs(e.clientY - edge.y0) < 160;
  ind.className = "";
  if (go) { touchSwallowClick = Date.now() + 350; if (!touchExit("b")) input("b"); }
}

/* ----- toque largo ----- */
const longp = { t: 0 };
function longStart(e) {
  if (!isFinger(e) || mode !== "ui" || QM.open) return;
  clearTimeout(longp.t);
  longp.x = e.clientX; longp.y = e.clientY; longp.el = e.target;
  longp.t = setTimeout(() => longFire(longp.el), TOUCH.LONG);
}
function longCancel(e) { if (e && e.type === "pointermove" && Math.hypot(e.clientX - longp.x, e.clientY - longp.y) < TOUCH.SLOP) return; clearTimeout(longp.t); }
function longFire(target) {
  const v = topView();
  // un juego del Inicio: lo elige y abre sus opciones (como X)
  const tile = !v && target.closest("#track [data-z='g']");
  if (tile) {
    const i = +tile.dataset.i; if (i >= homeGames().length) return;
    home.zone = "games"; home.g = i; updateFocus();
    touchSwallowClick = Date.now() + 600; rumbleTap(); optionsKey(); return;
  }
  if (!v) return;
  const items = v.items(), k = items.findIndex(x => x === target || x.contains(target));
  if (k < 0) return;
  // Álbum: empieza a elegir capturas con esa (como ☰)
  if (v.isAlbum) {
    v.i = k; updateFocus(); touchSwallowClick = Date.now() + 600; rumbleTap();
    if (!v.sel) v.onKey("start"); return;
  }
  // Biblioteca u otra lista con opciones (X)
  if (v.onX) { v.i = k; updateFocus(); touchSwallowClick = Date.now() + 600; rumbleTap(); v.onX(); }
}
const rumbleTap = () => rumble("tick");

addEventListener("pointerdown", e => {
  if (!isFinger(e)) return;
  if (edgeStart(e)) return;
  if (e.target.closest("#rowwrap")) rowDragStart(e);
  longStart(e);
}, true);
addEventListener("pointermove", e => { if (!isFinger(e)) return; edgeMove(e); rowDragMove(e); longCancel(e); if (rowDrag.moved) clearTimeout(longp.t); }, true);
addEventListener("pointerup", e => { if (!isFinger(e)) return; edgeEnd(e); rowDragEnd(e); clearTimeout(longp.t); }, true);
addEventListener("pointercancel", e => { edge.on = false; const i = $("#edgeback"); if (i) i.className = ""; rowDrag.on = false; $("#track").classList.remove("dragging"); clearTimeout(longp.t); }, true);
/* Sin el menú del navegador al dejar el dedo puesto (el toque largo es nuestro). */
addEventListener("contextmenu", e => { if (e.pointerType === "touch" || longp.el) e.preventDefault(); });
