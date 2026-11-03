/* ================= arranque ================= */
const bootStart = performance.now();
function dismissBoot() {
  if (mode !== "boot" || performance.now() - bootStart < 700) return;
  mode = "ui";
  $("#boot").classList.add("gone"); play("boot");
  setTimeout(() => $("#boot").classList.add("off"), 800);   // ya desvanecida: fuera del todo (sus animaciones seguían corriendo)
  renderHome();
  if (state.running && state.adopted) {   // la app se reinició con un juego abierto: se regresa a él
    state.adopted = false; showGame(runningGame()); mode = "game";
    Host.call("game.focus").catch(() => {});
  }
  if (!Host.has) {
    setTimeout(() => demoFriendOnline(), 7000); setInterval(() => demoFriendOnline(), 45000);
    setTimeout(() => { const g = byId(2246340); if (g && !state.downloads[g.appid]) { state.downloads[g.appid] = { p: .3, kind: "Actualizando" }; renderHome(); } }, 2500);
  }
}
Events.on("library.changed", () => reloadLibrary());
async function reloadLibrary() {
  const keep = focusedGame(); games = await Data.games();
  if (keep) { const i = homeGames().findIndex(g => g.appid === keep.appid); if (i >= 0) home.g = i; }
  if (mode !== "boot") renderHome();
  const lv = views.find(v => v.cols === 6 && v.refresh); if (lv) lv.refresh(true);
}
Events.on("profile.changed", p => { profile = p; renderUsers(); updateFocus(); });
Events.on("friends.changed", list => { if (Array.isArray(list)) { friends.length = 0; friends.push(...list); } });
(async function start() {
  if (OVERLAY) {   // solo el menú rápido: sin pantalla de inicio ni carga de juegos
    $("#boot").remove(); mode = "ui";
    const st = Host.has ? await Host.call("system.state").catch(() => null) : null;
    if (st) { mergeSys(st); saveSys(); }
    if (Host.has) Host.call("sounds.list").then(m => { if (m && Object.keys(m).length) Snd.loadCustom(m); }).catch(() => {});
    if (Host.has) Host.call("app.info").then(i => { DOLBY = !!(i && i.dolby); }).catch(() => {});
    if (!Host.has) { DOLBY = true; ATMOS = true; demoMedia(); openQuick(); }
    return;
  }
  [profile, games, friends] = await Promise.all([Data.profile(), Data.games(), Data.friends()]);
  if (Host.has) {
    const pads = await Host.call("pads.list").catch(() => null); if (pads && pads.length) state.pads = pads;
    Host.call("audio.mutebehind", { on: settings.muteBehind !== false }).catch(() => {});
    Host.call("cursor.hide", { on: settings.hideCursor !== false }).catch(() => {});
    const st = await Host.call("system.state").catch(() => null); if (st) { mergeSys(st); saveSys(); }
    Host.call("sounds.list").then(m => { if (m && Object.keys(m).length) Snd.loadCustom(m); }).catch(() => {});
    Host.call("sounds.info").then(i => { soundPack = (i && i.pack) || null; }).catch(() => {});
    const gs = await Host.call("game.state").catch(() => null);
    if (gs && gs.appid && byId(gs.appid)) { state.running = gs.appid; state.adopted = !!gs.ready; }
  }
  updatePadName();
  renderUsers();
  if (mode !== "boot") renderHome();
  if (Host.has) setTimeout(dismissBoot, 2200);
  const info = Host.has ? await Host.call("app.info").catch(() => null) : null;
  DOLBY = !!(info && info.dolby); paintDolby();
  if (info && info.version) APP_VERSION = String(info.version).replace(/\.0$/, "");
  if (info && info.updated) setTimeout(() => notify({ icon: I.gear, title: "Scarlet se actualizó", text: `Ya tienes la ${verName(APP_VERSION)}` }), 3000);
  if (info && info.rolledBack) setTimeout(() => notify({ icon: I.gear, title: "Se regresó a la versión anterior", text: `La ${verName(info.rolledBack)} no abrió bien; sigues con la ${verName(APP_VERSION)}` }), 3000);
  if (Host.has && Date.now() - (settings.updChecked || 0) > 864e5) setTimeout(() => Host.call("update.check").then(r => {
    settings.updChecked = Date.now(); saveSettings();
    if (r && r.available) { updState.available = true; updState.latest = r.latest; notify({ icon: I.gear, title: "Hay una actualización", text: `${verName(r.latest)} · Ajustes › Sistema` }); }
  }).catch(() => {}), 15000);
  if (Host.has && settings.bootLast) {   // al encender: directo al último juego (solo si la consola acaba de arrancar)
    const last = info && info.startup && !state.running && games.find(g => g.kind !== "app" && g.installed && !isHidden(g));
    if (last) setTimeout(() => { if (mode === "ui" && !state.running) { toast("Abriendo " + last.name); launchGame(last); } }, 2600);
  }
})();
