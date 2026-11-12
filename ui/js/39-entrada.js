/* ================= entrada: teclado, mando, táctil ================= */
function input(k) {
  Snd.ensure(); closeBattPop();
  if (activity()) return;                 // la primera pulsación solo quita la atenuación
  if (QM.open) return k === "guide" ? closeQuick() : qmInput(k);
  if (k === "guide") return guidePress();
  if (mode === "boot") return dismissBoot();
  if (mode === "lock") return lockPress(k);
  if (mode === "game") { if (k === "power") sleepConsole(); return; }
  if (mode !== "ui") return;
  const v = topView();
  if (v && v.onKey && v.onKey(k)) return;
  if (k === "up" || k === "down" || k === "left" || k === "right") v ? navView(v, k) : navHome(k);
  else if (k === "a") activate();
  else if (k === "b") back();
  else if (k === "x") optionsKey();
  else if (k === "power") sleepConsole();
  else if (k === "start") { const g = !v && focusedGame(); if (g) openView(propsView(g)); else if (!(v && v.isSettings)) openView(settingsView()); }
  else if (!v && (k === "lb" || k === "rb")) homeJump(k);
  else bump(v ? v.items()[v.i] : homeEl());
}
const KEYS = { ArrowUp: "up", ArrowDown: "down", ArrowLeft: "left", ArrowRight: "right", Enter: "a", " ": "a", Escape: "b", Backspace: "b",
  x: "x", y: "y", m: "start", "+": "start", "=": "start", p: "power", q: "lb", e: "rb", PageUp: "lb", PageDown: "rb", z: "lt", c: "rt" };
addEventListener("keydown", e => {
  const v = topView();
  if (mode === "ui" && v && v.onType && e.key.length === 1 && !e.ctrlKey && !e.altKey && !e.metaKey) { e.preventDefault(); v.physical = true; v.onType(e.key); return; }
  if (mode === "ui" && v && v.onType && v.physical && e.key === "Enter") { e.preventDefault(); v.done(); return; }
  const lk = e.key.length === 1 ? e.key.toLowerCase() : e.key;
  if (lk === "h" || lk === "Home") { e.preventDefault(); if (!e.repeat) guideDown(); return; }
  if (lk === "k") { e.preventDefault(); if (!e.repeat) { activity(); captureDown(); } return; }
  const k = KEYS[e.key] || KEYS[e.key.toLowerCase()];
  if (!k) return;
  e.preventDefault();
  if (!e.repeat || ["up", "down", "left", "right"].includes(k)) input(k);
});

