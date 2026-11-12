/* ===================== beta 1.7.2: logros con Lúmina =====================
   Con Lúmina encendido los logros salen con el estilo de los avisos de cristal (bloque a la izquierda, cuerpo de cristal,
   tira de color), pero cada uno con su propia animación y del largo exacto de su sonido:
   - normal (2.8 s): el ícono del logro da un brinco al golpe del sonido, pasa un brillo dorado y sale una onda; tira dorada.
   - raro (6.0 s): cae un diamante que gira mientras el porcentaje baja de 100 % al real; en el segundo golpe se voltea y
     revela el ícono del logro con un diamantito, salen esquirlas de cristal y ondas azules. Azul eléctrico.
   - 100 % (7.8 s): un anillo de progreso y el contador sube de 0 a 100 % (tambores, brasas que suben); al golpe, destello,
     ondas, chispas y fanfarria; se voltea al ícono del último logro y dice «Juego completado». Rojo → amarillo.
   Cada cuadro sale de achLPaint(el, a, t): sirve para animar, para las pruebas (Ach.seek) y es lo que copia la app
   (AchievementToast.cs, DrawB) para dibujarlos encima de los juegos. Sin Lúmina: los de estilo Xbox (43-logros.js). */
const ACHL = {
  normal: { cols: ["#d99a00", "#ffc93c", "#fff1a8"], tag: "#e0a800", glow: "255,190,40", hit: .3, exit: 2.45,
    ring: a => `conic-gradient(from ${a}deg,#d99a00,#ffc93c,#fff1a8,#ffc93c,#d99a00)` },
  rare: { cols: ["#0a2bff", "#00a8ff", "#8fe3ff"], tag: "#2f9dff", glow: "0,140,255", hit: 2.4, exit: 5.6,
    ring: a => `conic-gradient(from ${a}deg,#0a2bff,#00a8ff,#8fe3ff,#ffffff,#00a8ff,#0a2bff,#7a5cff,#0a2bff)` },
  full: { cols: ["#ff1a1a", "#ff7a00", "#ffd400"], tag: "#ff6a1a", glow: "255,110,0", hit: 2.6, exit: 7.2, hit2: 3.46,
    ring: a => `conic-gradient(from ${a}deg,#ff1a1a,#ff7a00,#ffd400,#fff6b0,#ffd400,#ff7a00,#ff1a1a,#ff004c,#ff1a1a)` }
};
const ACHL_GEM = `<svg viewBox="0 0 48 48"><path d="M14 8h20l8 10-18 24L6 18z" fill="rgba(255,255,255,.22)" stroke="#fff" stroke-width="2.4" stroke-linejoin="round"/><path d="M6 18h36M14 8l10 34M34 8 24 42M20 18l4-10 4 10" fill="none" stroke="#fff" stroke-width="1.6" stroke-linejoin="round" opacity=".9"/></svg>`;
const ACHL_RING = `<svg viewBox="0 0 64 64"><circle cx="32" cy="32" r="24" fill="none" stroke="rgba(255,255,255,.22)" stroke-width="6"/><circle class="alprog" cx="32" cy="32" r="24" fill="none" stroke="#fff" stroke-width="6" stroke-linecap="round" stroke-dasharray="150.8" stroke-dashoffset="150.8" transform="rotate(-90 32 32)"/></svg>`;
const achLTrophy = kind => `<div class="alnoicon" style="background:linear-gradient(150deg,${ACHL[kind].cols.join(",")})">${ACH_TROPHY}</div>`;

