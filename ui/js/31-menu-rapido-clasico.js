const POWER = [["silent", "Silencioso", "13 W"], ["perf", "Rendimiento", "17 W"], ["turbo", "Turbo", "25 W"]];
const sys = Object.assign({ brightness: 70, volume: 55, power: "perf", wifi: true, airplane: false, autoBri: false }, store.get("sys", {}));
const saveSys = () => store.set("sys", sys);
/* Lo que manda Windows, sin los datos que no pudo leer ("Volumen null" en el menú). */
function mergeSys(st) { for (const k in st) if (st[k] != null && k in sys) sys[k] = st[k]; }
for (const k of ["brightness", "volume"]) if (typeof sys[k] !== "number" || !isFinite(sys[k])) sys[k] = k === "brightness" ? 70 : 55;
const QM = { open: false, i: 0 };
const qmRowsClassic = () => [
  ...(media ? [{ k: "media", type: "media" }] : []),
  { k: "brightness", type: "slider", icon: QI.sun, label: "Brillo" },
  { k: "volume", type: "slider", icon: QI.vol, label: "Volumen" },
  ...(QM.mix || []).map(m => ({ k: "mix:" + m.exe, type: "mix", m })),
  // Interruptores en cuadritos, lado a lado (como el centro de control): ocupan la mitad y se tocan fácil.
  { k: "wifi", type: "tile", grp: "radio", icon: QI.wifi, label: "Wi-Fi" },
  { k: "airplane", type: "tile", grp: "radio", icon: QI.plane, label: "Modo avión" },
  ...(VPN.provider !== "none" ? [{ k: "vpn", type: "tile", grp: "radio", icon: QI.vpn, label: "VPN" }] : [])
  // beta 1.3: sin «Apagar…» ni «Ajustes» (están en el Inicio, en los íconos de abajo)
];
/* Filas agrupadas: las que van lado a lado (cuadritos, Apagar/Ajustes) forman un grupo para moverse con la cruceta. */
function qmGroups(rows) {
  const g = [];
  rows.forEach((r, i) => { const last = g[g.length - 1]; if (r.grp && last && last.grp === r.grp) last.items.push(i); else g.push({ grp: r.grp, items: [i] }); });
  return g;
}
/* Íconos que cambian con el valor: volumen con más ondas mientras más alto (tachado en 0); el sol crece con el brillo. */
function volIcon(v) {
  const body = '<path d="M4 9.5h3.5L12 5.5v13l-4.5-4H4z"/>';
  const waves = v <= 0 ? '<path d="M16 9.5l5 5M21 9.5l-5 5"/>' : (v >= 34 ? '<path d="M15.5 9a4 4 0 0 1 0 6"/>' : "") + (v >= 67 ? '<path d="M18 6.5a7.5 7.5 0 0 1 0 11"/>' : "");
  return `<svg class="qi${v <= 0 ? " mute" : ""}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${body}${waves}</svg>`;
}
function sunIcon(v) {
  const r = 2.6 + v / 100 * 2, a = r + 2.2, b = a + 1 + v / 100 * 1.4;
  const rays = [0, 45, 90, 135, 180, 225, 270, 315].map(d => { const t = d * Math.PI / 180, c = Math.cos(t), s2 = Math.sin(t); return `M${(12 + c * a).toFixed(2)} ${(12 + s2 * a).toFixed(2)}L${(12 + c * b).toFixed(2)} ${(12 + s2 * b).toFixed(2)}`; }).join("");
  return `<svg class="qi" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="${r.toFixed(2)}"/><path d="${rays}"/></svg>`;
}
/* Batería: con datos reales de Windows (si los da). Cargando: cuánto falta para llenarse. */
let battInfo = null;
const hmin = m => `${Math.floor(m / 60)} h ${String(Math.round(m % 60)).padStart(2, "0")} min`;
function battEstimate() {
  const b = battInfo, charging = deviceCharging || (b && (b.charging || b.plugged));
  if (charging) {
    if (deviceBatt >= .995) return "Carga completa";
    if (b && b.minutesToFull > 0 && b.minutesToFull < 1440) return `≈ ${hmin(b.minutesToFull)} para cargar`;
    return "Cargando";
  }
  if (b && b.minutesLeft > 0 && b.minutesLeft < 1440) return `≈ ${hmin(b.minutesLeft)} restantes`;
  return `≈ ${hmin(deviceBatt * 4.2 * 60)} restantes`;
}
/* Íconos chiquitos del mezclador: los mismos de la interfaz principal (portada del juego / ícono de la app). */
const mixIcons = {};
function mixIcon(m) {
  const g = { appid: m.appid || seeded(m.name, 99999), name: m.name, source: m.source, kind: m.kind, art: m.art };
  if (mixIcons[g.appid] === undefined) { mixIcons[g.appid] = null; Art.get(g).then(u => { mixIcons[g.appid] = u || ""; if (QM.open) renderQuick(); }).catch(() => {}); }
  const u = mixIcons[g.appid];
  return `<img alt="" src="${esc(u || Art.placeholder(g))}"${u && Art.icons.has(u) ? ' class="appicon"' : ""}>`;
}
function renderQuickClassic() {
  const rows = qmRows(), q = $("#quick");
  q.classList.remove("ns");
  const row = (r, i) => {
    const f = QM.i === i ? " focused" : "";
    if (r.type === "slider") { const off = r.k === "brightness" && QM.noBri, v = sys[r.k], ic = r.k === "volume" ? volIcon(v) : sunIcon(off ? 50 : v); return `<div class="qrow${f}${off ? " qoff" : ""}">${ic}<span class="qlab">${r.label}</span><span class="qbar"><i style="width:${off ? 0 : v}%"></i></span><span class="qval">${off ? "—" : v}</span></div>`; }
    if (r.type === "tile" && r.k === "vpn") { const on = vpnOn() && !VPN.busy; return `<div class="qrow qtile${on ? " on" : ""}${VPN.busy ? " busy" : ""}${f}">${r.icon}<span class="qtl"><b>${r.label}</b><span>${vpnLabel()}</span></span></div>`; }
    if (r.type === "tile") { const on = !!sys[r.k], st = r.k === "wifi" ? (on ? (netOn() ? "Conectado" : navigator.onLine ? "Sin internet" : "Encendido") : "Apagado") : (on ? "Activado" : "Desactivado"); return `<div class="qrow qtile${on ? " on" : ""}${f}">${r.icon}<span class="qtl"><b>${r.label}</b><span>${st}</span></span></div>`; }
    if (r.type === "seg") return `<div class="qrow wrap${f}">${r.icon}<span class="qlab">${r.label}</span><span class="qval">${POWER.find(p => p[0] === sys.power)[2]}</span><span class="qseg">${POWER.map(([k, n, w]) => `<span class="${sys.power === k ? "on" : ""}">${n}</span>`).join("")}</span></div>`;
    if (r.type === "switch") return `<div class="qrow${f}">${r.icon}<span class="qlab">${r.label}</span><span class="qsw${sys[r.k] ? " on" : ""}"></span></div>`;
    if (r.type === "media") return mediaCard(f);
    if (r.type === "mix") { const m = r.m; return `<div class="qrow qmix qsub${f}${m.volume <= 0 ? " muted" : ""}"><span class="mico">${mixIcon(m)}</span><span class="qlab">${esc(m.name)}</span><span class="qbar"><i style="width:${m.volume}%"></i></span><span class="qval">${m.volume}</span></div>`; }
    if (r.type === "output") { const o = (QM.outs || []).find(x => x.current) || QM.outs[0]; return `<div class="qrow${f}">${r.icon}<span class="qlab">${r.label}</span><span class="qout">${esc(o ? o.name : "")}</span><span class="qval qarr">‹ ›</span></div>`; }
    return "";
  };
  const showDolby = dolbyOn();
  q.classList.remove("dense");
  const rowAt = (r, i) => row(r, i).replace('<div class="qrow', `<div data-qi="${i}" class="qrow`);
  q.innerHTML = `<div class="qhead"><span class="qclock clock">${clockText()}</span><span class="qb"><span class="batt">${Math.round(deviceBatt * 100)} % ${battSvg(deviceBatt)}</span><span>${battEstimate()}</span></span></div>
    ${showDolby ? `<div class="dolby qdolby${dolbyGold() ? " gold" : ""}">${dolbyHTML()}</div>` : ""}
    <div class="qrows">${qmGroups(rows).map(g => g.grp === "radio" ? `<div class="qpair${g.items.length === 3 ? " three" : ""}">${g.items.map(i => rowAt(rows[i], i)).join("")}</div>`
      : g.grp === "act" ? `<div class="qbtns">${g.items.map(i => `<div data-qi="${i}" class="qrow${QM.i === i ? " focused" : ""}">${rows[i].icon || ""}<span>${rows[i].label}</span></div>`).join("")}</div>`
      : g.items.map(i => rowAt(rows[i], i)).join("")).join("")}</div>
    <div class="qhint"><span class="hint" data-key="b"><span class="glyph">B</span>Cerrar</span><span class="hint"><span class="glyph">◀▶</span>Ajustar</span><span class="hint" data-key="a"><span class="glyph">A</span>Aceptar</span></div>`;
  const qr = $(".qrows", q);
  q.classList.remove("dense2");
  if (qr && qr.scrollHeight > qr.clientHeight + 2) q.classList.add("dense");   // no cabe: versión compacta
  if (qr && qr.scrollHeight > qr.clientHeight + 2) q.classList.add("dense2");  // todavía no: más compacta
  const fr = $(".qrows .focused", q); if (fr) fr.scrollIntoView({ block: "nearest" });
}
/* Pantalla táctil en el menú rápido: tocar una fila la elige y la acciona; en las barras, tocar o arrastrar fija el valor. */
let qmDrag = null;
function qmBarValue(bar, x) { const b = bar.getBoundingClientRect(); return clamp(Math.round((x - b.left) / b.width * 20) * 5, 0, 100); }
function qmSetBar(r, v) {
  if (r.type === "slider" && r.k === "brightness" && (QM.noBri || sys.autoBri)) return;
  if (r.type === "slider") { if (sys[r.k] === v) return; sys[r.k] = v; applySys(r.k); }
  else if (r.type === "mix") { if (r.m.volume === v) return; r.m.volume = v; QM.mixTouched = Date.now(); Host.call("mixer.set", { exe: r.m.exe, value: v }).catch(() => {}); }
  renderQuick();
}
function qmTouchSetup() {
  const q = $("#quick"); if (q.dataset.touch) return; q.dataset.touch = "1";
  q.addEventListener("pointerdown", e => {
    if (!QM.open) return;
    Snd.ensure(); lastActivity = performance.now();
    const el = e.target.closest("[data-qi]"); if (!el) return;
    const i = +el.dataset.qi, r = qmRows()[i]; if (!r) return;
    const seek = e.target.closest(".qmprog .qbar");
    if (seek && r.type === "media" && media && media.duration) { const b = seek.getBoundingClientRect(); QM.i = i; return mediaSeek((e.clientX - b.left) / b.width * media.duration); }
    const bar = e.target.closest(".qbar");
    if (bar && (r.type === "slider" || r.type === "mix")) { QM.i = i; qmDrag = { r, bar, id: e.pointerId }; qmSetBar(r, qmBarValue(bar, e.clientX)); play(r.k === "volume" || r.type === "mix" ? "volume" : "tick"); }
  });
  addEventListener("pointermove", e => { if (qmDrag && e.pointerId === qmDrag.id) { const bar = $("#quick .focused .qbar") || qmDrag.bar; qmSetBar(qmDrag.r, qmBarValue(bar, e.clientX)); } });
  addEventListener("pointerup", e => { if (qmDrag && e.pointerId === qmDrag.id) { qmDrag = null; return; } });
  q.addEventListener("click", e => {
    if (!QM.open) return;
    if (e.target.closest(".qhint .hint")) { const t = e.target.closest(".qhint .hint").textContent; return qmInput(/Cerrar|Atrás/.test(t) ? "b" : /Aceptar/.test(t) ? "a" : "right"); }
    const el = e.target.closest("[data-qi]"); if (!el) return;
    const i = +el.dataset.qi, r = qmRows()[i]; if (!r) return;
    if (e.target.closest(".nqbatt")) return;
    if (e.target.closest(".qbar") && (r.type === "slider" || r.type === "mix" || r.type === "media" && e.target.closest(".qmprog"))) return;   // ya lo hizo pointerdown
    QM.i = i;
    if (r.type === "media") {
      const mb = e.target.closest(".qmb");
      if (mb) { QM.m = mb.dataset.m != null ? +mb.dataset.m : [...el.querySelectorAll(".qmb")].indexOf(mb); return qmInput("a"); }
      play("movelist"); return renderQuick();
    }
    if (r.type === "slider" || r.type === "mix") { play("movelist"); return renderQuick(); }
    if (r.type === "output") return qmInput("right");
    return qmInput("a");
  });
}
function openQuick() {
  qmTouchSetup();
  if (QM.open || !["ui", "game"].includes(mode)) return;
  QM.open = true; QM.look = null; QM.i = 0; QM.m = 2; QM.src = null; renderQuick(); tintQuick(); mediaTick();
  loadVpn().then(() => { if (QM.open) qmPatch(() => {}); });
  // Cada vez que se abre se vuelve a leer todo (juego, barras, Salida, Dolby, canción, batería). Mientras llega,
  // se ve lo último que se sabía; lo que no conteste a tiempo se queda con su último valor.
  if (Host.has) {
    liveQuick(true);
    clearInterval(QM.live); QM.live = setInterval(() => { if (!QM.open) return clearInterval(QM.live); liveQuick(false); }, 2000);
  }
  $("#quick").classList.add("show"); $("#qshade").classList.add("show"); play("qopen");
  // Ya está armado: se avisa enseguida (esperar al siguiente cuadro hacía que, tapado por el juego, se recargara sin necesidad).
  if (OVERLAY && Host.has) setTimeout(() => Host.call("quick.shown").catch(() => {}), 0);
}
/* Cambia filas del menú sin que el foco brinque (si aparecen o desaparecen filas arriba del foco). */
/* Lo que se ve en el menú (para no rehacerlo si una lectura trajo lo mismo: beta 1.2). */
const qmLook = () => { try { return JSON.stringify([sys, QM.mix, QM.outs, typeof ATMOS !== "undefined" ? ATMOS : null, QM.autoBriOk, QM.ssid, QM.noBri, battInfo]); } catch (e) { return Math.random() + ""; } };
function qmPatch(fn) {
  const key = (qmRows()[QM.i] || {}).k, before = qmRows().map(r => r.k).join();
  fn();
  if (!QM.open) return;
  const look = qmLook();
  if (look === QM.look && qmRows().map(r => r.k).join() === before) return;   // nada cambió: no se repinta
  QM.look = look;
  if (qmRows().map(r => r.k).join() === before) return renderQuick();
  const j = qmRows().findIndex(r => r.k === key); if (j >= 0) QM.i = j; else QM.i = clamp(QM.i, 0, qmRows().length - 1);
  renderQuick();
}
/* Barras de volumen y Dolby en vivo: cada 2 s mientras el menú está abierto. */
function liveQuick(full) {
  // Una lectura a la vez: si Windows tarda, no se amontonan pedidos.
  if (QM.liveBusy && !full && Date.now() - QM.liveBusy < 6000) return;
  QM.liveBusy = Date.now();
  Host.call("quick.live", { full: !!full }).finally(() => { QM.liveBusy = 0; }).then(r => {
    if (!r || !QM.open) return;
    if (r.system && typeof r.system.brightnessOk === "boolean") QM.noBri = !r.system.brightnessOk;
    if (r.system && QM.radioBusy) { delete r.system.wifi; delete r.system.airplane; }   // está cambiando: no pisar
    const touching = Date.now() - (QM.mixTouched || 0) < 4000;
    // Lo que acabas de mover no se pisa con la lectura de Windows (que puede llegar con el valor de antes).
    if (r.system) { for (const k in r.system) if (r.system[k] != null && k in sys && !(Date.now() - ((QM.sysTouched || {})[k] || 0) < 4000)) sys[k] = r.system[k]; saveSys(); }
    if ("battery" in r) battInfo = r.battery;
    if ("media" in r) setMedia(r.media);
    qmPatch(() => {
      if (Array.isArray(r.mixer)) {
        if (touching && QM.mix) r.mixer.forEach(m => { const o = QM.mix.find(x => x.exe === m.exe); if (o) m.volume = o.volume; });   // no pelear con lo que estás moviendo
        QM.mix = r.mixer;
      }
      if (Array.isArray(r.outputs)) QM.outs = r.outputs;
      if (typeof r.atmos === "boolean") ATMOS = r.atmos;
      if (r.system && typeof r.system.autoBriOk === "boolean") QM.autoBriOk = r.system.autoBriOk;
      if ("wifiName" in r) QM.ssid = r.wifiName || "";
    });
  }).catch(() => {});
}
function closeQuick(stay, why = "B") {
  if (!QM.open) return;
  QM.open = false; $("#quick").classList.remove("show"); $("#qshade").classList.remove("show"); play("qclose");
  clearInterval(QM.live); clearInterval(mediaTimer);   // nada del menú se queda corriendo de fondo
  if (OVERLAY) { if (!stay) setTimeout(() => Host.call("quick.close", { why }).catch(() => {}), 240); return; }
  if (!stay && Host.has && mode === "game") Host.call("game.focus").catch(() => {});
}
function applySys(k) {
  saveSys();
  QM.sysTouched = Object.assign(QM.sysTouched || {}, { [k]: Date.now() });
  if (k === "brightness") Host.call("system.brightness", { value: sys.brightness }).catch(() => {});
  if (k === "volume") Host.call("system.volume", { value: sys.volume }).catch(() => {});
  if (k === "power") Host.call("system.power", { profile: sys.power }).catch(() => {});
  $$(".wifi").forEach(w => w.style.opacity = sys.wifi && !sys.airplane && netOn() ? 1 : .3);
}
/* Wi-Fi y modo avión como en Windows: el modo avión apaga el Wi-Fi (y quitarlo lo prende), pero con el modo avión puesto
   se puede volver a prender solo el Wi-Fi. El interruptor se queda como Windows diga que quedó; si no deja, se avisa. */