addEventListener("keyup", e => {
  const lk = e.key.length === 1 ? e.key.toLowerCase() : e.key;
  if (lk === "h" || lk === "Home") { e.preventDefault(); guideUp(); }
  else if (lk === "k") { e.preventDefault(); captureUp(); }
});
const pad = { held: {}, next: {} };
function padInput(k) {
  if (OVERLAY && Host.has) { if (!QM.open || QM.src === "host") return; QM.src = "pad"; }
  input(k);
}
function pollPads(t) {
  // Encima del juego, los botones pueden llegar por dos caminos: el control del navegador (si el menú tiene el foco) o
  // la app (pad.key, si el juego se lo quedó). Cada vez que se abre, el que llegue primero se usa y el otro se ignora,
  // para que nada cuente doble y el menú siempre responda.
  const list = navigator.getGamepads && !(OVERLAY && Host.has && QM.src === "host") ? [...navigator.getGamepads()].filter(Boolean) : [];
  const p = list[0];
  if (p) {
    lastPad = p; watchActiveGamepad(list);
    const B = i => list.some(g => g.buttons[i] && (g.buttons[i].pressed || g.buttons[i].value > .5));
    const ax = [0, 1].map(i => list.reduce((m, g) => Math.abs(g.axes[i] || 0) > Math.abs(m) ? g.axes[i] : m, 0));
    const dirs = { up: B(12) || ax[1] < -.55, down: B(13) || ax[1] > .55, left: B(14) || ax[0] < -.55, right: B(15) || ax[0] > .55 };
    for (const d in dirs) {
      if (dirs[d]) {
        if (!pad.held[d]) { pad.held[d] = true; pad.next[d] = t + 360; padInput(d); }
        else if (t >= pad.next[d]) { pad.next[d] = t + 105; padInput(d); }
      } else pad.held[d] = false;
    }
    // distribución Xbox: A0 B1 X2 Y3 LB4 RB5 LT6 RT7 Menú9 Xbox16 (el botón Xbox lo manda el anfitrión en la app real)
    const btns = { a: 0, b: 1, x: 2, y: 3, lb: 4, rb: 5, lt: 6, rt: 7, start: 9 };
    for (const k in btns) { const pr = B(btns[k]); if (pr && !pad.held[k]) padInput(k); pad.held[k] = pr; }
    // en la app el botón Xbox lo maneja Scarlet (Windows); si la página también lo leyera, el menú rápido se abriría y cerraría a la vez
    if (!Host.has) { const gd = B(16); if (gd && !pad.held.guide) guideDown(); else if (!gd && pad.held.guide) guideUp(); pad.held.guide = gd; }
  }
  requestAnimationFrame(pollPads);
}
requestAnimationFrame(pollPads);
const padLabel = gp => trunc(gp.id.replace(/\(.*\)/, "").trim() || "Mando", 30);
addEventListener("gamepadconnected", e => {
  const gp = e.gamepad;
  if (gp.index > 0 && !state.pads.find(p => p.gp === gp.index)) {   // el índice 0 se toma como el mando integrado
    state.pads.push({ key: "gp" + gp.index, gp: gp.index, name: padLabel(gp), type: "USB / inalámbrico", player: state.pads.length + 1 });
    updatePadName(); refreshControls();
  }
  // (1.7.2) el mando de la consola: Windows lo presenta como «Xbox 360 Controller»; se dice cuál es y lleva su dibujo
  const ally = gp.index === 0 && (!Host.has || state.pads.some(p => p.key === "ally"));
  if (mode !== "boot") notify(ally ? { icon: ALLY_IC(), title: "Mando conectado", text: "ROG Xbox Ally X" } : { icon: I.pad, title: "Mando conectado", text: padLabel(gp) });
});
const ALLY_IC = () => `<i class="allyic" style="-webkit-mask-image:url(${PADSRC});mask-image:url(${PADSRC})"></i>`;
addEventListener("gamepaddisconnected", e => {
  const p = state.pads.find(x => x.gp === e.gamepad.index && x.gp > 0);
  if (p) { state.pads = state.pads.filter(x => x !== p); renumberPads(); updatePadName(); refreshControls(); }
  if (mode !== "boot") notify({ icon: I.pad, title: "Mando desconectado", text: p ? p.name : "Vuelve a conectarlo para seguir jugando" });
});
Events.on("pad.connected", p => { state.pads = state.pads.filter(x => x.key !== p.key).concat(p); updatePadName(); refreshControls(); notify({ icon: p.key === "ally" ? ALLY_IC() : I.pad, title: "Mando conectado", text: `${p.name} · Jugador ${p.player}` }); });
Events.on("pad.disconnected", p => { state.pads = state.pads.filter(x => x.key !== p.key); renumberPads(); updatePadName(); refreshControls(); notify({ icon: I.pad, title: "Mando desconectado", text: p.name }); });

document.addEventListener("click", e => { Snd.ensure(); if (activity()) return; if (QM.open) { if (e.target.id === "qshade") closeQuick(); return; } if (mode === "ui") onHomeClick(e); }, true);
addEventListener("mousemove", () => { if (!dimmed) lastActivity = performance.now(); });
$("#boot").addEventListener("click", e => { e.stopPropagation(); dismissBoot(); });

