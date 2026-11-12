/* ================= beta 1.6: logros como en Xbox =================
   Tres avisos, recreados de los de Xbox y del largo exacto de su sonido:
   - normal (2.8 s): disco verde con el botón Xbox → se voltea al trofeo → barra «Logro desbloqueado» → regresa.
   - raro (6.0 s, lo tiene menos del 10 %): disco azul con el botón Xbox → se voltea a un diamante que gira → barra azul
     con dos destellos → regresa al botón Xbox → se va.
   - 100 % (7.8 s, «mítico»): punto verde → cristales que giran → barra verde → al golpe del sonido, el ícono del último
     logro → se voltea al emblema (estrella y laurel) → «Juego completado al 100 %» → «Desbloqueaste los N logros».
   Cada uno se dibuja con frame(t): la misma función sirve para animar, para las pruebas (Ach.seek) y es la que copia la
   app (AchievementToast.cs) para dibujarlos encima de los juegos. Los juegos de Xbox usan su propio aviso: aquí no se tocan. */
const ACH_MS = { normal: 2800, rare: 6000, full: 7800 };
const ACH_VOL = { normal: 25, rare: 50, full: 75 };
const ACH_SND = { normal: "achieve", rare: "achrare", full: "ach100" };
const ACH_POS = [["Arriba a la izquierda", "tl"], ["Arriba al centro", "tc"], ["Arriba a la derecha", "tr"], ["Abajo a la izquierda", "bl"], ["Abajo al centro", "bc"], ["Abajo a la derecha", "br"]];
const ACH_RARE = 10;
const achKind = a => a.total > 0 && a.unlocked >= a.total ? "full" : a.percent != null && a.percent < ACH_RARE ? "rare" : "normal";
const pctText = p => p == null ? "" : (p < 1 ? p.toFixed(1) : p < 10 ? p.toFixed(1) : Math.round(p)) + " %";

/* ---- curvas y tiempos ---- */
const AE = {
  clamp: (x, a = 0, b = 1) => Math.min(b, Math.max(a, x)),
  seg: (t, a, b) => AE.clamp((t - a) / (b - a)),
  eo: x => 1 - Math.pow(1 - x, 3), ei: x => x * x * x,
  eio: x => x < .5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2,
  back: x => { const c = 1.9; return 1 + (c + 1) * Math.pow(x - 1, 3) + c * Math.pow(x - 1, 2); }
};

/* ---- diamante 3D (talla brillante sencilla): se proyecta y sombrea en cada cuadro ---- */
const GEM3D = (() => { const n = 8, v = [], f = [];
  const ring = (y, r, off) => Array.from({ length: n }, (_, i) => { const a = (i + off) / n * Math.PI * 2; return [Math.cos(a) * r, y, Math.sin(a) * r]; });
  v.push(...ring(-.42, .55, .5), ...ring(0, 1, 0), [0, 1, 0]); const T = i => i % n, G = i => n + (i % n), C = 2 * n;
  f.push(Array.from({ length: n }, (_, i) => T(i)).reverse());
  for (let i = 0; i < n; i++) { f.push([T(i), G(i + 1), G(i)]); f.push([T(i), T(i + 1), G(i + 1)]); f.push([G(i), G(i + 1), C]); }
  return { v, f }; })();
