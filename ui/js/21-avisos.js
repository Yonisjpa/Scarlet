/* ================= notificaciones ================= */
/* ================= beta 1.0: notificaciones =================
   Salen de una en una (las demás esperan su turno) con una línea abajo que se va acabando. Las que tienen acción la
   muestran con el botón Xbox: un toque mientras se ve la hace (solo en el menú; jugando, el botón Xbox abre el menú
   rápido como siempre). Lo que llega jugando y no se muestra (según Ajustes › Notificaciones) se junta en un resumen
   «Mientras jugabas» al volver al menú.
   o: { icon, bg, fg, title, text, sound, image, letter, kind, act: { label, run }, thumb: { src, video, dur, saving }, star, important } */
const NOTIF_MS = 4600, NOTIF_ACT_MS = 5600;
const notifQ = [], missed = [];
let notifCur = null;
const notifKinds = { dl: "nDl", ach: "nAch", batt: "nBatt" };
const notifImportant = o => o.important || ["batt", "dl", "cap", "err"].includes(o.kind);
function notifAllowed(o) {
  if (notifKinds[o.kind] && settings[notifKinds[o.kind]] === false) return false;
  if (o.kind === "friend" || o.kind === "friendplay") {
    const pref = settings[o.kind === "friend" ? "nFriendOn" : "nFriendPlay"] || "all";
    if (pref === "none" || (pref === "best" && !o.star)) return false;
  }
  return true;
}
function notify(o) {
  if (!notifAllowed(o)) return;
  if (mode === "game" && !OVERLAY) {
    const pref = settings.nInGame || "all";
    if (pref === "none" || (pref === "important" && !notifImportant(o))) { missed.push({ title: o.title, text: o.text }); return; }
    if (Host.has) {   // el menú está oculto: la app dibuja la notificación encima del juego (igual, pero sin acción)
      const t = o.thumb || {};
      const tag = NOTIF_TAG[o.kind] || (o.tag ? [o.tag, o.tagColor || "#ffffff"] : ["", "#ffffff"]);
      return Host.call("overlay.notify", { title: o.title, text: o.text, letter: o.letter || "", color: o.bg || "", image: o.image || "", wide: !!o.thumb, video: !!t.video, dur: t.dur || "", saving: !!t.saving,
        tag: tag[0], accent: tag[1] }).catch(() => {});
    }
  }
  if (o.key && notifCur && notifCur.key === o.key) return notifReplace(o);   // la misma (p. ej. "Guardando video…" → "Video guardado")
  const qi = o.key ? notifQ.findIndex(x => x.key === o.key) : -1;
  if (qi >= 0) notifQ[qi] = o; else notifQ.push(o);
  while (notifQ.length > 6) notifQ.shift();
  notifStack();
  if (!notifCur && !Ach.cur) notifNext();
}
/* beta 1.6: cápsula con un color y una etiqueta por tipo de aviso */
const NOTIF_TAG = { friend: ["En línea", "#3ddc84"], friendplay: ["Jugando", "#3ddc84"], dl: ["Descarga", "#3fb3ff"], batt: ["Batería", "#ff5a5a"], cap: ["Captura", "#ffffff"], sum: ["Avisos", "#ffffff"], err: ["Aviso", "#f2c230"] };
function notifHTML(o) {
  const t = o.thumb;
  const ic = t ? `<div class="ic thumb${t.saving ? " saving" : ""}">${t.src ? `<img alt="" src="${esc(t.src)}">` : ""}${t.video && !t.saving ? `<span class="pl"><b></b></span>${t.dur ? `<span class="du">${esc(t.dur)}</span>` : ""}` : ""}${t.saving ? `<span class="rec"><i></i></span>` : ""}</div>`
    : `<div class="ic" style="${o.bg ? `background:${o.bg};color:${o.fg || "#fff"}` : ""}">${o.icon}</div>`;
  const act = o.act && mode === "ui" ? `<div class="nact"><span class="xbtn"></span>${esc(o.act.label)}</div>` : "";
  const tag = NOTIF_TAG[o.kind] || (o.tag ? [o.tag, o.tagColor || "#ffffff"] : null);
  return `${ic}<div class="ntx">${tag ? `<span class="ntag">${esc(tag[0])}</span>` : ""}<b>${esc(o.title)}${o.star ? '<i class="nstar">★</i>' : ""}</b>${o.lines ? `<ul>${o.lines.map(l => `<li>${esc(l)}</li>`).join("")}</ul>` : `<span>${esc(o.text)}</span>`}</div>${act}<div class="nlife"></div>`;
}
function notifNext() {
  if (Ach.cur) { notifCur = null; return; }                      // hay un logro en pantalla: esperan a que se vaya
  if (Ach.q.length) { notifCur = null; return Ach.next(); }      // (beta 1.6) y los logros pendientes van primero
  const o = notifQ.shift(); if (!o) { notifCur = null; return; }
  const n = document.createElement("div"); n.className = "notif" + (o.thumb ? " wide" : "") + (o.lines ? " sum" : "");
  n.innerHTML = notifHTML(o); n.style.setProperty("--nac", (NOTIF_TAG[o.kind] || [0, o.tagColor || "#ffffff"])[1]);
  const wrap = document.createElement("div"); wrap.className = "nwrap"; wrap.append(n);
  const box = $("#notifs"); box.innerHTML = ""; box.append(wrap);
  if (settings.nSound !== false) play(o.sound || "notif");
  notifCur = Object.assign(o, { el: n });
  notifTime(o);
  notifStack();
}
/* Las que esperan se asoman detrás, como cartas, con un globito +N. */
function notifStack() {
  const o = notifCur; if (!o || !o.el) return;
  const wrap = o.el.parentNode; if (!wrap) return;
  wrap.querySelectorAll(".nghost,.nmore").forEach(x => x.remove());
  const n = notifQ.length; if (!n) return;
  for (let i = Math.min(2, n); i >= 1; i--) {
    const gh = document.createElement("div"); gh.className = "nghost" + (i === 2 ? " g2" : "");
    gh.style.transform = `translate(${1.1 * i}rem,${1.6 * i}rem) scale(${(1 - .06 * i).toFixed(2)})`;
    if (o.el.classList.contains("wide") || o.el.classList.contains("sum")) gh.style.borderRadius = "1.9rem";
    wrap.prepend(gh);
  }
  const m = document.createElement("span"); m.className = "nmore"; m.textContent = "+" + n; wrap.append(m);
}
/* La línea de abajo y el tiempo que se queda; si llega otra detrás, esta se va un poco antes. */
function notifTime(o) {
  const ms = o.ms || (o.act ? NOTIF_ACT_MS : NOTIF_MS), life = $(".nlife", o.el);
  clearTimeout(o.t);
  if (o.thumb && o.thumb.saving) { if (life) life.style.display = "none"; o.t = setTimeout(() => notifDone(o), 30000); return; }
  if (life && life.animate) life.animate([{ transform: "scaleX(1)" }, { transform: "scaleX(0)" }], { duration: ms, easing: "linear", fill: "forwards" });
  o.t = setTimeout(() => notifDone(o), ms);
}
function notifReplace(o) {
  const cur = notifCur; o.el = cur.el; clearTimeout(cur.t);
  cur.el.className = "notif" + (o.thumb ? " wide" : "");
  cur.el.innerHTML = notifHTML(o);
  if (settings.nSound !== false && o.sound) play(o.sound);
  notifCur = o; notifTime(o);
}
function notifDone(o) {
  if (notifCur !== o) return;
  o.el.classList.add("out"); notifCur = null;
  const wrap = o.el.parentNode; if (wrap) wrap.querySelectorAll(".nghost,.nmore").forEach(x => x.remove());
  setTimeout(() => { (wrap && wrap.classList.contains("nwrap") ? wrap : o.el).remove(); notifNext(); }, 300);
}
/* Botón Xbox con una notificación con acción a la vista (en el menú): hace la acción. */
function notifAction() {
  const o = notifCur;
  if (!o || !o.act || mode !== "ui" || QM.open || OVERLAY) return false;
  play("select"); clearTimeout(o.t); notifDone(o);
  try { o.act.run(); } catch (e) { console.error(e); }
  return true;
}
/* Al volver al menú: lo que no se mostró mientras jugabas, en un solo aviso. */
setInterval(() => {
  if (mode !== "ui" || !missed.length || QM.open) return;
  const list = missed.splice(0), more = list.length - 3;
  const lines = list.slice(0, 3).map(x => x.text && x.title ? `${x.title} · ${x.text}` : x.title || x.text);
  if (more > 0) lines.push(`y ${more} más`);
  missedLog.unshift(...list.map(x => ({ ...x, at: Date.now() }))); missedLog.splice(30);
  notify({ icon: I.home, title: "Mientras jugabas", text: `${list.length} aviso${list.length === 1 ? "" : "s"}`, lines, ms: 7000, kind: "sum", sound: "notif",
    act: { label: "Ver todo", run: () => openView(missedView()) } });
}, 1200);
const missedLog = [];
function missedView() {
  return {
    hints: [["B", "Atrás"]],
    render(el) {
      el.innerHTML = head(I.home, "Mientras jugabas", `${missedLog.length} avisos`) + `<div class="vbody">${missedLog.map(x => `<button class="lrow fi"><span class="meta"><b>${esc(x.title)}</b><span>${esc(x.text || "")}</span></span><span class="val">${esc(new Date(x.at).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }))}</span></button>`).join("") || '<p class="note">No hay avisos.</p>'}</div>`;
    },
    items() { return fis(this.el); }
  };
}
const isBest = name => (settings.bestFriends || []).includes(name);
function notifyFriend(f) {
  const best = isBest(f.name);
  notify({ kind: f.game ? "friendplay" : "friend", star: best, sound: f.game ? "friendgame" : "friend", icon: f.avatar ? `<img alt="" src="${esc(f.avatar)}">` : esc(f.name[0].toUpperCase()), letter: f.name[0].toUpperCase(), bg: `hsl(${hueOf(f.name)},55%,48%)`, title: f.name, text: f.game ? "Está jugando " + f.game : "Se ha conectado",
    act: { label: "Ver perfil", run: () => { const x = friends.find(y => y.name === f.name); if (x) openView(friendProfileView(x)); } } });
}
/* beta 1.6: los logros tienen su propio aviso, como en Xbox (43-logros.js) */
function notifyAchievement(g, name, d = {}) { showAchievement(g, name, d); }
function notifyBattery(level) { notify({ kind: "batt", icon: I.battLow, title: "Batería baja", text: `Queda ${Math.round(level * 100)} %. Conecta el cargador.` }); }
/* Capturas: miniatura de verdad (la foto, o un cuadro del video), con «Ver» que abre el Álbum en esa captura. */
function notifyCapture(item, saving) {
  const video = item.type === "video", game = item.game && item.game !== "Sistema" ? item.game : "";
  const src = item.thumb || (video ? "" : item.url);
  if (saving) return notify({ key: "cap:" + item.path, kind: "cap", thumb: { src, video: true, saving: true }, title: "Guardando video…", text: "Últimos 30 segundos" + (game ? " · " + game : ""), sound: "recstop" });
  notify({ key: "cap:" + item.path, kind: "cap", image: item.thumbPath || (video ? "" : item.path || ""), letter: "▣", sound: video ? "notif" : "capture",
    thumb: { src, video, dur: item.duration ? mmss(item.duration) : "" },
    title: video ? "Video guardado" : "Captura guardada", text: video ? `Últimos ${item.duration ? Math.round(item.duration) : 30} segundos${game ? " · " + game : ""}` : `${game ? game + " · " : ""}en el Álbum`,
    act: { label: "Ver", run: () => openAlbumAt(item) } });
}
function openAlbumAt(item) {
  if (mode !== "ui") return;
  closeAllViews(); const av = albumView(); openView(av);
  const go = (n = 0) => {   // el Álbum carga su lista; luego se abre la captura
    if (!av.list) return n < 30 && setTimeout(() => go(n + 1), 100);
    const i = av.list.findIndex(x => x.path === item.path);
    if (i >= 0 && topView() === av) openView(viewerView(av, i));
  };
  go();
}
function demoCapture() {
  const g = games.find(x => x.installed) || games[0];
  const item = { url: shotSvg(g, 3), path: "demo_" + Date.now(), game: g.name, type: "image" };
  notifyCapture(item);
  setTimeout(() => { const v = { url: shotSvg(g, 9), thumb: shotSvg(g, 9), path: "demov_" + Date.now(), game: g.name, type: "video" }; notifyCapture(v, true); setTimeout(() => notifyCapture({ ...v, duration: 30 }), 2500); }, 5200);
}
function demoFriendOnline(force) {
  const off = friends.filter(f => f.status === "offline");
  const f = off.length ? off[Math.floor(Math.random() * off.length)] : (force ? friends[Math.floor(Math.random() * friends.length)] : null);
  if (!f) return;
  f.status = Math.random() < .5 ? "online" : "playing";
  if (f.status === "playing") { const gl = games.filter(hasAch); f.game = gl.length ? gl[Math.floor(Math.random() * Math.min(6, gl.length))].name : ""; }
  notifyFriend(f);
}
let battWarned = false;
Events.on("friend.online", f => { const x = friends.find(y => y.name === f.name); if (x) Object.assign(x, f, { status: f.game ? "playing" : "online" }); else friends.push({ ...f, status: f.game ? "playing" : "online" }); notifyFriend(f); });
Events.on("battery.low", d => notifyBattery(d.level));
Events.on("achievement.unlocked", d => {
  const g = byId(d.appid);
  if (g && hasAch(g)) g.ach[0] = d.unlocked ? Math.min(d.unlocked, g.ach[1]) : Math.min(g.ach[0] + 1, g.ach[1]);
  notifyAchievement(g, d.name, d);
});

