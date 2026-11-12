/* ================= home ================= */
function renderUsers() { $("#users").innerHTML = `<button class="avatar" data-z="t" data-i="0" aria-label="Perfil">${avatarHTML()}${avatarFrame()}</button>`; }
function renderHome() {
  const track = $("#track"); track.innerHTML = "";
  homeGames().forEach((g, i) => {
    const b = buildTile(g); b.style.setProperty("--k", i); b.dataset.z = "g"; b.dataset.i = i; track.append(b);
  });
  const n = homeGames().length, all = document.createElement("button");
  all.className = "tile all"; all.style.setProperty("--k", n); all.dataset.z = "g"; all.dataset.i = n;
  all.innerHTML = I.all; all.setAttribute("aria-label", "Biblioteca");
  track.append(all);
  if (!$("#apps").children.length)
    $("#apps").innerHTML = APPS.map((a, i) => `<button class="app" data-z="a" data-i="${i}" style="${a.c ? "--c:" + a.c : ""}" aria-label="${esc(a.label)}">${a.icon}<span class="lbl">${esc(a.label)}</span></button>`).join("");
  home.g = Math.min(home.g, homeTiles().length - 1);
  updateFocus();
}
const homeTiles = () => [...$("#track").children];
function homeEl() {
  if (home.zone === "games") return homeTiles()[home.g];
  if (home.zone === "apps") return $("#apps").children[home.a];
  return $("#users").children[home.t];
}
const focusedGame = () => home.zone === "games" && home.g < homeGames().length ? homeGames()[home.g] : null;
const primaryLabel = g => g.appid === state.running ? "Reanudar" : !g.installed && !state.downloads[g.appid] ? "Instalar" : g.kind === "app" ? "Abrir" : "Iniciar";

/* Ícono junto al nombre: tarjeta (instalado), nube (por instalar) o tarjeta con flecha (descargando) + "40 %". */
const GT_CARD = '<svg class="gt-ic" viewBox="0 0 17 21" fill="currentColor"><path d="M2.2 0h12.6A2.2 2.2 0 0 1 17 2.2v16.6a2.2 2.2 0 0 1-2.2 2.2H2.2A2.2 2.2 0 0 1 0 18.8V2.2A2.2 2.2 0 0 1 2.2 0z"/><path d="M5 15.5h7v2.2H5z" fill="#1f1f1f"/></svg>';
const GT_DL = '<svg class="gt-ic" viewBox="0 0 17 21" fill="currentColor"><path d="M2.2 0h12.6A2.2 2.2 0 0 1 17 2.2v16.6a2.2 2.2 0 0 1-2.2 2.2H2.2A2.2 2.2 0 0 1 0 18.8V2.2A2.2 2.2 0 0 1 2.2 0z"/><path d="M5.3 14.7 8.5 17.5l3.2-2.8" fill="none" stroke="#1f1f1f" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"/></svg>';
const GT_CLOUD = '<svg class="gt-ic cloud" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 18a4.5 4.5 0 0 1-.6-9 6 6 0 0 1 11.5 1.6A3.8 3.8 0 0 1 17.5 18"/><path d="M12 11v8M9 16l3 3 3-3"/></svg>';
function paintTitleState() {
  const t = $("#gameTitle"); if (!t) return;
  const g = focusedGame(), d = g && state.downloads[g.appid];
  const kind = !g ? "card" : d ? "dl" : !g.installed ? "cloud" : "card";
  if (t.dataset.ic !== kind) { t.dataset.ic = kind; const ic = t.querySelector(".gt-ic"); if (ic) ic.outerHTML = kind === "dl" ? GT_DL : kind === "cloud" ? GT_CLOUD : GT_CARD; }
}
/* texto que se desplaza (como en la consola) si no cabe en su espacio */
function marquee(box, text, active) {
  box.dataset.t = text; box.classList.add("mqbox"); box.classList.remove("scroll");
  box.textContent = text;
  if (!active || box.scrollWidth <= box.clientWidth + 1) return;
  box.innerHTML = `<span class="mq"><span>${esc(text)}</span><span aria-hidden="true">${esc(text)}</span></span>`;
  const first = box.firstChild.firstChild, gap = parseFloat(getComputedStyle(box.firstChild).columnGap) || 0;
  const dist = first.offsetWidth + gap;
  box.style.setProperty("--mqd", -dist + "px");
  box.style.setProperty("--mqdur", (dist / (rem() * 5.5) / .84).toFixed(2) + "s");
  box.classList.add("scroll");
}
/* Táctil (beta 1.4): las ayudas de abajo («B Atrás», «A Aceptar», X, Y, ☰…) se pueden tocar. */
const GLYPH_KEY = { A: "a", B: "b", X: "x", Y: "y", "☰": "start", LB: "lb", RB: "rb", LT: "lt", RT: "rt", "◀": "left", "▶": "right" };
/* (1.8) Las ayudas dobles («◀▶ Ajustar», «LB RB ±5 s») también se tocan: cada mitad manda su botón. */
const GLYPH_PAIR = { "◀▶": [["◀", "left"], ["▶", "right"]], "LB RB": [["LB", "lb"], ["RB", "rb"]] };
const hintHTML = ([k, t]) => GLYPH_PAIR[k]
  ? `<span class="hint pair">${GLYPH_PAIR[k].map(([g, key]) => `<span class="glyph" data-key="${key}">${esc(g)}</span>`).join("")}${esc(t)}</span>`
  : `<span class="hint"${GLYPH_KEY[k] ? ` data-key="${GLYPH_KEY[k]}"` : ""}><span class="glyph">${esc(k)}</span>${esc(t)}</span>`;
