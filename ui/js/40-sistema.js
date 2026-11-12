/* ================= estado del sistema ================= */
function clockText(d = new Date()) {
  const m = String(d.getMinutes()).padStart(2, "0");
  if (settings.clock24 !== false) return String(d.getHours()).padStart(2, "0") + ":" + m;
  const h = d.getHours() % 12 || 12;
  return `${h}:${m} ${d.getHours() < 12 ? "a.m." : "p.m."}`;
}
function tick() {
  if (document.hidden && !OVERLAY) return;   // escondida (jugando): nada que actualizar; al volver se pone al día
  $$(".clock").forEach(c => c.textContent = clockText());
  $$(".wifi").forEach(w => w.style.opacity = (typeof netOn === "function" ? netOn() : navigator.onLine) && (typeof sys === "undefined" || (sys.wifi && !sys.airplane)) ? 1 : .3);
  if (typeof QM !== "undefined" && QM.open) renderQuick();
}
setInterval(tick, 10000); tick();
document.addEventListener("visibilitychange", () => { if (!document.hidden) tick(); });
/* Avisos al 20 % y al 10 % (cada uno una vez por descarga) y aviso al conectar el cargador. */
const battWarn = { 20: false, 10: false };
function setBatt(level, charging) {
  const wasCharging = deviceCharging;
  deviceBatt = level; deviceCharging = !!charging;
  $$(".battPct").forEach(e => e.textContent = Math.round(level * 100) + "%");
  $$(".battFill").forEach(e => { e.setAttribute("width", Math.max(1.5, 20.6 * level)); e.setAttribute("fill", level <= .15 && !charging ? "#e5484d" : charging ? "#3ad16b" : "currentColor"); });
  $$(".battBolt").forEach(e => e.style.display = charging ? "" : "none");
  if (mode === "boot") return;
  for (const t of [10, 20]) {
    if (!charging && level <= t / 100 && !battWarn[t]) { battWarn[10] = level <= .1; battWarn[20] = true; notifyBattery(level); break; }
  }
  if (charging || level > .25) { battWarn[20] = false; battWarn[10] = false; }
  else if (level > .12) battWarn[10] = false;
  if (charging && !wasCharging && typeof battReady !== "undefined" && battReady) notify({ icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"><path d="M13 2.5 5 13.5h6l-1 8 8-11h-6z"/></svg>', title: "Cargando", text: `Batería al ${Math.round(level * 100)} %`, batt: level, chg: true });
}
let battReady = false; setTimeout(() => battReady = true, 4000);
if (navigator.getBattery) navigator.getBattery().then(b => { const u = () => setBatt(b.level, b.charging); u(); b.addEventListener("levelchange", u); b.addEventListener("chargingchange", u); }).catch(() => setBatt(.82)); else setBatt(.82);
addEventListener("resize", () => updateFocus());
/* ================= v2.9.8: modo TV ================= */
function setTvMode(on, silent) {
  const was = document.documentElement.classList.contains("tv");
  document.documentElement.classList.toggle("tv", on);
  if (on !== was) { requestAnimationFrame(() => updateFocus()); if (!silent) notify({ icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2.5" y="4.5" width="19" height="13" rx="2"/><path d="M8 21h8"/></svg>', title: on ? "Modo TV" : "Modo portátil", text: on ? "Pantalla externa conectada" : "Pantalla externa desconectada" }); }
}
Events.on("display.changed", d => setTvMode(!!d.tv));