function gemFaces(spin, pitch) {
  const L = [-.4, -.8, .45], ln = Math.hypot(...L), cs = Math.cos(spin), sn = Math.sin(spin), cp = Math.cos(pitch), sp = Math.sin(pitch);
  const P = GEM3D.v.map(([x, y, z]) => { const x1 = x * cs + z * sn, z1 = -x * sn + z * cs; return [x1, y * cp - z1 * sp, y * sp + z1 * cp]; });
  return GEM3D.f.map(ix => { const q = ix.map(i => P[i]);
    const u = [q[1][0] - q[0][0], q[1][1] - q[0][1], q[1][2] - q[0][2]], w = [q[2][0] - q[0][0], q[2][1] - q[0][1], q[2][2] - q[0][2]];
    const nx = u[1] * w[2] - u[2] * w[1], ny = u[2] * w[0] - u[0] * w[2], nz = u[0] * w[1] - u[1] * w[0], nl = Math.hypot(nx, ny, nz) || 1;
    return { q, nz: nz / nl, z: q.reduce((s, c) => s + c[2], 0) / q.length, b: Math.max(0, -(nx * L[0] + ny * L[1] + nz * L[2]) / nl / ln) }; })
    .filter(f => f.nz < 0).sort((a, b) => b.z - a.z);
}
const gemColor = b => { const s = Math.pow(b, 10); return [Math.min(255, 60 + 170 * b + 30 * s) | 0, Math.min(255, 120 + 120 * b + 20 * s) | 0, Math.min(255, 215 + 40 * b) | 0]; };
function gemSVG(spin, pitch) {
  return gemFaces(spin, pitch).map(f => { const [r, g, b] = gemColor(f.b);
    return `<polygon points="${f.q.map(c => c[0].toFixed(3) + "," + c[1].toFixed(3)).join(" ")}" fill="rgb(${r},${g},${b})" stroke="rgba(235,245,255,.7)" stroke-width=".03" stroke-linejoin="round"/>`; }).join("");
}

/* ---- emblema del 100 %: estrella de 4 puntas plateada con laurel ---- */
const ACH_EMBLEM = (() => {
  let leaves = ""; const r = 36;
  for (const side of [-1, 1]) for (let i = 0; i < 6; i++) { const th = (side < 0 ? 100 + i * 17 : 80 - i * 17) * Math.PI / 180;
    const x = 50 + r * Math.cos(th), y = 50 + r * Math.sin(th), rot = th * 180 / Math.PI + (side < 0 ? 90 : -90) - side * 18;
    leaves += `<ellipse cx="${x.toFixed(1)}" cy="${y.toFixed(1)}" rx="7.5" ry="3.2" transform="rotate(${rot.toFixed(0)} ${x.toFixed(1)} ${y.toFixed(1)})" fill="url(#achLf)" stroke="#d6efd6" stroke-width=".6"/>`; }
  return `<svg viewBox="0 0 100 100"><defs><linearGradient id="achSv" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#fff"/><stop offset=".55" stop-color="#d7dde3"/><stop offset="1" stop-color="#8b959e"/></linearGradient>
    <linearGradient id="achLf" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#bfe6bf"/><stop offset="1" stop-color="#3f8f3f"/></linearGradient>
    <radialGradient id="achDk" cx=".5" cy=".4" r=".6"><stop offset="0" stop-color="#3a3f44"/><stop offset="1" stop-color="#15181b"/></radialGradient></defs>
    <circle cx="50" cy="50" r="47" fill="url(#achDk)" stroke="url(#achSv)" stroke-width="4"/>${leaves}
    <path d="M50 14 L57 43 L86 50 L57 57 L50 86 L43 57 L14 50 L43 43 Z" fill="url(#achSv)" stroke="#fff" stroke-width=".8"/>
    <path d="M50 14 L57 43 L50 50 Z M86 50 L57 57 L50 50 Z M50 86 L43 57 L50 50Z M14 50 L43 43 L50 50Z" fill="rgba(255,255,255,.55)"/></svg>`; })();
const ACH_TROPHY = '<svg viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M8 4h8v5a4 4 0 0 1-8 0z"/><path d="M8 6H5a3 3 0 0 0 3 4M16 6h3a3 3 0 0 1-3 4M12 13v4M8 20h8M10 17h4"/></svg>';

