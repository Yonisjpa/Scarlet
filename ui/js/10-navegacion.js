/* ================= navegación ================= */
function wiggle(el, dir) {
  if (!el || !el.animate) return;
  const a = .7 * (dir === "left" || dir === "up" ? -1 : 1), ax = dir === "up" || dir === "down" ? "y" : "x";
  const t = v => ax === "x" ? `${v}rem 0` : `0 ${v}rem`;
  el.animate([{ translate: t(0) }, { translate: t(a) }, { translate: t(-a * .55) }, { translate: t(a * .25) }, { translate: t(0) }], { duration: 300, easing: "ease-out" });
}
function bump(el, dir) { play("bump"); wiggle(el, dir || "right"); }
function navHome(dir) {
  const before = homeEl();
  if (home.zone === "games") {
    const n = homeTiles().length;
    if (dir === "left" && home.g > 0) home.g--;
    else if (dir === "right" && home.g < n - 1) home.g++;
    else if (dir === "up") home.zone = "top";
    else if (dir === "down") home.zone = "apps";
  } else if (home.zone === "apps") {
    if (dir === "left" && home.a > 0) home.a--;
    else if (dir === "right" && home.a < APPS.length - 1) home.a++;
    else if (dir === "up") home.zone = "games";
  } else {
    const n = $("#users").children.length;
    if (dir === "left" && home.t > 0) home.t--;
    else if (dir === "right" && home.t < n - 1) home.t++;
    else if (dir === "down") home.zone = "games";
  }
  if (homeEl() === before) return bump(before, dir);
  play("move"); updateFocus();
}
function homeJump(k) {
  if (home.zone !== "games") return bump(homeEl(), k === "lb" ? "left" : "right");
  const n = homeTiles().length, g = clamp(home.g + (k === "lb" ? -5 : 5), 0, n - 1);
  if (g === home.g) return bump(homeEl(), k === "lb" ? "left" : "right");
  home.g = g; play("move"); updateFocus();
}
function navView(v, dir) {
  if (v.nav && v.nav(dir) !== false) return;
  const items = v.items(), cols = v.cols || 1, n = items.length;
  if (v.onLR && cols === 1 && (dir === "left" || dir === "right")) return v.onLR(v.i, dir);
  if (!n) return play("bump");
  let i = v.i;
  if (dir === "up") i -= cols;
  else if (dir === "down") i += cols;
  else if (dir === "left") { if (cols > 1 && i % cols > 0) i--; }
  else if (dir === "right") { if (cols > 1 && i % cols < cols - 1 && i + 1 < n) i++; }
  if (dir === "down" && i >= n && Math.floor((n - 1) / cols) > Math.floor(v.i / cols)) i = n - 1;
  if (i === v.i || i < 0 || i >= n) return bump(items[v.i], dir);
  v.i = i; play("move"); updateFocus();
}
function activate() {
  const v = topView();
  if (v) { const el = v.items()[v.i]; if (el) el.click(); else play("bump"); return; }
  homeEl()?.click();
}
function back() {
  const v = topView();
  if (!v) return bump(homeEl(), "left");
  if (v.onB && v.onB()) return;
  closeView(); if (v.onBack) v.onBack();
}
function optionsKey() {
  const v = topView();
  if (v) return v.onX ? v.onX() : bump(v.items()[v.i]);
  const g = focusedGame();
  if (!g) return bump(homeEl());
  g.appid === state.running ? askCloseRunning() : openView(propsView(g));
}
function askCloseRunning() {
  const g = runningGame(); if (!g) return;
  confirmDialog(`¿Cerrar ${g.name}?\nSe perderá el progreso que no hayas guardado.`, "Cerrar").then(ok => { if (ok) closeRunning(); });
}
function onHomeClick(e) {
  const el = e.target.closest("[data-z]"); if (!el || views.length || mode !== "ui") return;
  const z = el.dataset.z, i = +el.dataset.i;
  if (z === "g") {
    home.zone = "games"; home.g = i; updateFocus();
    if (i < homeGames().length) launchGame(homeGames()[i], el); else openView(libraryView(), el);
  } else if (z === "a") { home.zone = "apps"; home.a = i; updateFocus(); APPS[i].act(el); }
  else if (z === "t") { home.zone = "top"; home.t = i; updateFocus(); openView(profileView(), el); }
}

