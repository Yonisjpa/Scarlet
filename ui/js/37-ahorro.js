/* ================= v2.9.8: ahorro de energía ================= */
const DIM_OPTS = [["Nunca", 0], ["15 s (prueba)", 15e3], ["1 min", 6e4], ["3 min", 18e4], ["5 min", 3e5]];
const SLEEP_OPTS = [["Nunca", 0], ["30 s (prueba)", 3e4], ["5 min", 3e5], ["10 min", 6e5], ["30 min", 18e5]];
if (settings.dimAfter == null) settings.dimAfter = 18e4;
if (settings.sleepAfter == null) settings.sleepAfter = 6e5;
for (const [k, d] of [["nFriendOn", "all"], ["nFriendPlay", "all"], ["nInGame", "all"]]) if (settings[k] == null) settings[k] = d;
let lastActivity = performance.now(), dimmed = false;
function activity() {
  lastActivity = performance.now();
  if (dimmed) { dimmed = false; $("#dim").classList.remove("on"); return true; }
  return false;
}
setInterval(() => {
  if (document.hidden) { lastActivity = performance.now(); return; }   // jugando: ni atenuar ni dormir
  if (OVERLAY || mode !== "ui" || QM.open) { lastActivity = Math.max(lastActivity, performance.now() - 1000); return; }
  const idle = performance.now() - lastActivity;
  if (settings.sleepAfter && idle >= settings.sleepAfter) { dimmed = false; $("#dim").classList.remove("on"); lastActivity = performance.now(); return sleepConsole(); }
  if (settings.dimAfter && idle >= settings.dimAfter && !dimmed) { dimmed = true; $("#dim").classList.add("on"); }
}, 1000);
const optLabel = (opts, v) => (opts.find(o => o[1] === v) || opts[0])[0];
function pickOption(title, opts, key) {
  openView(pickerView(title, I.power, opts.map(([l, v]) => ({ label: l, value: v })), settings[key], v => {
    settings[key] = v; saveSettings(); lastActivity = performance.now();
    const sv = views.find(x => x.isSettings); if (sv) sv.refresh();
  }));
}