/* ---- estado de cada aviso en el tiempo t (segundos). pop: tamaño del disco; fl: volteo 0→1; w: barra 0→1; tx: textos ---- */
const ACH_FRAMES = {
  normal(t) {
    const { seg, eo, ei, eio, back } = AE;
    return { pop: t < 2.6 ? back(seg(t, .05, .32)) : 1 - eo(seg(t, 2.6, 2.8)), fl: eio(seg(t, .4, .7)) - eio(seg(t, 2.42, 2.6)),
      w: eo(seg(t, .6, .95)) - ei(seg(t, 2.12, 2.42)), tx: [seg(t, .8, 1) - seg(t, 2.05, 2.15)] };
  },
  rare(t) {
    const { seg, eo, ei, eio, back } = AE;
    const sh = t < 3.95 ? seg(t, 2.85, 3.95) : seg(t, 4.0, 4.9);
    return { pop: t < 5.6 ? back(seg(t, .03, .28)) : 1 - eo(seg(t, 5.65, 6.0)), fl: eio(seg(t, .83, 1.3)) - eio(seg(t, 5.42, 5.62)),
      w: eo(seg(t, 1.37, 2.03)) - ei(seg(t, 4.95, 5.4)), tx: [seg(t, 1.85, 2.15) - seg(t, 4.8, 4.95)],
      pitch: -1.45 + 1.1 * eio(seg(t, 1.3, 1.75)), spin: Math.max(0, t - 1) * 1.3, shine: sh > 0 && sh < 1 ? sh : -1 };
  },
  full(t) {
    const { seg, eo, ei, eio, back } = AE;
    const out = seg(t, 7.35, 7.8);
    return {
      pop: 1, fl: 0, w: eo(seg(t, 1.45, 1.9)) - ei(seg(t, 6.4, 6.9)),
      tx: [seg(t, 1.65, 2) - seg(t, 3.2, 3.4), seg(t, 3.45, 3.75) - seg(t, 4.85, 5), seg(t, 5.05, 5.35) - seg(t, 6.25, 6.4)],
      dot: (t < 6.9 ? .12 + .88 * eo(seg(t, 0, .55)) : 1 - .82 * eio(seg(t, 6.9, 7.35))) * (1 - out),
      dotOp: t < 2 ? 1 - .7 * seg(t, .8, 1.2) : t < 6.85 ? 0 : 1 - out,
      emb: t < .8 ? .35 * seg(t, .55, .8) : t < 1.95 ? .15 : t < 3.4 ? 0 : t < 7.2 ? 1 : 0,
      embScale: t < 1.95 ? .8 : t < 6.9 ? 1 : 1 - eio(seg(t, 6.9, 7.2)), embTurn: t >= 3.4 && t < 3.7 ? (1 - seg(t, 3.4, 3.7)) * -90 : 0,
      icon: t >= 1.95 && t < 3.55 ? 1 : 0, iconScale: t < 3.25 ? back(seg(t, 1.95, 2.25)) : 1, iconTurn: seg(t, 3.25, 3.55) * 90,
      shards: t > .8 && t < 2.05 ? { k: seg(t, .8, 1.1), c: seg(t, 1.8, 2.05) } : null,
      halo: t < 1.95 ? 0 : (seg(t, 1.95, 3) < .4 ? seg(t, 1.95, 3) / .4 : 1) * (1 - seg(t, 2.9, 3.7)), haloScale: .6 + .55 * eo(seg(t, 1.95, 3)),
      rays: seg(t, 2, 2.6) * (1 - seg(t, 6.2, 6.8))
    };
  }
};
/* Tamaños (en rem): disco y largo de la barra. */
const ACH_SIZE = { normal: { D: 7, W: 40 }, rare: { D: 7.7, W: 47 }, full: { D: 7.6, W: 40 } };