/* Partículas: siempre las mismas para el mismo logro (así las pruebas y la app dibujan igual). */
function achLParts(kind) {
  let s = kind === "rare" ? 7 : 23; const rnd = () => (s = (s * 16807) % 2147483647) / 2147483647, out = [];
  if (kind === "rare") for (let i = 0; i < 14; i++) out.push({ a: i / 14 * Math.PI * 2 + rnd() * .3, d: 13 + rnd() * 14, s: .7 + rnd() * .9, r: rnd() * 360, t0: 2.4 + rnd() * .06 });
  if (kind === "full") {
    for (let i = 0; i < 34; i++) out.push({ spark: 1, a: rnd() * Math.PI * 2, d: 16 + rnd() * 26, s: .5 + rnd() * .9, t0: 2.6 + rnd() * .05, c: rnd() });
    for (let i = 0; i < 40; i++) out.push({ ember: 1, x: rnd(), t0: .9 + rnd() * 5.6, life: 1.1 + rnd() * 1.1, h: 6 + rnd() * 10, dx: (rnd() - .5) * 5, s: .35 + rnd() * .55, c: rnd(), top: rnd() < .5 });
  }
  return out;
}

function achLTexts(a) {
  const pct = pctText(a.percent), game = a.game || "";
  if (a.kind === "rare") return { tag: "Logro raro", title: a.name, sub: [game, pct && `lo tiene el ${pct} de los jugadores`].filter(Boolean).join(" · ") };
  if (a.kind === "full") return { tag: "Juego completado", title: game ? `${game} al 100 %` : "Juego al 100 %", sub: `Último logro: ${a.name}${pct ? " · " + pct : ""}`,
    pre: { tag: "Logros", title: game || "Juego", sub: "Completando el juego…" } };
  return { tag: "Logro", title: a.name, sub: [game, a.total ? `${Math.min(a.unlocked || 0, a.total)} de ${a.total}` : ""].filter(Boolean).join(" · ") };
}

function achLHTML(a) {
  const k = ACHL[a.kind], tx = achLTexts(a), icon = a.icon ? `<img alt="" src="${esc(a.icon)}">` : achLTrophy(a.kind);
  const front = a.kind === "rare" ? ACHL_GEM : a.kind === "full" ? ACHL_RING : "";
  a.parts = achLParts(a.kind);
  return `<div class="alfx"></div>
    <div class="notif alb" style="--g1:${k.cols[0]};--g2:${k.cols[2]}">
      <div class="ic alic" style="background:linear-gradient(150deg,${k.cols[0]},${k.cols[1]} 55%,${k.cols[2]})"><div class="alflip"><div class="alface fr">${front}</div><div class="alface bk">${icon}</div></div>${a.kind === "rare" ? `<span class="albadge">${ACHL_GEM}</span>` : ""}</div>
      <div class="ntx altx"><div class="almain"><span class="ntag" style="color:${k.tag}">${esc(tx.tag)}</span><b>${esc(tx.title)}</b><span>${esc(tx.sub)}</span></div>
        ${tx.pre ? `<div class="alpre"><span class="ntag" style="color:${k.tag}">${esc(tx.pre.tag)}</span><b>${esc(tx.pre.title)}</b><span>${esc(tx.pre.sub)}</span></div>` : ""}</div>
      ${a.kind === "normal" ? "" : `<span class="alpct"></span>`}
      <div class="nlife" style="background:linear-gradient(90deg,${k.cols.join(",")})"></div>
      <div class="alshine"></div><div class="alflash"></div>
    </div><div class="albd"></div><div class="alfx2">${a.parts.map(() => "<i></i>").join("")}</div>`;
}