function setHints(list) {
  const h = list.map(hintHTML).join("");
  if (h !== setHints.last) { $("#hints").innerHTML = h; setHints.last = h; }   // no se rehace si no cambió
}
/* (beta 1.7) Lúmina: el cursor de cristal se entera de cada cambio de foco. Apagado, esto no hace nada. */
let luminaKick = () => {};
function updateFocus() { updateFocus0(); luminaKick(); }
function updateFocus0() {
  $$(".focused").forEach(e => { e.classList.remove("focused"); const c = e.querySelector(".cap"); if (c) marquee(c, c.dataset.t || c.textContent, false); });
  const v = topView();
  if (v) {
    const items = v.items();
    if (items.length) {
      v.i = clamp(v.i, 0, items.length - 1);
      const el = items[v.i]; el.classList.add("focused");
      el.scrollIntoView({ block: "nearest", inline: "nearest", behavior: "smooth" });
      const cap = el.querySelector(".cap"); if (cap) marquee(cap, cap.dataset.t || cap.textContent, true);
      if (v.onFocus) v.onFocus(v.i);
    }
    setHints(typeof v.hints === "function" ? v.hints() : (v.hints || [["B", "Atrás"], ["A", "Aceptar"]]));
    return;
  }
  const el = homeEl(); if (!el) return;
  el.classList.add("focused");
  const title = $("#gameTitle");
  if (home.zone === "games") {
    const wrapW = $("#rowwrap").clientWidth, m = rem() * 4.6;
    let x = home.offset;
    const left = el.offsetLeft, right = left + el.offsetWidth;
    if (left - x < m) x = left - m;
    if (right - x > wrapW - m) x = right - wrapW + m;
    home.offset = Math.max(0, x);
    $("#track").style.transform = `translateX(${-home.offset}px)`;
    const g = focusedGame(), name = g ? g.name : "Biblioteca", txt = $("#gtText");
    paintTitleState();
    // el nombre centrado arriba de su portada; si no cabe en pantalla, se desplaza
    title.style.width = "auto"; marquee(txt, name, false);
    const natural = title.scrollWidth, edge = rem() * 4.6, maxW = wrapW - 2 * edge;
    const w = Math.min(natural, maxW), center = left - home.offset + el.offsetWidth / 2;
    const tx = clamp(center - w / 2, edge, wrapW - edge - w);
    title.style.width = w + "px"; if (natural > maxW) marquee(txt, name, true);
    title.style.transform = `translateX(${tx}px)`; title.style.opacity = 1;
    setHints(!g ? [["A", "Abrir"]] : g.appid === state.running ? [["☰", "Opciones"], ["X", "Cerrar"], ["A", "Reanudar"]] : [["X", "Opciones"], ["A", primaryLabel(g)]]);
  } else {
    title.style.opacity = 0;
    setHints(home.zone === "apps" ? [["A", "Abrir"]] : [["A", "Ver perfil"]]);
  }
}