function achHTML(a) {
  const k = a.kind, { D, W } = ACH_SIZE[k], line = (b, s) => `<b>${esc(b)}</b><span>${esc(s)}</span>`;
  const texts = k === "normal" ? [line("Logro desbloqueado", a.game ? `${a.name} · ${a.game}` : a.name)]
    : k === "rare" ? [line("Logro raro desbloqueado", a.percent != null ? `${a.name} · ${pctText(a.percent)} lo tiene` : a.name)]
    : [line(a.name, a.percent != null ? `Último logro · ${pctText(a.percent)} lo tiene` : "Último logro"),
       `<span>Juego completado al 100 %</span><b>${esc(a.game || "")}</b>`,
       `<span>Desbloqueaste los ${a.total} logros</span><span>del juego</span>`];
  const disc = k === "full"
    ? `<div class="adot"></div><div class="aemb">${ACH_EMBLEM}</div>
       <div class="aicon">${a.icon ? `<img alt="" src="${esc(a.icon)}">` : `<i>${ACH_TROPHY}</i>`}</div>`
    : `<div class="aflip"><div class="aface"><i class="axb"></i></div><div class="aface aback">${k === "rare" ? '<svg class="agem" viewBox="-1.25 -1.25 2.5 2.5"></svg>' : `<i>${ACH_TROPHY}</i>`}</div></div>`;
  return `${k === "full" ? '<div class="ahalo"></div><div class="arays"></div>' : ""}
    <div class="apill" style="width:${W}rem;left:${D / 2}rem">${k === "rare" ? '<i class="ashine"></i>' : ""}${texts.map((h, i) => `<div class="atx" data-i="${i}" style="left:${D / 2 + 1.7}rem">${h}</div>`).join("")}</div>
    <div class="adisc" style="width:${D}rem;height:${D}rem">${disc}</div>${k === "full" ? `<svg class="ashards" viewBox="-60 -60 120 120" style="width:${D}rem;height:${D}rem"></svg>` : ""}`;
}

/* Dibuja el aviso a en el tiempo t (s). */
function achPaint(el, a, t) {
  const k = a.kind, f = ACH_FRAMES[k](t), { D, W } = ACH_SIZE[k], R = rem(), C = AE.clamp;
  const q = s => el.querySelector(s);
  // posición: el borde izquierdo, derecho o el centro se quedan fijos mientras la barra crece (como en Xbox)
  const vis = Math.max(D, D / 2 + C(f.w) * W) * R, pos = settings.achPos || "tl", m = 2.4 * R;
  const x = pos[1] === "l" ? m : pos[1] === "r" ? innerWidth - m - vis : (innerWidth - vis) / 2;
  el.style.transform = `translate(${x.toFixed(1)}px,0)`;
  el.style.top = pos[0] === "t" ? m + "px" : ""; el.style.bottom = pos[0] === "b" ? m + "px" : "";
  const pill = q(".apill");
  pill.style.clipPath = `inset(0 ${((1 - C(f.w)) * 100).toFixed(2)}% 0 0 round 99rem)`; pill.style.visibility = f.w > .001 ? "visible" : "hidden";
  el.querySelectorAll(".atx").forEach((x, i) => x.style.opacity = C(f.tx[i] || 0));
  const disc = q(".adisc"); disc.style.transform = `scale(${Math.max(0, f.pop)})`;
  if (k !== "full") {
    q(".aflip").style.transform = `rotateY(${(f.fl * 180).toFixed(1)}deg)`;
    if (k === "rare") {
      q(".agem").innerHTML = gemSVG(f.spin, f.pitch);
      const s = q(".ashine"); s.style.display = f.shine < 0 ? "none" : ""; if (f.shine >= 0) s.style.transform = `translateX(${(-16 + f.shine * (W + 20)).toFixed(2)}rem)`;
    }
    return f;
  }
  const dot = q(".adot"); dot.style.transform = `scale(${f.dot.toFixed(3)})`; dot.style.opacity = f.dotOp;
  const emb = q(".aemb"); emb.style.opacity = f.emb; emb.style.transform = `scale(${Math.max(0, f.embScale).toFixed(3)}) rotateY(${f.embTurn.toFixed(1)}deg)`;
  const ic = q(".aicon"); ic.style.opacity = f.icon; ic.style.transform = `scale(${f.iconScale.toFixed(3)}) rotateY(${f.iconTurn.toFixed(1)}deg)`;
  const halo = q(".ahalo"); halo.style.opacity = f.halo; halo.style.transform = `scale(${f.haloScale.toFixed(3)})`;
  q(".arays").style.opacity = f.rays;
  const sh = q(".ashards");
  if (!f.shards) sh.innerHTML = "";
  else { const { k: kk, c } = f.shards, Rr = 44 * AE.eo(kk) * (1 - AE.eio(c)) + 4; let h = "";
    for (let i = 0; i < 11; i++) { const an = i / 11 * Math.PI * 2 + t * 2.2 + (i % 2) * .4, sp = t * (3 + i % 3) + i, fx = Math.cos(sp), sc = (.6 + .5 * AE.eo(kk)) * (1 - .6 * c);
      const x2 = Math.cos(an) * Rr * (i % 3 === 0 ? .7 : 1), y2 = Math.sin(an) * Rr * .9, l = (150 + 105 * Math.abs(fx)) | 0;
      h += `<g transform="translate(${x2.toFixed(1)} ${y2.toFixed(1)}) rotate(${(an * 57 + sp * 40).toFixed(0)}) scale(${(fx * sc).toFixed(3)} ${sc.toFixed(3)})"><polygon points="-14,-6 4,-16 16,2 -2,14" fill="rgb(${l},${l},${Math.min(255, l + 8)})" stroke="#fff" stroke-width="1.2"/><polygon points="-14,-6 4,-16 2,0" fill="rgba(255,255,255,.6)"/></g>`; }
    sh.innerHTML = h; }
  return f;
}

