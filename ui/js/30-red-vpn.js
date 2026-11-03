/* ================= red y VPN =================
   NET: ¿hay internet de verdad y llega a Steam? (lo revisa la app). VPN: Cloudflare WARP o un perfil de WireGuard
   (Proton VPN u otro), que se prenden desde el menú rápido o Ajustes › VPN. */
const NET = { internet: true, steam: true };
const VPN = { provider: "none", status: "off", warp: false, wireguard: false, profile: null, busy: false };
const netOn = () => navigator.onLine && NET.internet;
const WIFI_OFF_IC = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M2.5 9a14 14 0 0 1 6.2-3.3M14.6 5.3A14 14 0 0 1 21.5 9M5.5 12.3a9.5 9.5 0 0 1 3.4-2M18.5 12.3a9.5 9.5 0 0 0-2.2-1.6M8.6 15.6a5 5 0 0 1 6.8 0"/><circle cx="12" cy="19" r="1.2" fill="currentColor"/><path d="M3 3l18 18"/></svg>';
const SHIELD_IC = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3l7 3v5.5c0 4.4-3 8-7 9.5-4-1.5-7-5.1-7-9.5V6z"/><path d="M9.2 12.2l2 2 3.8-4"/></svg>';
const vpnOn = () => VPN.status === "on";
const vpnLabel = () => VPN.busy ? (VPN.status === "on" ? "Desconectando…" : "Conectando…") : VPN.status === "on" ? "Conectada" : VPN.status === "connecting" ? "Conectando…" : "Desconectada";
const vpnName = () => VPN.provider === "warp" ? "Cloudflare WARP" : VPN.provider === "wireguard" ? "WireGuard · " + (VPN.profile || "Perfil") : "Sin configurar";
function paintNet() {
  $$(".wifi").forEach(w => w.style.opacity = netOn() && (typeof sys === "undefined" || (sys.wifi && !sys.airplane)) ? 1 : .3);
  $$(".vpnchip").forEach(c => c.hidden = !vpnOn());
  if (typeof QM !== "undefined" && QM.open) renderQuick();
  const v = topView(); if (v && v.isVpn && v.refresh) v.refresh();
}
function loadVpn() {
  if (!Host.has) return Promise.resolve();
  return Host.call("vpn.state").then(r => { if (r && !r.error && !VPN.busy) { Object.assign(VPN, r); paintNet(); } }).catch(() => {});
}
/* Prender / apagar la VPN (desde el menú rápido o Ajustes). WireGuard la primera vez pide un permiso de Windows. */
async function vpnSet(on) {
  if (VPN.busy) return;
  if (!Host.has) { VPN.status = on ? "on" : "off"; play(on ? "on" : "off"); return paintNet(); }
  if (VPN.provider === "none") { play("bump"); return toast("Instala Cloudflare WARP o importa un perfil de WireGuard en Ajustes › VPN"); }
  VPN.busy = true; play(on ? "on" : "off"); paintNet();
  let r = await Host.call("vpn.set", { on }).catch(e => ({ error: e.message === "timeout" ? "La VPN tardó demasiado en contestar" : e.message }));
  if (r && r.error === "setup") {
    VPN.busy = false; paintNet();
    if (OVERLAY || QM.open) { if (QM.open) closeQuick(true); if (OVERLAY) return toast("Abre Ajustes › VPN para prepararla (una sola vez)"); }
    const ok = await confirmDialog("Para prender WireGuard sin preguntar cada vez, Windows te pedirá permiso UNA sola vez.\nAcepta en la ventana de Windows.", "Continuar");
    if (!ok) return;
    toast("Acepta el permiso en la ventana de Windows");
    const s = await Host.call("vpn.setup").catch(e => ({ ok: false, error: e.message === "timeout" ? "Si aceptaste el permiso, vuelve a intentarlo" : e.message }));
    if (!s || !s.ok) return toast((s && s.error) || "No se pudo preparar la VPN");
    VPN.busy = true; paintNet();
    r = await Host.call("vpn.set", { on }).catch(e => ({ error: e.message }));
  }
  VPN.busy = false;
  if (r && r.status) Object.assign(VPN, r);
  if (r && r.error) toast(r.error);
  else if (r && r.status === "on") toast(`VPN conectada (${vpnName()})`);
  else if (r && r.status === "off" && !on) toast("VPN desconectada");
  paintNet();
}
Events.on("net.state", async d => {
  if (!d) return;
  const was = { ...NET }; Object.assign(NET, { internet: d.internet !== false, steam: d.steam !== false });
  paintNet();
  await loadVpn();   // para sugerir lo correcto (prender la VPN o instalar una)
  if (was.internet && !NET.internet) notify({ key: "net", icon: WIFI_OFF_IC, title: "Sin conexión a internet", text: "Puedes jugar lo que tienes instalado" });
  else if (!was.internet && NET.internet) notify({ key: "net", icon: QI.wifi.replace(' class="qi"', ""), title: "Conectado a internet", text: NET.steam ? "Todo en orden" : "Pero esta red bloquea Steam" });
  if (NET.internet && !NET.steam && (was.steam || !was.internet))
    notify({ key: "net", icon: SHIELD_IC, title: "Esta red bloquea Steam", text: vpnOn() ? "Aun con la VPN: prueba apagarla y prenderla" : VPN.provider !== "none" ? "Prende la VPN en el menú rápido" : "Usa una VPN: Ajustes › VPN" });
  paintNet();
});
Events.on("steam.offline", () => notify({ icon: WIFI_OFF_IC, title: "Steam sin conexión", text: "Toca «Iniciar en modo sin conexión»" }));
/* Steam se reinicia para reconectarse (al prender la VPN o al regresar el internet). */
Events.on("steam.reconnect", d => {
  const st = d && d.state;
  if (st === "restarting") notify({ key: "steamre", icon: SHIELD_IC, title: "Reconectando Steam", text: "Se reinicia para detectar la conexión (unos segundos)" });
  else if (st === "done") notify({ key: "steamre", icon: SHIELD_IC, title: "Steam en línea", text: "Ya puedes instalar y ver a tus amigos" });
  else if (st === "later") notify({ key: "steamre", icon: SHIELD_IC, title: "Steam se reconectará", text: "Cuando cierres el juego (reiniciarlo ahora lo cerraría)" });
  else if (st === "failed") notify({ key: "steamre", icon: WIFI_OFF_IC, title: "Steam no terminó de reiniciar", text: "Ábrelo desde la Tienda o espera un momento" });
});
if (Host.has) { Host.call("net.state").then(d => { if (d) { NET.internet = d.internet !== false; NET.steam = d.steam !== false; paintNet(); } }).catch(() => {}); loadVpn(); }