/* El cuadro t (segundos). Regresa un resumen (para las pruebas). */
function achLPaint(el, a, t) {
  const kind = a.kind, k = ACHL[kind], R = rem(), C = AE.clamp, U = AE.seg, eo = AE.eo, back = AE.back, hit = k.hit, dur = ACH_MS[kind] / 1000;
  const q = s => el.querySelector(s), n = q(".alb"), pos = settings.achPos || "tl";
  // entrada desde su orilla, golpe, salida
  const pin = U(t, 0, kind === "normal" ? .45 : .5), side = pos[1] === "r" ? 1 : pos[1] === "c" ? 0 : -1;
  let x = pin < 1 ? side * (110 - 110 * back(pin)) : 0, y = 0, sc = side === 0 && pin < 1 ? .6 + .4 * back(pin) : 1, op = C(pin * 2.2);
  const pu = U(t, hit, hit + (kind === "normal" ? .35 : .4));
  if (t >= hit && pu < 1) sc *= 1 + (kind === "normal" ? .035 : kind === "rare" ? .06 : .1) * Math.sin(pu * Math.PI) * (1 - pu * .4);
  if (kind === "full") { const p2 = U(t, k.hit2, k.hit2 + .34); if (t >= k.hit2 && p2 < 1) sc *= 1 + .045 * Math.sin(p2 * Math.PI);
    const sh = U(t, 1.5, hit); if (t < hit && sh > 0) { x += Math.sin(t * 91) * sh * .5; y += Math.cos(t * 77) * sh * .28; } }
  const po = U(t, k.exit, dur); if (po > 0) { y += (pos[0] === "b" ? 1.2 : -1.2) * po; sc *= 1 - .14 * po; op *= 1 - po; }
  const ox = pos[1] === "l" ? "left" : pos[1] === "r" ? "right" : "center", oy = pos[0] === "b" ? "bottom" : "top";
  el.style.transformOrigin = `${ox} ${oy}`; el.style.transform = `translate(${x.toFixed(2)}%,${y.toFixed(2)}rem) scale(${sc.toFixed(4)})`; el.style.opacity = op.toFixed(3);
  // resplandor
  const at = (t0, r) => t >= t0 ? Math.exp(-(t - t0) * r) : 0;
  const pulse = kind === "normal" ? at(hit, 3) : Math.max(kind === "rare" ? at(.45, 2.2) : 0, at(hit, 1.6), kind === "full" ? at(k.hit2, 2.4) * .8 : 0);
  const base = kind === "normal" ? .12 : t >= hit ? .45 + .12 * Math.sin(t * 5) : .2 + (kind === "full" ? U(t, .5, hit) * .5 : 0);
  n.style.boxShadow = `inset 0 1.5px 0 var(--lm-hi),inset 0 0 0 1px var(--lm-edge),0 1.4rem 3.6rem rgba(0,0,0,.38),0 0 ${(kind === "normal" ? 2 + 3 * pulse : 3 + 5 * pulse).toFixed(2)}rem ${(kind === "normal" ? .1 + .6 * pulse : .2 + 1.2 * pulse).toFixed(2)}rem rgba(${k.glow},${C(base + pulse * (kind === "normal" ? .55 : .6)).toFixed(3)})`;
  // borde que gira
  const bd = q(".albd"); bd.style.background = k.ring(((t * (kind === "rare" ? 150 : kind === "full" ? 210 : 120)) % 360).toFixed(1));
  bd.style.opacity = (kind === "normal" ? C(pulse * 1.2) * .9 : kind === "rare" ? C(.35 * U(t, .45, .7) + .65 * U(t, hit, hit + .2)) : C(.3 * U(t, .6, hit) + .7 * U(t, hit, hit + .15))).toFixed(3);
  // bloque: frente (diamante o progreso) y reverso (ícono del logro)
  const fl = q(".alflip"), fr = q(".alface.fr svg"); let flip = 0, pct = null, prog = 0;
  if (kind === "normal") { const p = U(t, hit, hit + .4); fl.style.transform = `rotateY(180deg) scale(${(t < hit ? .82 + .18 * U(t, 0, hit) : 1 + .2 * Math.sin(p * Math.PI) * (1 - p * .5)).toFixed(4)})`; flip = 1; }
  else if (kind === "rare") {
    const app = U(t, .45, .78), spin = AE.eio(U(t, .6, 2.3)) * 720; flip = eo(U(t, hit - .06, hit + .22));
    fr.style.opacity = C(app * 4).toFixed(3); fr.style.transform = `scale(${(app < 1 ? 2.6 - 1.6 * back(app) : 1).toFixed(4)})`;
    fl.style.transform = `rotateY(${(spin + flip * 180).toFixed(1)}deg) scale(${(1 + .14 * Math.sin(U(t, hit + .05, hit + .45) * Math.PI)).toFixed(4)})`;
    const bp = U(t, hit + .22, hit + .55); q(".albadge").style.transform = `scale(${(bp <= 0 ? 0 : back(bp)).toFixed(4)})`;
    prog = U(t, .62, 2.22); pct = prog >= 1 || a.percent == null ? a.percent : 100 - (100 - a.percent) * (1 - Math.pow(1 - prog, 2.2));
  } else {
    prog = Math.pow(U(t, .55, hit), 1.6); q(".alprog").setAttribute("stroke-dashoffset", (150.8 * (1 - prog)).toFixed(1));
    flip = eo(U(t, hit, hit + .28)); fl.style.transform = `rotateY(${(flip * 180).toFixed(1)}deg) scale(${(1 + .16 * Math.sin(U(t, hit + .05, hit + .5) * Math.PI)).toFixed(4)})`;
    pct = Math.round(100 * prog);
  }
  // textos
  const main = q(".almain"), pre = q(".alpre");
  if (kind === "full") { const u = U(t, hit, hit + .22); main.style.opacity = u.toFixed(3); main.style.transform = `translateY(${((1 - eo(u)) * .8).toFixed(3)}rem)`; pre.style.opacity = (C(U(t, .45, .8)) * (1 - u)).toFixed(3); }
  else [...main.children].forEach((e, i) => { const d0 = kind === "normal" ? .22 : .5, d1 = kind === "normal" ? .5 : .85, u = eo(U(t, d0 + i * (kind === "normal" ? .07 : .09), d1 + i * (kind === "normal" ? .07 : .09))); e.style.opacity = u.toFixed(3); e.style.transform = `translateX(${((1 - u) * (kind === "normal" ? 1.4 : 1.6)).toFixed(3)}rem)`; });
  // porcentaje grande
  const pc = q(".alpct");
  if (pc) {
    pc.textContent = pct == null ? "" : kind === "full" ? pct + " %" : pctText(pct);
    pc.style.opacity = C(U(t, kind === "rare" ? .55 : .45, kind === "rare" ? .8 : .7)).toFixed(3);
    const pp = U(t, hit, hit + .45), pp2 = kind === "full" ? U(t, k.hit2, k.hit2 + .34) : 1;
    pc.style.transform = `scale(${(1 + (t >= hit && pp < 1 ? .4 * (1 - eo(pp)) : 0) + (kind === "full" && t >= k.hit2 && pp2 < 1 ? .16 * Math.sin(pp2 * Math.PI) : 0)).toFixed(4)})`;
    pc.classList.toggle("lit", t >= hit);
  }
  // tira: se llena con la cuenta y luego es el tiempo que queda
  const life = q(".nlife"), lv = kind === "normal" ? 1 - U(t, .45, k.exit) : t < hit + .3 ? prog : 1 - U(t, hit + .3, k.exit);
  life.style.transform = `scaleX(${C(lv).toFixed(4)})`;
  // destellos y brillo que cruza
  const f1 = kind === "rare" && t >= .45 ? Math.exp(-(t - .45) * 9) * .55 : 0, f2 = kind === "normal" ? pulse * .35 : at(hit, 5.5) * .95;
  q(".alflash").style.opacity = C(Math.max(f1, f2)).toFixed(3);
  const sweeps = kind === "normal" ? [hit + .05] : [hit + .15, hit + 1.7, hit + 3.3], sw = sweeps.map(s => U(t, s, s + (kind === "normal" ? .65 : .75))).find(u => u > 0 && u < 1), sh = q(".alshine");
  sh.style.opacity = sw == null ? "0" : kind === "normal" ? ".8" : "1"; if (sw != null) sh.style.left = `${(-25 + sw * 135).toFixed(2)}%`;
  // ondas que salen del bloque
  const ic = q(".alic"), cx = ic.offsetLeft + ic.offsetWidth / 2, cy = ic.offsetTop + ic.offsetHeight / 2;
  const rings = kind === "normal" ? [[hit, 40, k.cols[1], .6]] : kind === "rare" ? [[.45, 30, k.cols[1], .75], [hit, 52, k.cols[2], .95], [hit + .1, 38, "#ffffff", .7]]
    : [[hit, 46, k.cols[0], .95], [hit + .1, 62, k.cols[2], .85], [hit + .2, 34, "#ffffff", .7], [k.hit2, 40, k.cols[1], .7]];
  q(".alfx").innerHTML = rings.map(([t0, rad, col, o]) => { const u = U(t, t0, t0 + (kind === "normal" ? .8 : .9)); if (u <= 0 || u >= 1) return ""; const r = eo(u) * rad;
    return `<i style="left:${cx}px;top:${cy}px;width:${(r * 2).toFixed(2)}rem;height:${(r * 2).toFixed(2)}rem;border-color:${col};opacity:${(o * (1 - u)).toFixed(2)};box-shadow:0 0 2rem ${col},inset 0 0 2rem ${col}"></i>`; }).join("");
  // partículas
  const els = el.querySelectorAll(".alfx2 i"), W = n.offsetWidth, H = n.offsetHeight; let shown = 0;
  a.parts.forEach((p, i) => {
    const s = els[i].style; let css = "display:none";
    if (p.ember) { const u = (t - p.t0) / p.life; if (u > 0 && u < 1) { const px = p.x * W + p.dx * R * u, py = p.top ? -p.h * R * eo(u) * .8 : H + p.h * R * eo(u) * .35, col = p.c < .4 ? "#ff3b1f" : p.c < .75 ? "#ff9a1f" : "#ffe14a";
        css = `width:${p.s}rem;height:${p.s}rem;border-radius:50%;background:${col};box-shadow:0 0 ${p.s * 1.6}rem ${col};opacity:${(Math.sin(u * Math.PI) * (t < hit ? U(t, .9, hit) : 1)).toFixed(2)};transform:translate(${px.toFixed(1)}px,${py.toFixed(1)}px)`; } }
    else if (p.spark) { const u = (t - p.t0) / .95; if (u > 0 && u < 1) { const d = eo(u) * p.d * R, px = cx + Math.cos(p.a) * d, py = cy + Math.sin(p.a) * d + u * u * 5 * R, col = p.c < .35 ? "#ff3b1f" : p.c < .7 ? "#ffb300" : "#fff3a0";
        css = `width:${(2.2 * p.s * (1 - u * .6)).toFixed(2)}rem;height:.32rem;border-radius:.2rem;background:linear-gradient(90deg,transparent,${col});box-shadow:0 0 1rem ${col};opacity:${(1 - u * u).toFixed(2)};transform:translate(${px.toFixed(1)}px,${py.toFixed(1)}px) rotate(${p.a.toFixed(3)}rad)`; } }
    else { const u = (t - p.t0) / .95; if (u > 0 && u < 1) { const d = eo(u) * p.d * R, px = cx + Math.cos(p.a) * d, py = cy + Math.sin(p.a) * d;
        css = `width:${(1.5 * p.s).toFixed(2)}rem;height:${(1.9 * p.s).toFixed(2)}rem;clip-path:polygon(50% 0,100% 35%,50% 100%,0 35%);background:linear-gradient(160deg,#fff,#8fe3ff 45%,#00a8ff);filter:drop-shadow(0 0 .5rem #8fe3ff);opacity:${(1 - u * u).toFixed(2)};transform:translate(${px.toFixed(1)}px,${py.toFixed(1)}px) rotate(${(p.r + u * 260).toFixed(0)}deg)`; } }
    if (css !== "display:none") shown++;
    s.cssText = css;
  });
  return { flip, pct, prog, sc, op, pulse, parts: shown, life: lv };
}

/* Coloca el contenedor en la esquina elegida. */
function achLPlace(el) {
  const pos = settings.achPos || "tl";
  el.style.top = pos[0] === "t" ? "2.4rem" : ""; el.style.bottom = pos[0] === "b" ? "2.4rem" : "";
  el.style.left = pos[1] === "l" ? "2.4rem" : pos[1] === "c" ? "50%" : ""; el.style.right = pos[1] === "r" ? "2.4rem" : "";
  el.style.marginLeft = pos[1] === "c" ? `-${el.offsetWidth / 2}px` : "";
}