/* ---- cola: uno a la vez; mientras se ve un logro, las demás notificaciones esperan ---- */
const Ach = {
  q: [], cur: null,
  get busy() { return !!this.cur; },
  push(a) { this.q.push(a); while (this.q.length > 8) this.q.shift(); if (!this.cur && !notifCur) this.next(); },   // si hay una notificación a la vista, empieza al irse
  next() {
    const a = this.q.shift(); this.cur = a || null;
    if (!a) { if (!notifCur) notifNext(); return; }
    const ms = ACH_MS[a.kind];
    achSound(a.kind);
    rumble("achieve");
    a.lumina = settings.lumina === true;   // (1.7.2) con Lúmina, el estilo de cristal (45-logros-lumina.js)
    if (mode === "game" && !OVERLAY && Host.has) {   // el menú está oculto: la app lo dibuja encima del juego, igualito
      Host.call("ach.show", { kind: a.kind, name: a.name, game: a.game || "", percent: a.percent, total: a.total || 0, image: a.iconPath || "", pos: settings.achPos || "tl",
        ...(a.lumina ? { style: "b", light: document.documentElement.dataset.theme === "light", unlocked: a.unlocked || 0 } : {}) }).catch(() => {});
      a.t = setTimeout(() => this.done(a), ms + 150);
      return;
    }
    const el = document.createElement("div");
    if (a.lumina) { el.className = "achl k-" + a.kind; el.innerHTML = achLHTML(a); }
    else { el.className = "ach k-" + a.kind; el.style.height = ACH_SIZE[a.kind].D + "rem"; el.innerHTML = achHTML(a); }
    document.body.append(el); a.el = el; a.start = performance.now();
    if (a.lumina) achLPlace(el);
    const step = () => {
      if (this.cur !== a || a.manual) return;
      const t = (performance.now() - a.start) / 1000;
      if (t >= ms / 1000) return this.done(a);
      achDraw(el, a, t); a.raf = requestAnimationFrame(step);
    };
    achDraw(el, a, 0); a.raf = requestAnimationFrame(step);
  },
  done(a) { if (this.cur !== a) return; clearTimeout(a.t); cancelAnimationFrame(a.raf); if (a.el) a.el.remove(); this.cur = null; this.next(); },
  /* pruebas: congela el aviso actual en el segundo t */
  seek(t) { const a = this.cur; if (!a || !a.el) return null; a.manual = true; cancelAnimationFrame(a.raf); return achDraw(a.el, a, t); }
};

