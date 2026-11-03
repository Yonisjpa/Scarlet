/* ================= cuentas y arranque (dentro de la interfaz, con el control) ================= */
function accountsView() {
  let st = { steamId: "", steamKey: false, sgdbKey: false, autostart: false, gamebar: false };
  let dirty = false;
  const set = async (field, value) => {
    try { st = await Host.call("setup.set", { field, value }); dirty = dirty || /Key|steamId/.test(field); play("select"); v.refresh(); }
    catch (e) { toast(e.message || "No se pudo guardar"); play("bump"); }
  };
  const typeInto = (title, field, keys, max, placeholder) => openView(keyboardView("", txt => { if (txt.trim()) set(field, txt.trim()); }, null, { title, keys, max, placeholder, paste: true }));
  const rows = [
    { label: "SteamID64", desc: "Tu cuenta de Steam (17 dígitos, empieza con 7656119). Se detecta sola.", val: () => st.steamId || "Sin configurar",
      act() { typeInto("SteamID64", "steamId", "digits", 17, "7656119…"); } },
    { label: "API key de Steam", desc: "Para juegos, amigos y logros. Sácala en steamcommunity.com/dev/apikey (tu perfil debe ser público).", val: () => st.steamKey ? "Configurada ✓" : "Sin configurar",
      act() { typeInto("API key de Steam", "steamKey", "hexUpper", 32, "32 caracteres (0-9, A-F)"); } },
    { label: "API key de SteamGridDB", desc: "Para las portadas. steamgriddb.com › Preferencias › API.", val: () => st.sgdbKey ? "Configurada ✓" : "Sin configurar",
      act() { typeInto("API key de SteamGridDB", "sgdbKey", "hexLower", 32, "32 caracteres (0-9, a-f)"); } },
    { label: "Abrir Scarlet al iniciar sesión", desc: "Con AnyFSE como app de inicio no hace falta, pero no estorba.", val: () => st.autostart ? "Sí" : "No",
      toggle() { set("autostart", !st.autostart); } },
    { label: "Botón Xbox solo para Scarlet", desc: "Quita el botón Xbox a la Game Bar (también en modo Xbox), para el toque, doble toque y mantener.", val: () => st.gamebar ? "Sí" : "No",
      toggle() { set("gamebar", !st.gamebar); } },
    { label: "App de inicio del modo Xbox", desc: "Para que la consola arranque directo en Scarlet: AnyFSE (Microsoft Store) › Browse › Scarlet.exe.", val: () => "Abrir tienda ›",
      act() { Host.call("system.open", { uri: "ms-windows-store://pdp/?ProductId=XPFM6KZ5SD704G" }).catch(() => {}); } }
  ];
  const v = {
    hints() { return [["B", dirty ? "Guardar y recargar" : "Atrás"], ["A", rows[this.i || 0].toggle ? "Cambiar" : "Editar"]]; },
    render(el) {
      el.innerHTML = head(I.gear, "Cuentas y arranque") + `<div class="vbody">${rows.map((r, i) => `<button class="lrow fi" data-i="${i}"><span class="meta"><b>${esc(r.label)}</b><span>${esc(r.desc)}</span></span><span class="val">${esc(r.val())}</span></button>`).join("")}</div>`;
      fis(el).forEach(b => b.onclick = () => { const r = rows[+b.dataset.i]; (r.toggle || r.act).call(r); });
      if (Host.has) Host.call("setup.get").then(x => { if (x) { st = x; this.refresh(); } }).catch(() => {});
    },
    refresh() { if (!this.el) return; fis(this.el).forEach(b => { b.querySelector(".val").textContent = rows[+b.dataset.i].val(); }); updateFocus(); },
    onLR(i, d) { const r = rows[i]; if (r.toggle) r.toggle(); else bump(this.items()[i], d); },
    onBack() { if (dirty) { toast("Guardado. Recargando tus datos…"); setTimeout(() => Host.call("app.reload").catch(() => {}), 600); } },
    items() { return fis(this.el); }
  };
  return v;
}

