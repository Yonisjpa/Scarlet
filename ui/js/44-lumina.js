/* ===================== beta 1.7: Lúmina =====================
   Estilo de cristal (Ajustes › Temas › Lúmina). Apagado no hace nada: ni estilos (13-lumina.css cuelga de data-lumina),
   ni el cursor de cristal, ni un solo cuadro de animación. Encendido:
   · los paneles se vuelven de cristal (solo CSS),
   · el cursor es una gota que viaja entre opciones: el borde que va delante sale primero y el de atrás se estira,
   · y, si se deja la refracción, el borde de la gota dobla lo que tiene debajo como una lente de verdad
     (un mapa de desplazamiento en un filtro SVG, aplicado con backdrop-filter). */
const Lumina = (() => {
  const root = document.documentElement;
  const RIM = 26, BEND = 20;                       // ancho del borde que refracta y cuánto dobla, en px a 1080p
  const S = { l: 0, t: 0, r: 0, b: 0 }, V = { l: 0, t: 0, r: 0, b: 0 }, K = { l: 320, t: 320, r: 320, b: 320 };
  const EDGES = ["l", "t", "r", "b"];
  const maps = new Map();                          // mapas de desplazamiento ya hechos, por tamaño
  let lens = null, feImg = null, feDisp = null, el = null, raf = 0, until = 0, since = 0, last = 0, snapUntil = 0, rad = 16, mapKey = "", watch = 0;

  const on = () => settings.lumina === true;
  const canRefract = () => typeof CSS !== "undefined" && CSS.supports && (CSS.supports("backdrop-filter", "url(#a)") || CSS.supports("-webkit-backdrop-filter", "url(#a)"));
  const refracting = () => on() && !OVERLAY && settings.luminaRefract !== false && canRefract();

  function ensure() {
    if (lens) return;
    lens = document.createElement("div"); lens.id = "lumi"; lens.setAttribute("aria-hidden", "true");
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("class", "lumi-svg"); svg.setAttribute("width", "0"); svg.setAttribute("height", "0"); svg.style.cssText = "position:absolute;width:0;height:0;pointer-events:none";
    svg.innerHTML = `<filter id="lumi-f" x="0" y="0" width="100%" height="100%" color-interpolation-filters="sRGB">
      <feImage x="0" y="0" width="8" height="8" preserveAspectRatio="none" result="m"/>
      <feDisplacementMap in="SourceGraphic" in2="m" scale="0" xChannelSelector="R" yChannelSelector="G"/></filter>`;
    feImg = svg.querySelector("feImage"); feDisp = svg.querySelector("feDisplacementMap");
    document.body.append(svg, lens);
  }

  /* El mapa: gris neutro en el centro (no mueve nada) y, hacia el borde, cada punto toma la imagen de más adentro,
     siguiendo la forma redondeada. Se dibuja a media resolución y se guarda por tamaño. */
  /* (1.7.1) Qué tanto dobla, según el tamaño de lo enfocado: en una portada, todo; en la foto de perfil o un ícono chico,
     casi nada (antes la foto se veía como ojo de pez). 1 = tamaño completo, a partir de ~260 px de lado a 1080p. */
  const lensFit = (w, h) => Math.max(.12, Math.min(1, Math.min(w, h) / (26 * rem())));
  function buildMap(w, h, r) {
    const sc = .5, cw = Math.max(8, Math.round(w * sc)), ch = Math.max(8, Math.round(h * sc)), fit = lensFit(w, h);
    const c = document.createElement("canvas"); c.width = cw; c.height = ch;
    const ctx = c.getContext("2d"), img = ctx.createImageData(cw, ch), d = img.data;
    const rim = Math.min(RIM * sc * (rem() / 10) * fit, cw / 2, ch / 2), rr = Math.min(r * sc, cw / 2, ch / 2), hx = cw / 2 - rr, hy = ch / 2 - rr;
    for (let y = 0, i = 0; y < ch; y++) for (let x = 0; x < cw; x++, i += 4) {
      const px = x + .5 - cw / 2, py = y + .5 - ch / 2, qx = Math.abs(px) - hx, qy = Math.abs(py) - hy;
      const ox = Math.max(qx, 0), oy = Math.max(qy, 0), len = Math.hypot(ox, oy);
      const depth = rr - len - Math.min(Math.max(qx, qy), 0);          // qué tan adentro está (0 = justo en el borde)
      let nx = 0, ny = 0, m = 0;
      if (depth < rim && depth >= 0) {
        if (len > 0) { nx = ox / len * Math.sign(px); ny = oy / len * Math.sign(py); } else if (qx > qy) nx = Math.sign(px); else ny = Math.sign(py);
        const t = 1 - depth / rim; m = t * t;
      }
      d[i] = 128 - nx * m * 127; d[i + 1] = 128 - ny * m * 127; d[i + 2] = 128; d[i + 3] = 255;
    }
    ctx.putImageData(img, 0, 0);
    return c.toDataURL();
  }
  function useMap(w, h, r) {
    const key = `${Math.round(w / 4) * 4}x${Math.round(h / 4) * 4}x${Math.round(r)}`;
    if (key === mapKey) return; mapKey = key;
    let url = maps.get(key);
    if (!url) { url = buildMap(w, h, r); maps.set(key, url); if (maps.size > 24) maps.delete(maps.keys().next().value); }
    const im = new Image(); im.src = url;          // se pone cuando ya está lista: así nunca se ve un cuadro sin mapa
    const bend = 2 * BEND * (rem() / 10) * Math.pow(lensFit(w, h), 1.5);
    const put = () => { if (mapKey === key && feImg) { feImg.setAttribute("href", url); feDisp.setAttribute("scale", String(bend)); } };
    if (im.decode) im.decode().then(put, put); else im.onload = put;
  }

  const gap = () => rem() * .68;
  const rectOf = e => { const q = e.getBoundingClientRect(), g = gap(); return { l: q.left - g, t: q.top - g, r: q.right + g, b: q.bottom + g }; };
  const zoneOf = e => e.closest(".view,#quick,main,header");
  function pick() {
    if (typeof mode !== "undefined" && mode !== "ui") return null;
    const all = document.querySelectorAll(".focused");
    for (let i = all.length - 1; i >= 0; i--) if (all[i].getClientRects().length) return all[i];
    return null;
  }
  function retarget(n, now) {
    const far = !el || !el.isConnected || !n || zoneOf(n) !== zoneOf(el) || lens.style.opacity !== "1";
    el = n; if (!n) return;
    const T = rectOf(n), cs = getComputedStyle(n).borderTopLeftRadius, h = T.b - T.t, w = T.r - T.l;
    rad = Math.min((cs.endsWith("%") ? h : parseFloat(cs) || 10) + gap(), h / 2, w / 2);
    lens.classList.toggle("hollow", n.matches(".tile:not(.all),.avatar,.shot"));
    if (far) {                                     // otra pantalla o panel: aparece ahí, sin cruzar toda la pantalla
      Object.assign(S, T); for (const k of EDGES) V[k] = 0; snapUntil = now + 520;
      if (lens.animate) lens.animate([{ opacity: 0, scale: .92 }, { opacity: 1, scale: 1.03 }, { opacity: 1, scale: 1 }], { duration: 300, easing: "ease-out" });
    } else {                                       // misma zona: viaja. El borde que va delante corre; el de atrás se queda
      const dx = (T.l + T.r) / 2 - (S.l + S.r) / 2, dy = (T.t + T.b) / 2 - (S.t + S.b) / 2, FAST = 520, SLOW = 130;
      K.r = dx > 0 ? FAST : SLOW; K.l = dx < 0 ? FAST : SLOW; K.b = dy > 0 ? FAST : SLOW; K.t = dy < 0 ? FAST : SLOW;
      if (Math.abs(dx) < 2) K.l = K.r = 320; if (Math.abs(dy) < 2) K.t = K.b = 320;
      snapUntil = 0;
    }
    since = now;
    if (refracting()) useMap(w, h, rad);
  }
  function tick(now) {
    raf = 0;
    if (!on() || !lens) return;
    const dt = Math.min(.032, Math.max(.001, (now - last) / 1000)); last = now;
    const n = pick();
    if (n !== el || (n && !el.isConnected)) retarget(n, now);
    let moving = false;
    if (el && el.isConnected) {
      const T = rectOf(el), snap = now < snapUntil || now - since > 900;
      for (const k of EDGES) {
        if (snap) { S[k] = T[k]; V[k] = 0; continue; }
        const k0 = K[k], c = 2 * Math.sqrt(k0) * .62;
        V[k] += (k0 * (T[k] - S[k]) - c * V[k]) * dt; S[k] += V[k] * dt;
        if (Math.abs(V[k]) > 4 || Math.abs(T[k] - S[k]) > .5) moving = true;
      }
      const w = Math.max(8, S.r - S.l), h = Math.max(8, S.b - S.t), sp = Math.min(1, (Math.abs(V.l) + Math.abs(V.r) + Math.abs(V.t) + Math.abs(V.b)) / 5000);
      const st = lens.style;
      st.width = w + "px"; st.height = h + "px"; st.transform = `translate(${S.l}px,${S.t}px)`;
      st.borderRadius = Math.min(rad + sp * 10, h / 2, w / 2) + "px";
      st.setProperty("--go", sp.toFixed(2)); st.setProperty("--gl", (150 - Math.min(1, (now - since) / 450) * 200) + "%");
      st.opacity = "1";
      if (feImg) { feImg.setAttribute("width", w); feImg.setAttribute("height", h); }
    } else if (lens.style.opacity !== "0") lens.style.opacity = "0";
    if (moving || now < until) raf = requestAnimationFrame(tick);
  }
  /* Se llama cuando cambia el foco o se mueve algo: la gota sigue a su opción un rato y luego deja de gastar. */
  function kick(ms = 1300) {
    if (!on() || !lens) return;
    until = Math.max(until, performance.now() + ms);
    if (!raf) { last = performance.now(); raf = requestAnimationFrame(tick); }
  }
  /* Red de seguridad: si la opción enfocada se movió sin avisar (una lista que se recorre sola, un panel que cambia), la alcanza. */
  function check() {
    if (raf || document.hidden) return;
    const n = pick();
    if (n !== el) return kick(600);
    if (!n) return;
    const T = rectOf(n);
    if (Math.abs(T.l - S.l) > 1 || Math.abs(T.t - S.t) > 1 || Math.abs(T.r - S.r) > 1 || Math.abs(T.b - S.b) > 1) kick(600);
  }
  /* (1.7.1) Encima del juego el menú es una ventana aparte: con Lúmina el panel flota redondeado, así que la ventana (y su
     fondo al 50 %) toman esa misma forma. Si no, detrás se veía el rectángulo de siempre. */
  function shape() {
    if (!OVERLAY || !Host.has) return;
    const dpr = window.devicePixelRatio || 1;
    Host.call("quick.shape", on() ? { inset: Math.round(1.8 * rem() * dpr), radius: Math.round(3.4 * rem() * dpr) } : { off: true }).catch(() => {});
  }
  function apply() {
    const o = on();
    root.toggleAttribute("data-lumina", o);
    shape();
    if (!o) {
      delete root.dataset.lens; clearInterval(watch); watch = 0; if (raf) cancelAnimationFrame(raf); raf = 0; el = null;
      if (lens) lens.style.opacity = "0";
      return;
    }
    ensure();
    root.dataset.lens = refracting() ? "refract" : "on";
    mapKey = ""; el = null;
    if (!watch) watch = setInterval(check, 400);
    kick();
  }
  addEventListener("scroll", () => kick(500), { capture: true, passive: true });
  addEventListener("resize", () => { maps.clear(); mapKey = ""; el = null; kick(); shape(); });
  /* El menú encima del juego es otra página: cuando cambias el tema o Lúmina en Ajustes, se entera aquí. */
  if (OVERLAY) addEventListener("storage", e => {
    if (e.key !== "scarlet.settings") return;
    const s = store.get("settings", {});
    for (const k of ["theme", "lumina", "luminaRefract"]) if (k in s) settings[k] = s[k]; else delete settings[k];
    applyTheme(); apply();
  });
  return { apply, kick, on, canRefract, get el() { return lens; }, get target() { return el; } };
})();
luminaKick = () => Lumina.kick();

/* Batería con Lúmina: el aviso dice lo mismo que siempre, pero se entiende de un vistazo (color, porcentaje en grande, nivel). */
function luminaBatt(o) {
  const lv = Math.max(0, Math.min(1, o.batt)), pct = Math.round(lv * 100);
  if (o.chg) return { cls: "b-chg", tag: "Cargando", title: "Cargador conectado", text: lv >= .995 ? "Carga completa" : battEstimate().replace(/^Cargando$/, `Batería al ${pct} %`), pct };
  if (lv <= .1) return { cls: "b-crit", tag: "Batería crítica", title: "Guarda tu partida", text: "La consola se apagará pronto", pct };
  return { cls: "b-low", tag: "Batería baja", title: "Conecta el cargador", text: battEstimate(), pct };
}

if (document.body) Lumina.apply(); else addEventListener("DOMContentLoaded", () => Lumina.apply());