const achDraw = (el, a, t) => a.lumina ? achLPaint(el, a, t) : achPaint(el, a, t);

/* Sonido: el de Scarlet o el importado (Personalizados), al volumen de su tipo. Con Lúmina, el raro y el 100 % tienen
   los suyos (achrare_l, ach100_l), hechos para su animación. */
function achSound(kind) {
  if (settings.nSound === false) return;
  const lum = settings.lumina === true && kind !== "normal" && settings.achSounds !== "custom" && Snd.custom[ACH_SND[kind] + "_l"];
  const key = ACH_SND[kind] + (lum ? "_l" : ""), vol = (settings["achVol_" + kind] ?? ACH_VOL[kind]) / 100;
  if (vol <= 0) return;
  const mine = settings.achSounds === "custom" && Snd.custom["x" + key] ? "x" + key : key;
  if (Snd.custom[mine] && Snd.sample(mine, 1, vol, true)) return;
  if (kind !== "normal" && Snd.custom.achieve && Snd.sample("achieve", 1, vol, true)) return;
  play("achieve");
}

function showAchievement(g, name, d = {}) {
  if (g && g.source === "xbox") return;   // los juegos de Xbox tienen su propio aviso de Windows
  if (settings.nAch === false) return;
  if (mode === "game" && !OVERLAY && (settings.nInGame || "all") === "none") { missed.push({ title: "Logro desbloqueado", text: g ? `${name} · ${g.name}` : name }); return; }
  const a = { name, game: g ? g.name : d.game || "", percent: d.percent ?? null, icon: d.icon || "", iconPath: d.iconPath || "",
    unlocked: d.unlocked || 0, total: d.total || 0, appid: g ? g.appid : 0 };
  a.kind = d.kind || achKind(a);
  if (a.kind === "full" && !a.total) a.total = g && g.ach ? g.ach[1] : 0;
  Ach.push(a);
}

/* Probar desde Ajustes */
function demoAchievement(kind) {
  const g = games.find(x => hasAch(x) && x.source !== "xbox") || games[0];
  const total = g && g.ach ? g.ach[1] : 40;
  showAchievement(g, kind === "full" ? "Protocolo EVE" : kind === "rare" ? "Sin un rasguño" : "Primer paso",
    { kind, percent: kind === "normal" ? 48.3 : 3.1, total, unlocked: kind === "full" ? total : 12 });
}

/* ---- Ajustes ---- */
const ACH_VOLS = [["Apagado", 0], ["10 %", 10], ["20 %", 20], ["25 %", 25], ["30 %", 30], ["40 %", 40], ["50 %", 50], ["60 %", 60], ["70 %", 70], ["75 %", 75], ["80 %", 80], ["90 %", 90], ["100 %", 100]];
const volLabel = v => v <= 0 ? "Apagado" : v + " %";
const achCustomCount = () => Object.keys(Snd.custom).filter(k => /^xach/.test(k)).length;
/* Tus sonidos (por ejemplo los de Xbox) se copian a los datos de Scarlet en tu consola; nunca van al repositorio. */
async function importAchSounds() {
  toast("Buscando tus sonidos de logros…");
  const r = await Host.call("sounds.importAch").catch(e => ({ ok: false, error: e.message }));
  if (r && r.ok) { settings.achSounds = "custom"; saveSettings(); toast(`Sonidos de logros importados (${r.count})`); }
  else toast((r && r.error) || "No se pudieron importar");
  const sv = views.find(x => x.isSettings); if (sv && sv.refresh) sv.refresh();
}