/* Ajustes › VPN */
function vpnView() {
  const open = url => { if (Host.has) Host.call("web.open", { url }).catch(() => {}); else toast("Prototipo: aquí se abriría " + url); };
  const rows = [
    { label: "VPN", desc: "Para redes que bloquean Steam o Xbox. También en el menú rápido.", val: () => VPN.provider === "none" ? "Sin configurar" : vpnLabel(),
      act() { if (VPN.provider === "none") { play("bump"); return toast("Primero instala Cloudflare WARP o importa un perfil de WireGuard"); } vpnSet(!vpnOn()); } },
    { label: "Usar", desc: "Cuál VPN prende el interruptor", val: () => vpnName(),
      act() {
        const can = [VPN.warp && "warp", VPN.wireguard && VPN.profile && "wireguard"].filter(Boolean);
        if (can.length < 2) { play("bump"); return toast(can.length ? "Solo tienes " + vpnName() : "Todavía no hay ninguna VPN lista"); }
        const next = VPN.provider === "warp" ? "wireguard" : "warp";
        if (vpnOn()) return toast("Apaga la VPN antes de cambiarla");
        Host.call("vpn.provider", { provider: next }).then(r => { if (r) Object.assign(VPN, r); play("select"); paintNet(); }).catch(() => {});
      } },
    { label: "Cloudflare WARP", desc: "Gratis y sin cuenta. Cifra tu conexión para que la red no bloquee Steam ni Xbox (no eliges país).", val: () => VPN.warp ? "Instalada ✓" : "Descargar ›",
      act() { if (VPN.warp) return toast("WARP ya está instalada"); open("https://one.one.one.one/"); toast("Instálala y vuelve a Scarlet"); } },
    { label: "WireGuard", desc: "Para perfiles .conf de Proton VPN (gratis) u otro proveedor. Bájalo a Descargas y elige Importar.", val: () => !VPN.wireguard ? "Descargar ›" : VPN.profile ? VPN.profile : "Importar perfil ›",
      async act() {
        if (!VPN.wireguard) { open("https://www.wireguard.com/install/"); return toast("Instálalo y vuelve a Scarlet"); }
        let what = "import";
        if (VPN.profile) {
          what = await choiceDialog(`Perfil de WireGuard: ${VPN.profile}`, [{ label: "Cancelar", value: null }, { label: "Quitar", value: "remove" }, { label: "Importar otro", value: "import" }], 2);
          if (!what) return;
        }
        if (what === "remove") { const r = await Host.call("vpn.remove").catch(() => null); if (r) Object.assign(VPN, r); toast("Perfil quitado"); return paintNet(); }
        const r = await Host.call("vpn.import").catch(e => ({ ok: false, error: e.message }));
        if (!r || !r.ok) return alertDialog((r && r.error) || "No se pudo importar");
        toast("Perfil importado: " + r.name); await loadVpn();
      } },
    { label: "Conseguir un perfil de Proton VPN", desc: "account.protonvpn.com › Descargas › Configuraciones de WireGuard (con cuenta gratis)", val: () => "Abrir ›", act() { open("https://account.protonvpn.com/downloads"); } }
  ];
  return {
    isVpn: true,
    hints() { return [["B", "Atrás"], ["A", "Aceptar"]]; },
    render(el) {
      el.innerHTML = head(SHIELD_IC, "VPN") + `<div class="vbody">${rows.map((r, i) => `<button class="lrow fi" data-i="${i}"><span class="meta"><b>${esc(r.label)}</b><span>${esc(r.desc)}</span></span><span class="val">${esc(r.val())}</span></button>`).join("")}</div>`;
      fis(el).forEach(b => b.onclick = () => { const r = rows[+b.dataset.i]; Promise.resolve(r.act.call(r)).then(() => this.refresh()); });
      loadVpn().then(() => this.refresh());
    },
    refresh() { if (!this.el || !this.el.isConnected) return; fis(this.el).forEach(b => { b.querySelector(".val").textContent = rows[+b.dataset.i].val(); }); },
    items() { return fis(this.el); }
  };
}

