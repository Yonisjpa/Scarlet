/* ================= beta 1.2: Internet (Wi-Fi) sin salir de Scarlet =================
   Ajustes › Red y VPN › Internet: redes registradas y encontradas (señal y candado), Y para buscar otra vez, conectarse
   (con teclado para la contraseña) con la animación Consola → Dispositivo de red → Internet, prueba de conexión,
   estado de la conexión y olvidar una red. */
const WIFI_IC = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><path d="M2.5 8.6a13.5 13.5 0 0 1 19 0"/><path d="M5.8 12a8.8 8.8 0 0 1 12.4 0"/><path d="M9.1 15.4a4.2 4.2 0 0 1 5.8 0"/><circle cx="12" cy="18.8" r="1.4" fill="currentColor" stroke="none"/></svg>';
const LOCK_IC = '<svg class="wlock" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linejoin="round"><rect x="5" y="10.5" width="14" height="10" rx="2"/><path d="M8 10.5V8a4 4 0 0 1 8 0v2.5"/></svg>';
/* Señal en tres barras (como en la consola): las que no llegan van tenues. */
const wifiBars = n => `<svg class="wbars" viewBox="0 0 24 20" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round">
  <path d="M2 7a14.5 14.5 0 0 1 20 0" opacity="${n >= 3 ? 1 : .25}"/><path d="M5.6 10.8a9.4 9.4 0 0 1 12.8 0" opacity="${n >= 2 ? 1 : .25}"/><path d="M9.1 14.6a4.4 4.4 0 0 1 5.8 0" opacity="${n >= 1 ? 1 : .25}"/><circle cx="12" cy="18" r="1.5" fill="currentColor" stroke="none"/></svg>`;
