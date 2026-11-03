/* ================= controles: mandos, Bluetooth, mapeo y prueba ================= */
const keysDown = new Set();
addEventListener("keydown", e => keysDown.add(e.key.length === 1 ? e.key.toLowerCase() : e.key));
addEventListener("keyup", e => keysDown.delete(e.key.length === 1 ? e.key.toLowerCase() : e.key));
addEventListener("blur", () => keysDown.clear());
const sortedPads = () => [...state.pads].sort((a, b) => a.player - b.player);
function renumberPads() { sortedPads().forEach((p, i) => p.player = i + 1); }
function updatePadName() {
  if (activePad !== "ally" && !state.pads.some(p => p.key === activePad) && !/^gp/.test(activePad)) { activePad = "ally"; activeLayout = ""; }   // se desconectó el que usabas
  paintPadFoot();
}
function refreshControls() { const v = views.find(x => x.isControls); if (v) v.refresh(); }
function identifyPad(p) {
  if (Host.has) Host.call("pad.rumble", { key: p.key }).catch(() => {});
  else {
    const gp = navigator.getGamepads && navigator.getGamepads()[p.gp ?? 0];
    if (gp && gp.vibrationActuator) gp.vibrationActuator.playEffect("dual-rumble", { duration: 600, strongMagnitude: .7, weakMagnitude: .7 }).catch(() => {});
  }
  toast(`Vibrando: ${p.name} (J${p.player})`);
}
function padOptions(p) {
  const opts = [{ label: "Probar botones", value: "test" }, { label: "Identificar (vibrar)", value: "id" }, { label: `Número de jugador · ahora J${p.player}`, value: "player" }];
  if (p.type !== "Integrado") opts.push({ label: "Desconectar", value: "disc" });
  openView(pickerView(p.name, I.pad, opts, null, v => {
    if (v === "test") openView(testerView(p));
    else if (v === "id") identifyPad(p);
    else if (v === "player") {
      const n = state.pads.length, o = Array.from({ length: n }, (_, i) => ({ label: `Jugador ${i + 1}`, value: i + 1 }));
      openView(pickerView("Número de jugador", I.pad, o, p.player, num => {
        const other = state.pads.find(x => x.player === num); if (other) other.player = p.player; p.player = num;
        Host.call("pad.player", { key: p.key, player: num }).catch(() => {});
        refreshControls(); toast(`${p.name} ahora es J${num}`);
      }));
    } else if (v === "disc") {
      confirmDialog(`¿Desconectar ${p.name}?`, "Desconectar").then(ok => {
        if (!ok) return;
        Host.call("pad.disconnect", { key: p.key }).catch(() => {});
        state.pads = state.pads.filter(x => x !== p); renumberPads(); updatePadName(); refreshControls();
        notify({ icon: I.pad, title: "Mando desconectado", text: p.name });
      });
    }
  }));
}
function btView() {
  return {
    found: [], timers: [], hints: [["B", "Atrás"], ["A", "Emparejar"]],
    render(el) {
      el.innerHTML = head(I.bt, "Conectar un mando") + `<div class="vbody"><div class="bthelp">${I.bt}<div>Pon tu mando en modo de emparejamiento. En un mando Xbox, mantén el botón de sincronización (arriba, junto al puerto USB) hasta que el logo parpadee rápido.</div></div><div class="sect">Mandos encontrados</div><div class="btlist"></div></div>`;
      this.scan();
    },
    scan() {
      $(".vhead .sub", this.el).innerHTML = `Buscando<span class="dots3"><i></i><i></i><i></i></span>`;
      this.paint();
      if (Host.has) {
        Host.call("bt.scan").catch(() => {});
        this.off = Events.on("bt.found", d => { if (!this.found.find(f => f.id === d.id)) { this.found.push({ ...d }); this.paint(); } });
        return;
      }
      [{ id: "bt1", name: "Xbox Wireless Controller" }, { id: "bt2", name: "DualSense Wireless Controller" }, { id: "bt3", name: "8BitDo Ultimate 2" }]
        .forEach((d, i) => this.timers.push(setTimeout(() => { if (this.el.isConnected) { this.found.push(d); this.paint(); } }, 1200 + i * 1300)));
    },
    paint() {
      const st = d => d.state === "pairing" ? `Emparejando<span class="dots3"><i></i><i></i><i></i></span>` : d.state === "done" ? "Conectado ✓" : "Disponible";
      $(".btlist", this.el).innerHTML = (this.found.length ? "" : `<p class="note" style="margin:0 0 1.6rem">Buscando mandos cerca…</p>`) +
        this.found.map((d, i) => `<button class="lrow fi" data-i="${i}"><span class="ico">${I.pad}</span><span class="meta"><b>${esc(d.name)}</b><span>Bluetooth</span></span><span class="val">${st(d)}</span></button>`).join("") +
        `<button class="lrow fi" data-win="1"><span class="ico">${I.gear}</span><span class="meta"><b>Abrir Bluetooth de Windows</b><span>Para audífonos, teclados y otros dispositivos</span></span><span class="val">›</span></button>`;
      $$("[data-i]", this.el).forEach(b => b.onclick = () => this.pair(this.found[+b.dataset.i]));
      $("[data-win]", this.el).onclick = () => openSystemApp(SYS.bluetooth);
      updateFocus();
    },
    pair(d) {
      if (d.state) return bump(this.items()[this.i]);
      d.state = "pairing"; this.paint();
      const done = () => {
        d.state = "done"; if (this.el.isConnected) this.paint();
        const p = { key: d.id, name: d.name, type: "Bluetooth", player: state.pads.length + 1, battery: .7 };
        state.pads.push(p); updatePadName();
        notify({ icon: I.pad, title: "Mando conectado", text: `${d.name} · Jugador ${p.player}` });
      };
      if (Host.has) Host.call("bt.pair", { id: d.id }).then(done).catch(() => { d.state = null; this.paint(); notify({ icon: I.bt, title: "No se pudo emparejar", text: d.name }); });
      else setTimeout(done, 1800);
    },
    onBack() { this.timers.forEach(clearTimeout); if (this.off) this.off(); refreshControls(); },
    items() { return fis(this.el); }
  };
}
const MAPPING = [["A · B · X · Y", "A · B · X · Y"], ["Cruceta", "Cruceta"], ["Sticks (L3 · R3)", "Sticks (L3 · R3)"], ["LB · RB", "LB · RB"], ["LT · RT", "LT · RT analógicos"], ["Vista · Menú", "Vista · Menú"], ["Botón Xbox", "1 toque: menú rápido · 2: Inicio · mantener: ventanas"], ["Command Center", "Armoury Crate"], ["Traseros izq. / der.", "Sin asignar"]];
function mapView() {
  return {
    cols: 2, hints: [["B", "Atrás"], ["A", "Aceptar"]],
    render(el) {
      el.innerHTML = head(I.tune, "Mapeo de la consola", "Modo juego") + `<div class="vbody"><div class="map"><div><div class="allyart"></div>
        <div class="btnrow"><button class="btn primary fi" data-a="ac">Cambiar en Armoury Crate</button><button class="btn fi" data-a="test">Probar botones</button></div>
        <p class="note">Scarlet muestra el esquema, pero quien lo cambia es Armoury Crate: ASUS no ofrece una forma pública de modificarlo desde otra app. Para cambiar botones solo en un juego, usa “Controles (Steam Input)” en sus opciones (X).</p></div>
        <dl class="mtable"><dt style="font-weight:800;color:var(--fg)">Botón</dt><dd style="color:var(--fg2)">Hace</dd>${MAPPING.map(([k, v]) => `<dt>${esc(k)}</dt><dd>${esc(v)}</dd>`).join("")}</dl></div></div>`;
      $('[data-a="ac"]', el).onclick = () => openSystemApp(SYS.armoury);
      $('[data-a="test"]', el).onclick = () => openView(testerView(state.pads.find(p => p.type === "Integrado") || sortedPads()[0]));
    },
    items() { return fis(this.el); }
  };
}
/* --- dibujos de mandos (propios): Ally (tu esquema), estilo Xbox, estilo DualSense --- */
const padKind = p => p.type === "Integrado" ? "ally" : p.layout === "xbox" || p.layout === "ps" ? p.layout : (p.vendor === 0x054C || /dualsense|dualshock|playstation|ps5|ps4|sony|054c/i.test(p.name)) ? "ps" : (p.vendor === 0x045E || /xbox|xinput|microsoft|045e/i.test(p.name)) ? "xbox" : "generic";
let artSeq = 0;
function padArt(kind, big) {
  const H = (k, inner) => `<g data-b="${k}">${inner}</g>`;
  const L = (x, y, t, cls = "lbl") => big ? `<text class="${cls}" x="${x}" y="${y}">${t}</text>` : "";
  if (kind === "ally") {
    const id = "am" + (++artSeq);
    return `<svg class="art" viewBox="0 0 840 364"><defs><mask id="${id}" maskUnits="userSpaceOnUse" x="0" y="0" width="840" height="364"><image href="${PADSRC}" width="840" height="364"/></mask></defs>
      <rect width="840" height="364" fill="currentColor" mask="url(#${id})"/>
      ${H("lb", '<path class="sh" d="M38 44Q72 18 150 9"/>')}${H("rb", '<path class="sh" d="M802 44Q768 18 690 9"/>')}
      ${H("ls", '<circle class="hotfill" cx="75" cy="106" r="21"/>')}${H("rs", '<circle class="hotfill" cx="707" cy="157" r="21"/>')}
      ${H("up", '<rect class="hotfill" x="124" y="131" width="20" height="21" rx="3"/>')}${H("down", '<rect class="hotfill" x="124" y="172" width="20" height="21" rx="3"/>')}
      ${H("left", '<rect class="hotfill" x="103" y="152" width="21" height="20" rx="3"/>')}${H("right", '<rect class="hotfill" x="144" y="152" width="21" height="20" rx="3"/>')}
      ${H("y", '<circle class="hotfill" cx="757" cy="62" r="13"/>' + L(757, 62, "Y", "lbl ovl"))}${H("x", '<circle class="hotfill" cx="729" cy="90" r="13"/>' + L(729, 90, "X", "lbl ovl"))}
      ${H("b", '<circle class="hotfill" cx="786" cy="90" r="13"/>' + L(786, 90, "B", "lbl ovl"))}${H("a", '<circle class="hotfill" cx="757" cy="117" r="13"/>' + L(757, 117, "A", "lbl ovl"))}
      ${H("guide", '<rect class="hotfill" x="158" y="57" width="14" height="20" rx="4"/>')}${H("view", '<rect class="hotfill" x="158" y="85" width="14" height="20" rx="4"/>')}
      ${H("menu", '<rect class="hotfill" x="668" y="57" width="14" height="20" rx="4"/>')}
      ${big ? `<circle class="dotm lsd" cx="75" cy="106" r="8"/><circle class="dotm rsd" cx="707" cy="157" r="8"/>
        <text class="scrs" x="421" y="150">Último botón</text><text class="scrb tlast" x="421" y="198">—</text>` : ""}
    </svg>`;
  }
  const maskArt = (src, w, h, body) => { const id = "am" + (++artSeq); return `<svg class="art" viewBox="0 0 ${w} ${h}"><defs><mask id="${id}" maskUnits="userSpaceOnUse" x="0" y="0" width="${w}" height="${h}"><image href="${src}" width="${w}" height="${h}"/></mask></defs><rect width="${w}" height="${h}" fill="currentColor" mask="url(#${id})"/>${body}</svg>`; };
  if (kind === "ps") return maskArt(PSPADSRC, 600, 401, `
      ${H("lb", '<path class="sh" d="M84 25Q104 10 150 8"/>')}${H("rb", '<path class="sh" d="M516 25Q496 10 450 8"/>')}
      ${H("tp", '<path class="hotfill" d="M190 20H410L400 120Q397 134 382 134H218Q203 134 200 120Z" opacity=".55"/>')}
      ${H("view", '<rect class="hotfill" x="156" y="38" width="13" height="25" rx="6"/>')}${H("menu", '<rect class="hotfill" x="432" y="38" width="13" height="25" rx="6"/>')}
      ${H("up", '<rect class="hotfill" x="101" y="70" width="28" height="33" rx="7"/>')}${H("down", '<rect class="hotfill" x="101" y="128" width="28" height="33" rx="7"/>')}
      ${H("left", '<rect class="hotfill" x="68" y="104" width="35" height="28" rx="7"/>')}${H("right", '<rect class="hotfill" x="126" y="104" width="35" height="28" rx="7"/>')}
      ${H("y", '<circle class="hotfill" cx="482" cy="72" r="18"/>' + L(482, 72, "△", "lbl ovl"))}${H("x", '<circle class="hotfill" cx="441" cy="115" r="18"/>' + L(441, 115, "□", "lbl ovl"))}
      ${H("b", '<circle class="hotfill" cx="527" cy="115" r="18"/>' + L(527, 115, "○", "lbl ovl"))}${H("a", '<circle class="hotfill" cx="482" cy="159" r="18"/>' + L(482, 159, "✕", "lbl ovl"))}
      ${H("ls", '<circle class="hotfill" cx="207" cy="195" r="25"/>')}${H("rs", '<circle class="hotfill" cx="395" cy="195" r="25"/>')}
      ${H("guide", '<rect class="hotfill" x="284" y="222" width="32" height="14" rx="7"/>')}
      ${big ? '<circle class="dotm lsd" cx="207" cy="195" r="10"/><circle class="dotm rsd" cx="395" cy="195" r="10"/>' : ""}`);
  if (kind === "xbox") return maskArt(XPADSRC, 600, 424, `
      ${H("lb", '<path class="sh" d="M92 50Q128 22 194 19"/>')}${H("rb", '<path class="sh" d="M508 50Q472 22 406 19"/>')}
      ${H("guide", '<circle class="hotfill" cx="300" cy="62" r="23"/>')}
      ${H("view", '<circle class="hotfill" cx="255" cy="122" r="12"/>')}${H("menu", '<circle class="hotfill" cx="344" cy="122" r="12"/>')}
      ${H("share", '<rect class="hotfill" x="285" y="149" width="30" height="15" rx="7"/>')}
      ${H("ls", '<circle class="hotfill" cx="144" cy="122" r="26"/>')}${H("rs", '<circle class="hotfill" cx="380" cy="215" r="26"/>')}
      ${H("up", '<rect class="hotfill" x="206" y="184" width="28" height="22" rx="3"/>')}${H("down", '<rect class="hotfill" x="206" y="236" width="28" height="23" rx="3"/>')}
      ${H("left", '<rect class="hotfill" x="181" y="207" width="25" height="28" rx="3"/>')}${H("right", '<rect class="hotfill" x="234" y="207" width="25" height="28" rx="3"/>')}
      ${H("y", '<circle class="hotfill" cx="457" cy="85" r="19"/>' + L(457, 85, "Y", "lbl ovl"))}${H("x", '<circle class="hotfill" cx="416" cy="125" r="19"/>' + L(416, 125, "X", "lbl ovl"))}
      ${H("b", '<circle class="hotfill" cx="499" cy="125" r="19"/>' + L(499, 125, "B", "lbl ovl"))}${H("a", '<circle class="hotfill" cx="457" cy="166" r="19"/>' + L(457, 166, "A", "lbl ovl"))}
      ${big ? '<circle class="dotm lsd" cx="144" cy="122" r="11"/><circle class="dotm rsd" cx="380" cy="215" r="11"/>' : ""}`);
  return `<svg class="art" viewBox="0 0 600 400" fill="none" stroke="currentColor" stroke-width="3" stroke-linejoin="round" stroke-linecap="round">
    <path d="M150 82H450Q522 82 548 150L586 292Q600 368 538 374Q498 377 472 330L442 284H158L128 330Q102 377 62 374Q0 368 14 292L52 150Q78 82 150 82Z"/>
    ${H("lb", '<rect class="hot" x="100" y="52" width="120" height="22" rx="11" transform="rotate(-6 160 63)"/>')}${H("rb", '<rect class="hot" x="380" y="52" width="120" height="22" rx="11" transform="rotate(6 440 63)"/>')}
    ${H("ls", '<circle class="hot" cx="175" cy="160" r="36"/>')}<circle cx="175" cy="160" r="22" opacity=".5"/>
    ${H("up", '<rect class="hot" x="229" y="208" width="22" height="28" rx="4"/>')}${H("down", '<rect class="hot" x="229" y="260" width="22" height="28" rx="4"/>')}
    ${H("left", '<rect class="hot" x="200" y="237" width="28" height="22" rx="4"/>')}${H("right", '<rect class="hot" x="252" y="237" width="28" height="22" rx="4"/>')}
    ${H("rs", '<circle class="hot" cx="380" cy="248" r="36"/>')}<circle cx="380" cy="248" r="22" opacity=".5"/>
    ${H("y", '<circle class="hot" cx="430" cy="126" r="16"/>' + L(430, 126, "Y"))}${H("x", '<circle class="hot" cx="397" cy="160" r="16"/>' + L(397, 160, "X"))}
    ${H("b", '<circle class="hot" cx="463" cy="160" r="16"/>' + L(463, 160, "B"))}${H("a", '<circle class="hot" cx="430" cy="194" r="16"/>' + L(430, 194, "A"))}
    ${H("view", '<circle class="hot" cx="262" cy="160" r="9"/>')}${H("menu", '<circle class="hot" cx="338" cy="160" r="9"/>')}
    ${big ? '<circle class="dotm lsd" cx="175" cy="160" r="12" stroke="none"/><circle class="dotm rsd" cx="380" cy="248" r="12" stroke="none"/>' : ""}
  </svg>`;
}
function battSvg(level, charging = deviceCharging) {
  const w = Math.max(1.5, 20.6 * level), red = level <= .15 && !charging;
  const bolt = charging ? `<path d="M15.6 1.6 9.8 9h3.6l-1.6 5.4L17.8 7h-3.7z" fill="#3ad16b" stroke="var(--bg,#000)" stroke-width="1.1" stroke-linejoin="round"/>` : "";
  return `<svg viewBox="0 0 30 16"><rect x=".8" y=".8" width="25" height="14.4" rx="3" fill="none" stroke="currentColor" stroke-width="1.6"/><rect x="27.2" y="5" width="2.2" height="6" rx="1" fill="currentColor"/><rect x="3" y="3" width="${w}" height="10" rx="1.5" fill="${red ? "#e5484d" : charging ? "#3ad16b" : "currentColor"}"/>${bolt}</svg>`;
}
let deviceCharging = false;
let deviceBatt = .82;
function padCard(p, n) {
  const kind = padKind(p), many = n > 2;
  const w = kind === "ally" ? (many ? 17 : n > 1 ? 24 : 32) : (many ? 11 : 14);
  const art = kind === "ally" ? `<div class="allymini" style="width:${w}rem"></div>` : `<div style="width:${w}rem">${padArt(kind)}</div>`;
  const lvl = kind === "ally" ? deviceBatt : p.battery;
  const label = kind === "ally" ? (n > 1 ? `Sistema · J${p.player}` : "Sistema") : `Jugador ${p.player}`;
  return `<div class="ccard" data-key="${esc(p.key)}"><span class="clabel">${label}</span><div class="cart">${art}</div><div class="cbatt">${lvl != null ? battSvg(lvl) : "<span>Con cable</span>"}</div>${kind === "ally" ? "" : `<span class="cname" style="max-width:${w + 1.5}rem">${esc(p.name)}</span>`}</div>`;
}
function choosePad(title, then) {
  const pads = sortedPads();
  if (pads.length === 1) return then(pads[0]);
  openView(pickerView(title, I.pad, pads.map(p => ({ label: `J${p.player} · ${p.name}`, value: p.key })), null, k => then(state.pads.find(p => p.key === k))));
}
const CTL_OPTS = [["order", "Cambiar orden"], ["search", "Buscar mandos"], ["manage", "Gestionar mandos"], ["test", "Probar botones"], ["map", "Mapeo de la consola"], ["help", "Cómo emparejar mandos"]];
function controlsView() {
  return {
    isControls: true, ordering: false, lifted: false,
    hints() { return this.ordering ? (this.lifted ? [["B", "Terminar"], ["◀▶", "Mover"], ["A", "Soltar"]] : [["B", "Terminar"], ["◀▶", "Elegir"], ["A", "Tomar"]]) : [["B", "Atrás"], ["A", "Aceptar"]]; },
    render(el) {
      el.innerHTML = head(I.pad, "Controles") + `<div class="ctl"><div class="ctl-left"><div class="cards"></div><p class="ctl-note"></p></div><div class="ctl-div"></div><div class="ctl-right">${CTL_OPTS.map(([k, t]) => `<button class="crow fi" data-opt="${k}">${t}</button>`).join("")}</div></div>`;
      $$("[data-opt]", el).forEach(b => b.onclick = () => this.opt(b.dataset.opt));
      this.refresh();
    },
    refresh() {
      const pads = sortedPads();
      $(".cards", this.el).innerHTML = pads.map(p => padCard(p, pads.length)).join("");
      $(".ctl-note", this.el).textContent = this.ordering ? (this.lifted ? "Mueve el mando a su nuevo lugar y presiona A." : "Elige con A el mando que quieres cambiar de lugar.") : pads.length === 1 ? "No hay otros mandos conectados." : "";
      this.el.classList.toggle("ordering", this.ordering);
      $$(".ccard", this.el).forEach((c, i) => {
        c.classList.toggle("lifted", this.ordering && this.lifted && i === this.i);
        c.onclick = () => { if (this.ordering) { this.i = i; this.toggleLift(); } };
      });
      updateFocus();
    },
    items() { return this.ordering ? $$(".ccard", this.el) : fis(this.el); },
    nav(dir) {
      if (!this.ordering) return false;
      const pads = sortedPads(), n = pads.length;
      if (dir === "up" || dir === "down") return bump(this.items()[this.i], dir);
      const j = this.i + (dir === "left" ? -1 : 1);
      if (j < 0 || j >= n) return bump(this.items()[this.i], dir);
      if (this.lifted) { const a = pads[this.i], b = pads[j], t = a.player; a.player = b.player; b.player = t; }
      this.i = j; play("move"); this.refresh();
    },
    toggleLift() {
      this.lifted = !this.lifted; play(this.lifted ? "select" : "back");
      if (!this.lifted) sortedPads().forEach(p => Host.call("pad.player", { key: p.key, player: p.player }).catch(() => {}));
      this.refresh();
    },
    onB() {
      if (!this.ordering) return false;
      if (this.lifted) this.toggleLift();
      this.ordering = false; this.i = this.saveI; play("back"); this.refresh(); toast("Orden de mandos guardado");
      return true;
    },
    opt(k) {
      if (k === "order") {
        if (state.pads.length < 2) { bump(this.items()[this.i]); return toast("Conecta otro mando para cambiar el orden"); }
        this.ordering = true; this.lifted = false; this.saveI = this.i; this.i = 0; play("select"); this.refresh();
      } else if (k === "search") openView(btView());
      else if (k === "manage") choosePad("Gestionar mandos", p => padOptions(p));
      else if (k === "test") choosePad("¿Qué mando quieres probar?", p => openView(testerView(p)));
      else if (k === "map") openView(mapView());
      else openView(helpView());
    }
  };
}
function helpView() {
  const card = (kind, title, steps) => `<div class="hcard"><div class="cart">${padArt(kind)}</div><h3>${title}</h3><ol>${steps.map(s => `<li>${s}</li>`).join("")}</ol></div>`;
  return {
    hints: [["B", "Atrás"]],
    render(el) {
      el.innerHTML = head(I.bt, "Cómo emparejar mandos") + `<div class="vbody"><div class="help">
        ${card("xbox", "Mando Xbox", ["Enciéndelo con el botón Xbox.", "Mantén el botón de sincronización (arriba, junto al puerto USB) unos 3 segundos.", "Cuando el logo parpadee rápido, entra a “Buscar mandos” y elígelo."])}
        ${card("ps", "DualSense (PS5)", ["Con el mando apagado, mantén Create y el botón PS al mismo tiempo.", "Suelta cuando la barra de luz parpadee dos veces seguidas.", "Entra a “Buscar mandos” y elígelo."])}
        ${card("generic", "Otros mandos", ["Ponlo en modo Bluetooth (en muchos mandos, modo X-input).", "Actívale el emparejamiento según su manual.", "Entra a “Buscar mandos”. Si es de cable, solo conéctalo."])}
      </div></div>`;
    },
    items() { return []; }
  };
}
function btnName(kind, k) {
  const n = { a: "A", b: "B", x: "X", y: "Y", lb: "LB", rb: "RB", lt: "LT", rt: "RT", view: "Vista", menu: "Menú", ls: "Stick izquierdo (L3)", rs: "Stick derecho (R3)", up: "Cruceta ↑", down: "Cruceta ↓", left: "Cruceta ←", right: "Cruceta →", guide: "Botón Xbox", tp: "Panel táctil", share: "Compartir" };
  if (kind === "ps") Object.assign(n, { a: "Cruz ✕", b: "Círculo ○", x: "Cuadrado □", y: "Triángulo △", lb: "L1", rb: "R1", lt: "L2", rt: "R2", view: "Create", menu: "Options", ls: "L3", rs: "R3", guide: "Botón PS" });
  return n[k] || k;
}
function testerView(p) {
  const kind = padKind(p), ps = kind === "ps", R = kind === "ally" ? 13 : 16;
  const TB = { a: 0, b: 1, x: 2, y: 3, lb: 4, rb: 5, view: 8, menu: 9, ls: 10, rs: 11, up: 12, down: 13, left: 14, right: 15, guide: 16, tp: 17, share: 17 };
  const KB = { a: ["Enter", " "], b: ["Escape", "Backspace"], x: ["x"], y: ["y"], lb: ["q"], rb: ["e"], menu: ["m"], view: ["v"], guide: ["h"], up: ["ArrowUp"], down: ["ArrowDown"], left: ["ArrowLeft"], right: ["ArrowRight"] };
  const f = v => (v >= 0 ? " " : "") + v.toFixed(2);
  return {
    hints: [["B", "Mantén para salir"]],
    render(el) {
      el.innerHTML = head(I.test, "Probar botones", `${p.name} · J${p.player}`) + `<div class="tester">
        <div class="tshoulders" style="width:${kind === "ally" ? "min(100rem,92%,calc((100vh - 36rem) * 2.31))" : "min(64rem,72%,calc((100vh - 36rem) * 1.5 + 8rem))"}"><div class="tsh"><div class="trig lt"><i></i><span>${ps ? "L2" : "LT"}</span></div><div class="bmp" data-k="lb">${ps ? "L1" : "LB"}</div></div><div class="tsh r"><div class="trig rt"><i></i><span>${ps ? "R2" : "RT"}</span></div><div class="bmp" data-k="rb">${ps ? "R1" : "RB"}</div></div></div>
        <div class="tart" style="width:${kind === "ally" ? "min(100rem,92%,calc((100vh - 36rem) * 2.31))" : "min(56rem,62%,calc((100vh - 36rem) * 1.5))"}">${padArt(kind, true)}</div>
        <div class="tinfo">${kind === "ally" ? "" : `<span>Último botón: <b class="tlast">—</b></span>`}<span class="tsticks"></span></div>
        <div class="holdbar"><i></i></div></div>`;
      this.g = {};
      $$("[data-b]", el).forEach(n => (this.g[n.dataset.b] = this.g[n.dataset.b] || []).push(n));
      $$(".bmp", el).forEach(n => (this.g[n.dataset.k] = this.g[n.dataset.k] || []).push(n));
      this.prev = {}; this.loop = this.loop.bind(this); requestAnimationFrame(this.loop);
    },
    setLast(k) { $$(".tlast", this.el).forEach(n => n.textContent = btnName(kind, k)); },
    loop(t) {
      if (!this.el.isConnected) return;
      if (topView() !== this) return requestAnimationFrame(this.loop);
      const all = navigator.getGamepads ? [...navigator.getGamepads()].filter(Boolean) : [];
      const gp = (p.gp != null && all.find(x => x.index === p.gp)) || (kind === "ally" ? all.find(x => x.index === 0) : null) || all[0];
      const pressed = i => !!(gp && gp.buttons[i] && gp.buttons[i].pressed);
      for (const k in TB) {
        const on = pressed(TB[k]) || (KB[k] || []).some(x => keysDown.has(x));
        (this.g[k] || []).forEach(n => n.classList.toggle("on", on));
        if (on && !this.prev[k]) this.setLast(k);
        this.prev[k] = on;
      }
      const tv = i => gp && gp.buttons[i] ? gp.buttons[i].value : 0;
      const lt = Math.max(tv(6), keysDown.has("z") ? 1 : 0), rt = Math.max(tv(7), keysDown.has("c") ? 1 : 0);
      $(".trig.lt i", this.el).style.width = lt * 100 + "%"; $(".trig.rt i", this.el).style.width = rt * 100 + "%";
      if (lt > .5 && !this.prev.lt) this.setLast("lt"); if (rt > .5 && !this.prev.rt) this.setLast("rt");
      this.prev.lt = lt > .5; this.prev.rt = rt > .5;
      const ax = gp ? gp.axes : [0, 0, 0, 0], a = i => ax[i] || 0;
      $$(".lsd", this.el).forEach(n => n.setAttribute("transform", `translate(${a(0) * R} ${a(1) * R})`));
      $$(".rsd", this.el).forEach(n => n.setAttribute("transform", `translate(${a(2) * R} ${a(3) * R})`));
      $(".tsticks", this.el).textContent = `Stick izq. (${f(a(0))}, ${f(a(1))})  ·  Stick der. (${f(a(2))}, ${f(a(3))})`;
      const held = pressed(1) || keysDown.has("Escape") || keysDown.has("Backspace");
      if (held) this.hold = this.hold || t; else this.hold = 0;
      const prog = this.hold ? Math.min(1, (t - this.hold) / 1000) : 0;
      $(".holdbar i", this.el).style.width = prog * 100 + "%";
      if (prog >= 1) { this.hold = 0; closeView(); return; }
      requestAnimationFrame(this.loop);
    },
    onKey() { return true; },
    items() { return []; }
  };
}

