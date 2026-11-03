/* ================= suspensión y pantalla de bloqueo ================= */
const lock = { last: null, n: 0, t: 0, timer: 0 };
function sleepConsole(kind) {
  if (mode !== "ui" && mode !== "game") return;
  if (QM.open) { QM.open = false; $("#quick").classList.remove("show"); $("#qshade").classList.remove("show"); }
  state.sleptFromGame = mode === "game" && !state.overlay;
  if (state.overlay) { state.overlay = null; $("#game").className = ""; }
  closeAllViews(); mode = "sleep";
  $("#sleep").classList.add("show");
  Host.call(kind === "hibernate" ? "system.hibernate" : "system.sleep").catch(() => {});
  if (!Host.has) setTimeout(wake, 1900);
}
function wake() {
  const g = state.sleptFromGame && runningGame();
  const ic = $("#licon"), bg = $(".lbg");
  if (g) {
    ic.className = "licon game"; ic.innerHTML = '<img alt="">'; Art.apply($("img", ic), g);
    bg.style.backgroundImage = `url("${Art.placeholder(g, "hero")}")`;
    Art.get(g, "hero").then(u => { if (u) bg.style.backgroundImage = `url("${u}")`; });
  } else { ic.className = "licon"; ic.innerHTML = I.home; bg.style.backgroundImage = ""; }
  lock.n = 0; lock.last = null; paintDots();
  $("#lock").className = "show"; mode = "lock";
  $("#sleep").classList.remove("show");
}
Events.on("system.resumed", wake);
function paintDots() { $$(".ldots i").forEach((d, i) => d.classList.toggle("on", i < lock.n)); }
function lockPress(k) {
  if (!["a", "b", "x", "y", "start", "lb", "rb"].includes(k)) return;
  const now = performance.now();
  if (k === lock.last && now - lock.t < 1500) lock.n++; else lock.n = 1;
  lock.last = k; lock.t = now; paintDots(); play("tick", lock.n);
  clearTimeout(lock.timer);
  if (lock.n >= 3) return unlock();
  lock.timer = setTimeout(() => { lock.n = 0; paintDots(); }, 1500);
}
async function unlock() {
  mode = "unlocking";
  const ic = $("#licon"); ic.classList.remove("pop"); void ic.offsetWidth; ic.classList.add("pop");
  play("wake"); await wait(380);
  $("#lock").className = "out"; await wait(380); $("#lock").className = "";
  if (state.sleptFromGame && state.running) { mode = "game"; Host.call("game.focus").catch(() => {}); } else { mode = "ui"; updateFocus(); }
}

