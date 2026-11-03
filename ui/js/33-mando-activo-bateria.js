/* ================= beta 1.0: el mando que estás usando (abajo a la izquierda) =================
   El ícono cambia al último mando con el que se presionó algo (la Ally, uno de Xbox o un DualSense) y los cuadritos
   marcan su número de jugador. En la app lo avisa Scarlet ("pad.active"); en el navegador se mira qué mando se movió. */
document.documentElement.style.setProperty("--xpad", `url("${XPADSRC}")`);
document.documentElement.style.setProperty("--xbtn", `url("${document.getElementById("xbtnsrc").src}")`);
document.documentElement.style.setProperty("--pspad", `url("${PSPADSRC}")`);
function padIconCls() {
  const p = state.pads.find(x => x.key === activePad);
  const k = p ? padKind(p) : activeLayout || "ally";
  return k === "xbox" ? "xbox" : k === "ps" ? "ps" : "";
}
function paintPadFoot() {
  const cls = padIconCls(), p = state.pads.find(x => x.key === activePad), n = state.pads.length;
  const player = p && p.player ? p.player : 1;
  $$(".hints .padicon, #quick .padicon").forEach(e => { e.classList.toggle("xbox", cls === "xbox"); e.classList.toggle("ps", cls === "ps"); });
  $$("#padsq i, #quick .padsq i").forEach((i, k) => i.classList.toggle("on", k === player - 1));
  $("#padName").textContent = p ? p.name + (n > 1 ? ` · ${n} mandos` : "") : "ROG Xbox Ally X";
}
function setActivePad(key, layout) {
  if (!key || (key === activePad && (layout || "") === activeLayout)) return;
  activePad = key; activeLayout = layout || "";
  paintPadFoot();
}
Events.on("pad.active", d => d && setActivePad(d.key, d.layout));
/* Navegador: el mando cuyo estado cambió es el que se está usando (el 0 se toma como el de la consola). */
const gpSig = {};
function watchActiveGamepad(list) {
  if (Host.has) return;
  for (const g of list) {
    const s = g.buttons.map(b => b.pressed ? 1 : 0).join("") + g.axes.map(a => Math.round(a * 3)).join(",");
    if (gpSig[g.index] !== undefined && gpSig[g.index] !== s) {
      const ps = /054c|dualsense|dualshock|playstation|wireless controller/i.test(g.id);
      setActivePad(g.index === 0 ? "ally" : "gp" + g.index, g.index === 0 ? "" : ps ? "ps" : "xbox");
    }
    gpSig[g.index] = s;
  }
}

/* ================= beta 1.0: tocar la batería ================= */
function battPop(anchor) {
  let p = $("#battpop");
  if (p && p.classList.contains("show")) { p.classList.remove("show"); return; }
  if (!p) { p = document.createElement("div"); p.id = "battpop"; document.body.append(p); }
  const b = battInfo, plugged = deviceCharging || (b && (b.charging || b.plugged));
  p.innerHTML = `<b>${Math.round(deviceBatt * 100)} %</b><span>${esc(battEstimate())}${plugged && deviceBatt < .995 ? "" : plugged ? " · conectada al cargador" : ""}</span>`;
  const r = anchor.getBoundingClientRect();
  p.style.top = r.bottom + rem() * .9 + "px"; p.style.right = Math.max(rem(), innerWidth - r.right - rem() * .4) + "px";
  p.classList.add("show"); play("popup");
  clearTimeout(battPop.t); battPop.t = setTimeout(() => p.classList.remove("show"), 4000);
}
const closeBattPop = () => { const p = $("#battpop"); if (p) p.classList.remove("show"); };
$(".status .batt").addEventListener("click", e => { if (mode === "ui" && !QM.open) battPop(e.currentTarget); });