const WIFI = { data: null, at: 0 };
async function wifiLoad(scan) {
  const r = Host.has ? await Host.call("wifi.list", { scan: !!scan }).catch(e => ({ ok: false, error: e.message })) : fakeWifi("list", { scan });
  WIFI.data = r; WIFI.at = Date.now();
  return r;
}
function wifiView() {
  return {
    isWifi: true, rows: [],
    hints() { return [["B", "Atrás"], ["Y", "Buscar redes"], ["A", "Aceptar"]]; },
    render(el) {
      el.innerHTML = head(WIFI_IC, "Internet", "Buscando redes…") + `<div class="vbody wbody"><p class="note">Buscando redes…</p></div>`;
      this.paint(WIFI.data);
      wifiLoad(!WIFI.data).then(r => this.paint(r));
    },
    paint(r) {
      if (!this.el || !this.el.isConnected || !r) return;
      const sub = $(".vhead .sub", this.el), body = $(".wbody", this.el);
      const keep = (this.rows[this.i] || {}).key;
      const rows = this.rows = [];
      let html = "";
      if (!r.ok) {
        sub.textContent = "";
        html += `<p class="note">${esc(r.error || "No se pudo leer el Wi-Fi")}</p>`;
      } else {
        const cur = r.current, nets = r.networks || [];
        sub.textContent = cur ? `Conectado a ${cur.ssid}` : "Sin conexión";
        const byName = Object.fromEntries(nets.map(n => [n.ssid, n]));
        const netRow = (n, saved) => {
          const k = rows.length; rows.push({ key: "n:" + n.ssid, net: n, saved });
          const val = n.connected ? "Conectada" : saved && !n.visible ? "" : "";
          return `<button class="lrow fi wrow${n.connected ? " on" : ""}" data-k="${k}"><span class="meta"><b>${esc(n.ssid)}</b>${n.connected ? `<span>Conectada${n.band ? " · " + esc(n.band) : ""}</span>` : saved && !n.visible ? `<span>Fuera de alcance</span>` : n.band ? `<span>${esc(n.band)}</span>` : ""}</span><span class="wsig">${n.secure ? LOCK_IC : ""}${n.visible === false ? "" : wifiBars(n.bars)}</span>${val ? `<span class="val">${val}</span>` : ""}</button>`;
        };
        if (cur) {
          rows.push({ key: "status" }); rows.push({ key: "test" });
          html += `<div class="nssec">Conexión actual</div>
            <button class="lrow fi" data-k="0"><span class="meta"><b>Estado de la conexión</b><span>${esc(cur.ssid)} · ${esc(cur.security)}${cur.band ? " · " + esc(cur.band) : ""}</span></span><span class="wsig">${wifiBars(cur.signal >= 75 ? 3 : cur.signal >= 45 ? 2 : 1)}</span></button>
            <button class="lrow fi" data-k="1"><span class="meta"><b>Prueba de conexión</b><span>Revisa si hay internet y si llega a Steam</span></span><span class="val">›</span></button>`;
        }
        const saved = (r.saved || []).map(s => Object.assign({ ssid: s, bars: 0, visible: false, secure: true }, byName[s] ? { ...byName[s], visible: true } : {}));
        saved.sort((a, b) => (b.connected - a.connected) || (b.visible - a.visible) || (b.signal || 0) - (a.signal || 0));
        if (saved.length) html += `<div class="nssec">Redes registradas</div>` + saved.map(n => netRow(n, true)).join("");
        const found = nets.filter(n => !(r.saved || []).includes(n.ssid) && !n.saved).map(n => ({ ...n, visible: true }));
        html += `<div class="nssec">Redes encontradas</div>` + (found.length ? found.map(n => netRow(n, false)).join("")
          : `<p class="note">${r.location ? "Windows no deja ver las redes de alrededor sin permiso de ubicación." : "No se encontró ninguna otra red. Presiona Y para buscar de nuevo."}</p>`);
        if (r.location) {
          const k = rows.length; rows.push({ key: "loc" });
          html += `<button class="lrow fi" data-k="${k}"><span class="meta"><b>Permitir ver las redes</b><span>Windows › Privacidad › Ubicación › «Permitir que las aplicaciones de escritorio accedan a tu ubicación»</span></span><span class="val">Abrir ›</span></button>`;
        }
      }
      const k = rows.length; rows.push({ key: "win" });
      html += `<div class="nssec">Más</div><button class="lrow fi" data-k="${k}"><span class="meta"><b>Wi-Fi y red en Windows</b><span>Redes ocultas, empresariales y datos avanzados</span></span><span class="val">›</span></button>`;
      body.innerHTML = html;
      fis(body).forEach(b => b.onclick = () => { this.i = +b.dataset.k; updateFocus(); this.act(rows[+b.dataset.k]); });
      const j = rows.findIndex(x => x.key === keep);
      this.i = j >= 0 ? j : Math.min(this.i || 0, rows.length - 1); updateFocus();
    },
    async act(row) {
      if (!row) return;
      if (row.key === "status") { play("select"); return openView(wifiStatusView()); }
      if (row.key === "test") { play("select"); return openView(wifiConnectView(null, null, "test", () => this.refresh())); }
      if (row.key === "loc") { play("select"); return openSystemApp({ appid: 990005, name: "Ajustes de Windows", sub: "Ventana de Windows a pantalla completa", url: "ms-settings:privacy-location" }); }
      if (row.key === "win") return openSystemApp(SYS.winsettings);
      const n = row.net; play("select");
      if (n.connected) {
        const v = await choiceDialog(`${n.ssid}\nConectada`, [{ label: "Atrás", value: null }, { label: "Estado de la conexión", value: "status" }, { label: "Prueba de conexión", value: "test" }, { label: "Olvidar esta red", value: "forget" }], 1);
        if (v === "status") return openView(wifiStatusView());
        if (v === "test") return openView(wifiConnectView(null, null, "test", () => this.refresh()));
        if (v === "forget") return this.forget(n);
        return;
      }
      if (row.saved) {
        const opts = [{ label: "Atrás", value: null }, ...(n.visible ? [{ label: "Conectarse", value: "go" }] : []), { label: "Olvidar esta red", value: "forget" }];
        const v = await choiceDialog(`${n.ssid}\n${n.visible ? "Registrada" : "Registrada · fuera de alcance"}`, opts, 1);
        if (v === "go") return openView(wifiConnectView(n, null, "connect", () => this.refresh()));
        if (v === "forget") return this.forget(n);
        return;
      }
      if (!n.secure) return openView(wifiConnectView(n, null, "connect", () => this.refresh()));
      openView(passwordKeyboardView(n.ssid, pw => openView(wifiConnectView(n, pw, "connect", () => this.refresh()))));
    },
    async forget(n) {
      const ok = await confirmDialog(`¿Olvidar ${n.ssid}?\nPara volver a usarla tendrás que escribir su contraseña.`, "Olvidar");
      if (!ok) return;
      const r = Host.has ? await Host.call("wifi.forget", { name: n.ssid }).catch(e => ({ ok: false, error: e.message })) : fakeWifi("forget", { name: n.ssid });
      if (!r || !r.ok) return alertDialog((r && r.error) || "No se pudo olvidar");
      toast(`Se olvidó ${n.ssid}`); this.refresh();
    },
    refresh(scan) { if (!this.el || !this.el.isConnected) return; wifiLoad(scan).then(r => this.paint(r)); },
    onKey(k) {
      if (k === "y") { play("select"); $(".vhead .sub", this.el).textContent = "Buscando redes…"; this.refresh(true); return true; }
      return false;
    },
    items() { return fis(this.el); }
  };
}
/* Estado de la conexión: lo de la red actual en una lista. */
function wifiStatusView() {
  return {
    hints: [["B", "Atrás"], ["▲▼", "Desplazar"]],
    render(el) {
      const c = (WIFI.data && WIFI.data.current) || null;
      const row = (k, v) => v || v === 0 ? `<dt>${esc(k)}</dt><dd>${esc(String(v))}</dd>` : "";
      el.innerHTML = head(WIFI_IC, "Estado de la conexión", c ? c.ssid : "") + `<div class="vbody">${c ? `<dl class="kv">
        ${row("Nombre de la red (SSID)", c.ssid)}${row("Seguridad", c.security)}${row("Banda", c.band)}${row("Canal", c.channel || "")}
        ${row("Intensidad de la señal", c.signal + " %")}${row("Velocidad", c.rx ? `${c.rx} Mbps` : "")}
        ${row("Dirección IP", c.ip)}${row("Máscara de subred", c.mask)}${row("Puerta de enlace", c.gateway)}${row("DNS", c.dns)}
        ${row("Dirección MAC", c.mac)}${row("Punto de acceso (BSSID)", c.bssid)}${row("Adaptador", WIFI.data.adapter)}</dl>` : `<p class="note">No estás conectado a ninguna red Wi-Fi.</p>`}</div>`;
    },
    onKey(k) { if (k === "up" || k === "down") { const b = $(".vbody", this.el); if (b) b.scrollBy({ top: k === "down" ? 220 : -220, behavior: "smooth" }); return true; } return false; },
    items() { return []; }
  };
}
/* Conectarse / prueba de conexión: Consola → Dispositivo de red → Internet, cada tramo se llena al quedar listo. */
function wifiConnectView(net, password, what, after) {
  const CON = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><rect x="2" y="6.5" width="20" height="11" rx="3"/><circle cx="6.5" cy="12" r="1.6"/><path d="M16 10.5v3M14.5 12h3"/></svg>';
  const RTR = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="13" width="18" height="6" rx="1.6"/><path d="M7 13 5.5 6M17 13l1.5-7"/><circle cx="7.5" cy="16" r=".8" fill="currentColor"/><circle cx="10.5" cy="16" r=".8" fill="currentColor"/></svg>';
  const NET = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3c2.6 2.5 3.8 5.5 3.8 9s-1.2 6.5-3.8 9c-2.6-2.5-3.8-5.5-3.8-9S9.4 5.5 12 3z"/></svg>';
  return {
    cls: "dialog wcdlg", done: false,
    hints() { return this.done ? [["A", "Aceptar"]] : []; },
    render(el) {
      el.innerHTML = `<div class="dbox wcbox"><h3>${what === "test" ? "Prueba de conexión" : "Conectándose a " + esc(net.ssid)}</h3>
        <div class="wchain"><div class="wnode n0 ok"><i>${CON}</i><span>Consola</span></div><div class="wlink l1"><u></u></div><div class="wnode n1"><i>${RTR}</i><span>Dispositivo de red</span></div><div class="wlink l2"><u></u></div><div class="wnode n2"><i>${NET}</i><span>Internet</span></div></div>
        <p class="wmsg">${what === "test" ? "Probando la conexión…" : "Conectándose…"}</p><button class="key ok fi wok" hidden>Aceptar</button></div>`;
      $(".wok", el).onclick = () => this.close();
      this.run();
    },
    step(n, state) { const L = $(".l" + n, this.el), N = $(".n" + n, this.el); L.classList.remove("busy"); L.classList.add(state); N.classList.add(state); },
    busy(n) { $(".l" + n, this.el).classList.add("busy"); },
    async run() {
      const msg = t => { const m = $(".wmsg", this.el); if (m) m.textContent = t; };
      this.busy(1);
      if (what === "connect") {
        const r = Host.has ? await Host.call("wifi.connect", { ssid: net.ssid, password, sae: !!net.sae, open: !net.secure }).catch(e => ({ ok: false, error: e.message })) : await fakeWifi("connect", { ssid: net.ssid, password });
        if (!this.el.isConnected) return;
        if (!r || !r.ok) { this.step(1, "bad"); msg((r && r.error) || "No se pudo conectar"); return this.finish(false); }
      } else {
        const r = WIFI.data && WIFI.data.current;
        await wait(700);
        if (!r) { this.step(1, "bad"); msg("No estás conectado a ninguna red Wi-Fi"); return this.finish(false); }
      }
      this.step(1, "ok"); this.busy(2);
      const t = Host.has ? await Host.call("wifi.test").catch(() => ({ ok: false })) : await fakeWifi("test");
      if (!this.el.isConnected) return;
      if (t && t.ok) { this.step(2, "ok"); msg(`${what === "test" ? "La prueba de conexión fue exitosa" : "Conexión establecida"}${t.ms ? ` · ${t.ms} ms` : ""}${t.steam === false ? " · Steam no responde en esta red (prueba la VPN)" : ""}`); }
      else { this.step(2, "bad"); msg(t && t.portal ? "Hay red, pero pide iniciar sesión en una página (hotel, aeropuerto)" : "Conectado al dispositivo de red, pero sin internet"); }
      this.finish(true);
    },
    finish(ok) {
      this.done = true; this.ok = ok; play(ok ? "select" : "bump");
      const b = $(".wok", this.el); b.hidden = false; this.i = 0; updateFocus();
    },
    close() { if (!this.done) return; closeView(); if (after) after(); },
    onB() { if (this.done) this.close(); return true; },
    onKey(k) { if (k === "a" && this.done) { this.close(); return true; } return !this.done; },
    items() { return this.done ? [$(".wok", this.el)] : []; }
  };
}
/* Teclado para contraseñas: letras, números, una página de símbolos (#+=), Mostrar/Ocultar, y se ve con puntos. */
function passwordKeyboardView(ssid, onDone) {
  const tail = isSym => ["Mayús", isSym ? "abc" : "#+=", "Espacio", "Mostrar", "Borrar", "Listo"];
  const PAGES = {
    abc: [[..."1234567890"], [..."qwertyuiop"], [..."asdfghjklñ"], [..."zxcvbnm-_."], tail(false)],
    sym: [[..."1234567890"], [..."!@#$%^&*()"], [..."~`|\\{}[]<>"], [..."+=/?:;\"',"], tail(true)]
  };
  let page = "abc";
  const rows = () => PAGES[page], last = 4, max = 63;
  const flatIndex = (r, c) => rows().slice(0, r).reduce((s, x) => s + x.length, 0) + c;
  return {
    text: "", r: 1, c: 0, i: flatIndex(1, 0), show: false, shift: false,
    hints: [["B", "Borrar"], ["Y", "Mostrar"], ["☰", "Listo"], ["A", "Escribir"]],
    render(el) {
      el.innerHTML = head(LOCK_IC.replace(' class="wlock"', ""), "Contraseña", ssid) + `<div class="kb"><div class="kbfield"><span class="kbtext"></span><i class="caret"></i></div><div class="kbcount"></div><div class="kbrows"></div></div>`;
      this.keys();
    },
    keys() {
      const box = $(".kbrows", this.el);
      box.innerHTML = rows().map((row, ri) => `<div class="kbrow">${row.map((k, ci) => `<button class="key fi${ri === last ? " wide" : ""}${k === "Listo" ? " ok" : ""}" data-r="${ri}" data-c="${ci}">${esc(k)}</button>`).join("")}</div>`).join("");
      fis(box).forEach(b => b.onclick = () => { this.r = +b.dataset.r; this.c = +b.dataset.c; this.i = flatIndex(this.r, this.c); updateFocus(); this.press(); });
      this.upd();
    },
    syncFromIndex() { let n = this.i; for (let r = 0; r < rows().length; r++) { if (n < rows()[r].length) { this.r = r; this.c = n; return; } n -= rows()[r].length; } },
    upd() {
      const t = $(".kbtext", this.el);
      $$(".key", this.el).forEach(b => { if (b.textContent.length === 1) b.style.textTransform = this.shift && page === "abc" ? "uppercase" : ""; });
      const sk = $$(".key", this.el).find(b => b.textContent === "Mayús"); if (sk) sk.classList.toggle("on", !!this.shift);
      const mk = $$(".key", this.el).find(b => b.textContent === "Mostrar" || b.textContent === "Ocultar"); if (mk) { mk.textContent = this.show ? "Ocultar" : "Mostrar"; mk.classList.toggle("on", this.show); }
      t.innerHTML = this.text ? esc(this.show ? this.text : "•".repeat(this.text.length)) : `<span class="ph">Escribe la contraseña…</span>`;
      $(".kbcount", this.el).textContent = this.text ? `${this.text.length} caracteres` : "";
    },
    press() {
      const k = rows()[this.r][this.c];
      if (k === "Mayús") { this.shift = !this.shift; play("tick"); return this.upd(); }
      if (k === "#+=" || k === "abc") { page = page === "abc" ? "sym" : "abc"; play("tick"); this.keys(); this.i = flatIndex(this.r, Math.min(this.c, rows()[this.r].length - 1)); this.syncFromIndex(); return updateFocus(); }
      if (k === "Mostrar" || k === "Ocultar") { this.show = !this.show; play("tick"); return this.upd(); }
      if (k === "Espacio") return this.type(" ");
      if (k === "Borrar") return this.del();
      if (k === "Listo") return this.done();
      this.type(k);
    },
    type(ch) {
      if (this.text.length >= max) return play("bump");
      if (this.shift && /\p{L}/u.test(ch)) { ch = ch.toUpperCase(); this.shift = false; }
      this.text += ch; play("type"); this.upd();
    },
    del() { if (!this.text) return play("bump"); this.text = this.text.slice(0, -1); play("back"); this.upd(); },
    done() {
      if (this.text.length < 8) { play("bump"); return toast("La contraseña de una red Wi-Fi tiene al menos 8 caracteres"); }
      closeView(); onDone(this.text);
    },
    nav(dir) {
      let r = this.r, c = this.c;
      if (dir === "left") c--; else if (dir === "right") c++;
      else {
        const nr = r + (dir === "down" ? 1 : -1);
        if (nr < 0 || nr >= rows().length) return bump(this.items()[this.i], dir);
        c = Math.round(c * (rows()[nr].length - 1) / Math.max(1, rows()[r].length - 1)); r = nr;
      }
      if (c < 0 || c >= rows()[r].length) return bump(this.items()[this.i], dir);
      this.r = r; this.c = c; this.i = flatIndex(r, c); play("move"); updateFocus();
    },
    onKey(k) {
      if (k === "x") { this.del(); return true; }
      if (k === "y") { this.show = !this.show; play("tick"); this.upd(); return true; }
      if (k === "start") { this.done(); return true; }
      return false;
    },
    onB() { if (!this.text) return false; this.del(); return true; },
    onType(ch) { this.type(ch); },
    items() { return fis(this.el); }
  };
}
/* Sin la app (prototipo): redes de ejemplo. */
const FAKEWIFI = { saved: ["Casa_5G", "Oficina"], current: "Casa_5G" };
function fakeWifi(op, a = {}) {
  const near = [{ ssid: "Casa_5G", signal: 88, bars: 3, secure: true, security: "WPA2-Personal", band: "5 GHz" }, { ssid: "Casa", signal: 70, bars: 2, secure: true, security: "WPA2-Personal", band: "2.4 GHz" },
    { ssid: "Vecinos-2.4", signal: 40, bars: 1, secure: true, security: "WPA3-Personal", band: "2.4 GHz", sae: true }, { ssid: "Cafeteria", signal: 55, bars: 2, secure: false, security: "Abierta", band: "2.4 GHz" }];
  if (op === "list") return { ok: true, adapter: "Prototipo Wi-Fi", saved: [...FAKEWIFI.saved], location: false,
    networks: near.map(n => ({ ...n, saved: FAKEWIFI.saved.includes(n.ssid), connected: n.ssid === FAKEWIFI.current, connectable: true })),
    current: FAKEWIFI.current ? { ssid: FAKEWIFI.current, security: "WPA2-Personal", band: "5 GHz", channel: 44, signal: 88, rx: 866, ip: "192.168.1.42", mask: "255.255.255.0", gateway: "192.168.1.1", dns: "192.168.1.1", mac: "A4:B1:C1:00:11:22", bssid: "10:20:30:40:50:60" } : null };
  if (op === "forget") { FAKEWIFI.saved = FAKEWIFI.saved.filter(s => s !== a.name); if (FAKEWIFI.current === a.name) FAKEWIFI.current = null; return { ok: true }; }
  if (op === "connect") return wait(900).then(() => { if (a.password && a.password !== "contraseña1") return { ok: false, error: "No se pudo conectar. Revisa la contraseña." }; FAKEWIFI.current = a.ssid; if (!FAKEWIFI.saved.includes(a.ssid)) FAKEWIFI.saved.push(a.ssid); return { ok: true }; });
  if (op === "test") return wait(700).then(() => ({ ok: true, steam: true, ms: 38 }));
}