function setRadio(k, on) {
  const prev = { wifi: sys.wifi, airplane: sys.airplane };
  sys[k] = on;
  if (k === "airplane") sys.wifi = !on;
  play(on ? "on" : "off"); saveSys(); applySys("radio");
  if (QM.open) renderQuick();
  if (!Host.has) return;
  QM.radioBusy = (QM.radioBusy || 0) + 1;
  Host.call("system.radio", { wifi: sys.wifi, airplane: sys.airplane }).then(r => {
    if (r && r.error) { Object.assign(sys, prev); toast(r.error); }
    else if (r) { if (typeof r.wifi === "boolean") sys.wifi = r.wifi; if (typeof r.airplane === "boolean") sys.airplane = r.airplane; }
  }).catch(() => { Object.assign(sys, prev); toast("No se pudo cambiar"); })
    .finally(() => { QM.radioBusy--; saveSys(); applySys("radio"); if (QM.open) renderQuick(); });
}
function qmInputClassic(k) {
  const rows = qmRows(), r = rows[QM.i], n = rows.length;
  if (k === "b") return closeQuick();
  const G = qmGroups(rows), gi = G.findIndex(g => g.items.includes(QM.i)), col = gi >= 0 ? G[gi].items.indexOf(QM.i) : 0;
  if (k === "up" || k === "down") {
    const t = G[gi + (k === "down" ? 1 : -1)];
    if (!t) { play("bump"); return; }
    QM.i = t.items[Math.min(col, t.items.length - 1)]; play("move"); return renderQuick();
  }
  // En los grupos lado a lado (cuadritos, Apagar/Ajustes), izquierda y derecha cambian de cuadro.
  if ((k === "left" || k === "right") && gi >= 0 && G[gi].items.length > 1) {
    const j = col + (k === "right" ? 1 : -1);
    if (j < 0 || j >= G[gi].items.length) { play("bump"); return; }
    QM.i = G[gi].items[j]; play("move"); return renderQuick();
  }
  if (r.type === "media" && (k === "lb" || k === "rb")) return mediaSeek((media ? mediaPos() : 0) + (k === "rb" ? 10 : -10));
  if (r.type === "media" && (k === "left" || k === "right")) {
    const opts = mediaButtons().map((b, j) => b.ok ? j : -1).filter(j => j >= 0);
    const at = opts.indexOf(QM.m), n2 = opts[at + (k === "right" ? 1 : -1)];
    if (n2 == null) return play("bump");
    QM.m = n2; play("move"); return renderQuick();
  }
  if (r.type === "media" && k === "a") { const b = mediaButtons()[QM.m]; if (!b || !b.ok) return play("bump"); play("select"); return b.run(); }
  if (r.type === "media" && k === "y") { const b = mediaButtons()[0]; if (b.ok) { play("select"); b.run(); } return; }
  if (r.type === "mix" && (k === "left" || k === "right")) {
    const v = clamp(r.m.volume + (k === "right" ? 5 : -5), 0, 100); if (v === r.m.volume) return play("bump");
    r.m.volume = v; QM.mixTouched = Date.now(); play("volume", v / 50); Host.call("mixer.set", { exe: r.m.exe, value: v }).catch(() => {});
    return renderQuick();
  }
  if (r.type === "output" && (k === "left" || k === "right" || k === "a")) {
    const outs = QM.outs, cur = Math.max(0, outs.findIndex(o => o.current)), nx = (cur + (k === "left" ? -1 : 1) + outs.length) % outs.length;
    outs.forEach((o, j) => o.current = j === nx); play("select"); renderQuick();
    Host.call("audio.output", { id: outs[nx].id }).then(() => setTimeout(() => Host.call("system.state").then(st => { if (st && st.volume != null) { sys.volume = st.volume; if (QM.open) renderQuick(); } }).catch(() => {}), 600)).catch(() => {});
    toast("Salida: " + outs[nx].name);
    return;
  }
  if (k === "left" || k === "right") {
    const d = k === "right" ? 1 : -1;
    if (r.type === "slider" && r.k === "brightness" && QM.noBri) { play("bump"); return toast("Esta pantalla no deja cambiar el brillo desde Windows"); }
    if (r.type === "slider") { const v = clamp(sys[r.k] + d * 5, 0, 100); if (v === sys[r.k]) return play("bump"); sys[r.k] = v; play(r.k === "volume" ? "volume" : d > 0 ? "up" : "down", v / 50); applySys(r.k); }
    else if (r.type === "seg") { const j = POWER.findIndex(p => p[0] === sys.power) + d; if (j < 0 || j >= POWER.length) return play("bump"); sys.power = POWER[j][0]; play("move"); applySys("power"); }
    else if (r.type === "btn") { const j = r.k === "sleep" ? QM.i + 1 : QM.i - 1; QM.i = j; play("move"); }
    else if (r.type === "switch") { if (sys[r.k] === (d > 0)) return play("bump"); return setRadio(r.k, d > 0); }
    return renderQuick();
  }
  if (k === "a") {
    if (r.k === "vpn") return vpnSet(!vpnOn());
    if (r.type === "switch" || r.type === "tile") return setRadio(r.k, !sys[r.k]);
    if (r.type === "seg") { const j = (POWER.findIndex(p => p[0] === sys.power) + 1) % POWER.length; sys.power = POWER[j][0]; play("select"); applySys("power"); return renderQuick(); }
    if (OVERLAY && (r.k === "sleep" || r.k === "settings")) {   // lo hace la pantalla principal
      closeQuick(true); return setTimeout(() => Host.call("quick.action", { action: r.k }).catch(() => {}), 240);
    }
    if (r.k === "sleep") { closeQuick(true); return setTimeout(powerMenu, 250); }
    if (r.k === "settings") {
      closeQuick(true);
      if (mode === "game") goHome();
      return setTimeout(() => { if (mode === "ui" && !(topView() && topView().isSettings)) openView(settingsView()); }, 380);
    }
    return play("bump");
  }
  play("bump");
}
