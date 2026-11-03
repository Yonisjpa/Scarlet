/* ================= instalar con Steam, directo (sin sus ventanas) =================
   Scarlet le pide a Steam su asistente de instalación (escondido) y muestra el suyo: portada, tamaño, disco con su
   espacio libre y, si el juego lo tiene, el contrato de licencia. Si Steam pide algo que Scarlet no maneja (clave de
   producto, contraseña…) o la conexión no está activa, se usa la ventanita de Steam de siempre. */
const fmtBytes = b => { if (!(b > 0)) return "—"; const gb = b / 1073741824; return gb >= 1 ? `${gb.toFixed(2)} GB` : `${Math.max(1, Math.round(b / 1048576))} MB`; };
const DRIVE_IC = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M4.2 15 6.4 6.6A2 2 0 0 1 8.3 5.1h7.4a2 2 0 0 1 1.9 1.5L19.8 15"/><rect x="3.5" y="14.6" width="17" height="5" rx="1.5"/><path d="M7.2 17.1h.01M10.2 17.1h.01"/></svg>';
const STAR_IC = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="m12 2.8 2.8 5.8 6.3.9-4.6 4.4 1.1 6.3L12 17.2l-5.6 3 1.1-6.3L2.9 9.5l6.3-.9z"/></svg>';
let installing = 0;   // un solo asistente a la vez (Steam tiene uno)

function dropView(v) {
  if (!views.includes(v)) return;
  if (topView() === v) return closeView();
  views.splice(views.indexOf(v), 1); v.el.remove(); updateFocus();
}
/* Lo que eligió la persona en un diálogo (se guarda si llega antes de que alguien lo espere). */
function actOn(v, a) { if (v.onAct) { const f = v.onAct; v.onAct = null; f(a); } else v.queued = a; }
function nextAct(v) {
  return new Promise(res => {
    if (v.queued !== undefined) { const a = v.queued; v.queued = undefined; return res(a); }
    v.onAct = res;
    const t = setInterval(() => { if (!views.includes(v)) { clearInterval(t); actOn(v, "cancel"); } else if (!v.onAct) clearInterval(t); }, 700);   // si se cerró por fuera (suspender, un aviso)
  });
}
const alertDialog = text => choiceDialog(text, [{ label: "Aceptar", value: true }], 0);
/* Portada horizontal de Steam (como en la consola); si no hay, la del mosaico. */
function capsule(img, g) {
  if ((g.source || "steam") !== "steam") return Art.apply(img, g);
  img.onerror = () => { img.onerror = null; Art.apply(img, g); };
  const url = `https://cdn.cloudflare.steamstatic.com/steam/apps/${g.appid}/header.jpg`;
  Art.local(url).then(u => { if (u) img.src = u; else Art.apply(img, g); }).catch(() => { img.src = url; });
}
/* En el navegador (sin la app), un Steam de mentiras para probar los diálogos. */
const demoSteam = {
  async call(t, p) {
    await wait(t === "install.prepare" ? 700 : 900);
    const g = byId(p.appid) || {};
    if (t === "install.prepare") return { mode: "cdp", size: 64.97 * 1073741824 * (g.appid % 3 ? .4 : 1), folder: 0, canChange: true, folders: [{ i: 0, name: "Disco local (C:)", free: 76.61 * 1073741824, def: true, ok: true }, { i: 1, name: "Juegos (D:)", free: 812.4 * 1073741824, def: false, ok: true }] };
    if (t === "install.folder") return { ok: true, folder: p.folder };
    if (t === "install.go") return g.appid % 4 === 0 ? { s: "eula", eulas: [{ id: "demo", version: 1, url: "", html: "<h1>Acuerdo de usuario</h1><p>Última actualización: 17 de julio de 2025.</p><p>1. ¡Hola! Este es un contrato de ejemplo para probar el diálogo.</p><p>2. Lo que ves aquí son las normas que regulan el uso del juego.</p>".repeat(6) }] } : { s: "started" };
    if (t === "install.eula") return p.accept ? { s: "started" } : { s: "canceled" };
    return true;
  }
};
const steamCall = (t, p = {}) => Host.has ? Host.call(t, p) : demoSteam.call(t, p);

async function startInstall(g) {
  if ((g.source || "steam") !== "steam") {   // juegos de otras tiendas: como antes
    const ok = await confirmDialog(`${g.name} no está instalado.\n¿Quieres instalarlo?`, "Instalar");
    if (ok) installViaSteam(g);
    return;
  }
  if (installing) { bump(homeEl()); return toast("Espera: Steam está preparando otra instalación"); }
  installing = g.appid;
  try { await installFlow(g); }
  catch (e) { toast("No se pudo instalar: " + e.message); }
  finally { installing = 0; }
}

function installView(g) {
  const v = {
    cls: "dialog", isInstall: true, phase: "load", msg: "Preparando con Steam…", size: 0, folders: [], sel: -1, i: 0,
    hints() { return this.phase === "busy" ? [] : [["B", "Cancelar"], ["A", "Aceptar"]]; },
    render(el) {
      el.innerHTML = `<div class="dbox inst"><h3 class="dh">Instalar</h3><div class="dsec"><div class="irow"><img class="icap" alt=""><b>${esc(g.name)}</b><span class="isz"></span></div><div class="ibody"></div></div><div class="dbtns"><button class="fi" data-b="cancel">Cancelar</button><button class="fi" data-b="go">Instalar</button></div></div>`;
      capsule($(".icap", el), g);
      $$(".dbtns button", el).forEach(b => b.onclick = () => {
        if (this.phase === "busy") return;
        if (b.dataset.b === "cancel") return actOn(this, "cancel");
        if (this.phase !== "config") return bump(b);
        actOn(this, "go");
      });
      this.paint();
    },
    focusKey() { const it = this.items()[this.i]; return it ? (it.dataset.b ? "b:" + it.dataset.b : "f:" + it.dataset.f) : "b:cancel"; },
    paint() {
      const el = this.el; if (!el) return;
      const key = this.focusKey();
      $(".isz", el).textContent = this.phase === "load" ? "" : fmtBytes(this.size);
      const usable = this.folders.filter(f => f.ok), multi = usable.length > 1 && this.canChange !== false;
      const fo = this.folders.find(f => f.i === this.sel);
      this.short = !!(fo && this.size > 0 && fo.free >= 0 && fo.free < this.size);
      const body = $(".ibody", el);
      if (this.phase === "load") body.innerHTML = `<div class="iload"><i></i>${esc(this.msg)}</div>`;
      else body.innerHTML = `<div class="ilab">Instalar en:</div><div class="idrives">${this.folders.map(f =>
        `<button class="idrv${multi && f.ok ? " fi" : ""}${f.i === this.sel ? " sel" : ""}${f.ok ? "" : " off"}" data-f="${f.i}">${DRIVE_IC}<span class="nm">${esc(f.name)}</span><span class="fr">${f.def ? STAR_IC : ""}${f.ok ? "Espacio libre: " + fmtBytes(f.free) : "No disponible"}</span></button>`).join("") || `<div class="iload">Steam no tiene bibliotecas</div>`}</div>` +
        (this.short ? `<div class="iwarn">No cabe en este disco: faltan ${fmtBytes(this.size - fo.free)}.${multi ? " Elige otro disco." : ""}</div>` : "");
      $$(".idrv.fi", el).forEach(b => b.onclick = () => { if (this.phase === "config") actOn(this, { folder: +b.dataset.f }); });
      const go = $('[data-b="go"]', el), cancel = $('[data-b="cancel"]', el);
      go.classList.toggle("off", this.phase === "load" || this.short);
      go.classList.toggle("busy", this.phase === "busy"); cancel.classList.toggle("off", this.phase === "busy");
      go.innerHTML = this.phase === "busy" ? '<i class="sp"></i>Instalando…' : "Instalar";
      const items = this.items(), k = items.findIndex(x => (x.dataset.b ? "b:" + x.dataset.b : "f:" + x.dataset.f) === key);
      this.i = k >= 0 ? k : items.indexOf(cancel);
      if (topView() === this) updateFocus();
    },
    set(o) { Object.assign(this, o); this.paint(); },
    items() { return fis(this.el); },
    nav(dir) {
      const items = this.items(), cur = items[this.i];
      const to = x => { this.i = items.indexOf(x); play("move"); updateFocus(); return true; };
      const no = () => { bump(cur, dir); return true; };
      if (!cur || this.phase === "busy") return no();
      const drv = items.filter(x => x.classList.contains("idrv")), btn = items.filter(x => !x.classList.contains("idrv"));
      if (cur.classList.contains("idrv")) {
        const k = drv.indexOf(cur);
        if (dir === "up") return k > 0 ? to(drv[k - 1]) : no();
        if (dir === "down") return k < drv.length - 1 ? to(drv[k + 1]) : to(btn[btn.length - 1]);
        return no();
      }
      const k = btn.indexOf(cur);
      if (dir === "left") return k > 0 ? to(btn[k - 1]) : no();
      if (dir === "right") return k < btn.length - 1 ? to(btn[k + 1]) : no();
      if (dir === "up" && drv.length) return to(drv.find(d => +d.dataset.f === this.sel) || drv[drv.length - 1]);
      return no();
    },
    onKey(k) { return ["start", "x", "y", "lb", "rb", "lt", "rt"].includes(k) || (this.phase === "busy" && k !== "power"); },
    onB() { if (this.phase !== "busy") actOn(this, "cancel"); return true; }
  };
  return v;
}

/* Convierte la página del contrato en párrafos de texto (nunca se inserta su HTML ni se ejecuta nada). */
function eulaText(html) {
  if (!html) return null;
  let doc; try { doc = new DOMParser().parseFromString(html, "text/html"); } catch (e) { return null; }
  if (!doc.body) return null;
  doc.querySelectorAll("script,style,noscript,svg,iframe,img,video,header,footer,nav,form,button,select,input,#global_header,#footer,#footer_spacer,.responsive_page_menu_ctn,.responsive_header,#store_header,.page_header_ctn,.breadcrumbs").forEach(e => e.remove());
  const len = e => (e.textContent || "").replace(/\s+/g, " ").trim().length;
  let root = doc.querySelector("#eula_content,.eula_content,#eula,.eula,main,article") || doc.body;
  if (len(root) < 80) root = doc.body;
  for (let k = 0; k < 14; k++) {   // baja al bloque que tiene casi todo el texto (quita menús y pies de página)
    const total = len(root); if (!total) break;
    const big = [...root.children].find(c => len(c) >= total * .8);
    if (!big) break; root = big;
  }
  const out = []; let cur = "", head = false;
  const BLOCK = /^(P|DIV|LI|H[1-6]|BR|TR|SECTION|ARTICLE|UL|OL|TABLE|BLOCKQUOTE|PRE|DT|DD)$/;
  const flush = () => { const t = cur.replace(/[ \t\r\n ]+/g, " ").trim(); if (t) out.push({ t, h: head }); cur = ""; head = false; };
  const walk = n => {
    if (n.nodeType === 3) { cur += n.nodeValue; return; }
    if (n.nodeType !== 1) return;
    const b = BLOCK.test(n.tagName);
    if (b) flush();
    if (/^H[1-6]$/.test(n.tagName)) head = true;
    if (n.tagName === "LI") {   // listas numeradas conservan su número (las cláusulas de los contratos)
      const ol = n.parentElement && n.parentElement.tagName === "OL" ? n.parentElement : null;
      cur += ol ? `${(parseInt(ol.getAttribute("start"), 10) || 1) + [...ol.children].filter(c => c.tagName === "LI").indexOf(n)}. ` : "• ";
    }
    n.childNodes.forEach(walk);
    if (b) flush();
  };
  walk(root); flush();
  return out.reduce((a, x) => a + x.t.length, 0) > 80 ? out : null;
}

function eulaView(g, eulas) {
  const v = {
    cls: "dialog", isInstall: true, cols: 2, i: 1, busy: false,
    hints() { return this.busy ? [] : [["B", "Cancelar"], ["A", "Aceptar"]]; },
    render(el) {
      el.innerHTML = `<div class="dbox eula"><h3 class="dh">Contrato de licencia</h3><div class="dsec"><p class="esub">Para jugar ${esc(g.name)} debes aceptar el Contrato de Licencia de Usuario Final. Léelo completo:</p><div class="etext"><div class="eload"><i></i>Cargando el contrato…</div></div></div><div class="dbtns"><button class="fi" data-b="cancel">Cancelar</button><button class="fi" data-b="accept">Aceptar</button></div></div>`;
      $$(".dbtns button", el).forEach(b => b.onclick = () => { if (!this.busy) actOn(this, b.dataset.b); });
      requestAnimationFrame(() => {   // el texto se arma después de abrir, para que el diálogo salga al instante
        const t = $(".etext", el); if (!t) return;
        const many = eulas.length > 1;
        const parts = eulas.map((e, k) => {
          const paras = eulaText(e.html);
          const head = many ? `<h4>Contrato ${k + 1} de ${eulas.length}</h4>` : "";
          if (!paras) return head + `<p>No se pudo cargar el texto de este contrato${e.url ? ". Está en:\n" + esc(e.url) : "."}</p>`;
          return head + paras.map(x => x.h ? `<h4>${esc(x.t)}</h4>` : `<p>${esc(x.t)}</p>`).join("");
        });
        t.innerHTML = parts.join("") || "<p>Steam no mandó el texto del contrato.</p>";
        t.scrollTop = 0;
      });
    },
    set(o) {
      Object.assign(this, o);
      const a = $('[data-b="accept"]', this.el), c = $('[data-b="cancel"]', this.el);
      if (a) { a.innerHTML = this.busy ? '<i class="sp"></i>Instalando…' : "Aceptar"; a.classList.toggle("busy", this.busy); }
      if (c) c.classList.toggle("off", this.busy);
      if (topView() === this) updateFocus();
    },
    items() { return fis(this.el); },
    nav(dir) {   // arriba / abajo: leer el contrato
      if (dir !== "up" && dir !== "down") return this.busy ? true : false;
      const t = $(".etext", this.el); if (!t) return true;
      const max = t.scrollHeight - t.clientHeight;
      if ((dir === "down" && t.scrollTop >= max - 1) || (dir === "up" && t.scrollTop <= 0)) { bump(this.items()[this.i], dir); return true; }
      t.scrollBy({ top: (dir === "down" ? 1 : -1) * t.clientHeight * .7, behavior: "smooth" }); play("move");
      return true;
    },
    onKey(k) {   // LB / RB también leen; Start, X, Y no abren nada encima del contrato
      if (k === "lb" || k === "rb") { this.nav(k === "lb" ? "up" : "down"); return true; }
      if (["start", "x", "y", "lt", "rt"].includes(k)) return true;
      return this.busy && k !== "power";
    },
    onB() { if (!this.busy) actOn(this, "cancel"); return true; }
  };
  return v;
}

async function installFlow(g) {
  const v = installView(g); openView(v);
  const gone = () => !views.includes(v);
  let quit = false;
  const prep = (async () => {
    let r;
    for (let tries = 0; ; tries++) {   // Steam arrancando: se espera un poco
      r = await steamCall("install.prepare", { appid: g.appid }).catch(e => ({ mode: "steam", why: e.message }));
      if (quit || gone()) {   // cancelaste mientras Steam contestaba: si alcanzó a abrir su asistente, se cierra
        if (r && (r.mode === "cdp" || r.mode === "eula")) steamCall("install.cancel").catch(() => {});
        return null;
      }
      if (r && r.mode === "wait" && tries < 25) { v.set({ msg: "Esperando a Steam…" }); await wait(3000); if (quit || gone()) return null; continue; }
      return r;
    }
  })();
  // Cancelar (o B) funciona desde el primer momento: no espera a que Steam conteste.
  const first = await Promise.race([prep.then(r => ({ r })), nextAct(v).then(a => ({ a }))]);
  if (first.a !== undefined || !first.r) {
    quit = true; dropView(v);
    steamCall("install.cancel").catch(() => {});
    return;
  }
  let r = first.r;
  if (r.mode === "wait") r = { mode: "steam", why: "Steam no terminó de abrir" };
  if (r.mode !== "cdp") { dropView(v); return afterPrepare(g, r); }
  v.set({ phase: "config", size: r.size || 0, folders: r.folders || [], sel: r.folder >= 0 ? r.folder : ((r.folders || []).find(f => f.def) || (r.folders || [])[0] || { i: -1 }).i, canChange: r.canChange });
  for (;;) {
    const a = await nextAct(v);
    if (a === "cancel") { dropView(v); steamCall("install.cancel").catch(() => {}); return; }
    if (a && a.folder != null) {
      if (a.folder === v.sel) continue;
      v.set({ sel: a.folder }); play("select");
      const f = await steamCall("install.folder", { appid: g.appid, folder: a.folder }).catch(() => null);
      if (gone()) { steamCall("install.cancel").catch(() => {}); return; }
      if (f && f.ok) v.set({ size: f.size || v.size });
      continue;
    }
    if (a === "go") {
      if (v.short) { bump($('[data-b="go"]', v.el)); toast("No hay espacio suficiente en ese disco"); continue; }
      v.set({ phase: "busy" });
      const res = await steamCall("install.go", { appid: g.appid, folder: v.sel }).catch(e => ({ s: "steam", why: e.message }));
      dropView(v);
      return afterStep(g, res);
    }
  }
}

async function afterPrepare(g, r) {
  if (r.mode === "setup") return setupDirect(g, r);
  if (r.mode === "started") return began(g);
  if (r.mode === "eula") return eulaFlow(g, r.eulas || []);
  if (r.mode === "error") return alertDialog(`No se pudo instalar ${g.name}.\n${r.error || ""}`);
  return askSteamWindow(g);
}
/* Si Scarlet no pudo con el asistente de Steam, la ventana de Steam solo se abre si tú quieres. */
async function askSteamWindow(g) {
  const c = await choiceDialog(`Steam no contestó como Scarlet esperaba.\n¿Terminas de instalar ${g.name} en la ventana de Steam?`, [{ label: "Cancelar", value: null }, { label: "Abrir Steam", value: "steam" }], 0);
  if (c === "steam") installViaSteam(g);
}
async function afterStep(g, r) {
  const s = r && r.s;
  if (s === "started") return began(g);
  if (s === "eula") return eulaFlow(g, r.eulas || []);
  if (s === "canceled") return toast("Se canceló la instalación");
  if (s === "error") return alertDialog(`No se pudo instalar ${g.name}.\n${r.error || ""}`);
  return askSteamWindow(g);
}
async function eulaFlow(g, eulas) {
  const v = eulaView(g, eulas); openView(v);
  const ids = eulas.map(e => ({ id: e.id, version: e.version }));
  for (;;) {
    const a = await nextAct(v);
    if (a === "cancel") { dropView(v); steamCall("install.eula", { appid: g.appid, accept: false, eulas: ids }).catch(() => {}); return toast("Se canceló la instalación"); }
    if (a === "accept") {
      v.set({ busy: true });
      const r = await steamCall("install.eula", { appid: g.appid, accept: true, eulas: ids }).catch(e => ({ s: "steam", why: e.message }));
      dropView(v);
      return afterStep(g, r);
    }
  }
}
/* Ya empezó: pasa al primer lugar con su franja de descarga (Steam manda el progreso en vivo). */
function began(g) {
  play("select");
  if (!state.downloads[g.appid]) state.downloads[g.appid] = { p: 0, kind: "Descargando", st: "pending" };
  pinFirst(g); refreshLibrary();
  setTimeout(() => {   // si Steam nunca la empezó, se quita la franja
    const d = state.downloads[g.appid];
    if (d && d.st === "pending" && !g.installed) { delete state.downloads[g.appid]; unpin(g.appid); renderHome(); refreshLibrary(); toast(`Steam no empezó a descargar ${g.name}`); }
  }, 120000);
}

/* Primera vez: conectar Scarlet con Steam (un archivo en la carpeta de Steam y reiniciarlo una vez). */
async function setupDirect(g, r) {
  if (r.gameOpen) {
    const c = await choiceDialog(`Para instalar desde Scarlet, Steam tiene que reiniciarse una vez, y ahora tienes un juego abierto.\nCiérralo y vuelve a intentarlo, o instala con la ventanita de Steam.`, [{ label: "Cancelar", value: null }, { label: "Usar Steam", value: "steam" }], 1);
    if (c === "steam") installViaSteam(g);
    return;
  }
  const text = (r.restart ? "Steam tiene que reiniciarse una vez para terminar de conectarse con Scarlet." :
    "Para instalar desde Scarlet, sin que se abra Steam, Scarlet tiene que conectarse con Steam.\nSteam se reiniciará una sola vez (tarda unos segundos).") +
    (r.millennium ? "\nTienes Millennium instalado: puede bloquear esta conexión." : "");
  const c = await choiceDialog(text, [{ label: "Cancelar", value: null }, { label: "Usar Steam", value: "steam" }, { label: "Conectar", value: "go" }], 2);
  if (c === "steam") return installViaSteam(g);
  if (c !== "go") return;
  const ok = await connectSteam();
  if (ok === true) return installFlow(g);
  if (ok === false) installViaSteam(g);
}
function connectSteam() {
  return new Promise(async res => {
    const r = await Host.call("install.enable").catch(e => ({ ok: false, error: e.message }));
    if (!r || !r.ok) { await alertDialog((r && r.error) || "No se pudo conectar con Steam."); return res(false); }
    let done = false, t = 0, off = () => {};
    const v = {
      cls: "dialog", isInstall: true, i: 0, hints: [["B", "Seguir en segundo plano"]],
      render(el) { el.innerHTML = `<div class="dbox"><p><span class="iload" style="justify-content:center;padding:0"><i></i>Conectando con Steam…</span><span class="dsub">Steam se está reiniciando. Tarda entre 20 segundos y un minuto.</span></p></div>`; },
      items() { return []; },
      onKey(k) { return k !== "b" && k !== "power"; },
      onB() { if (!done) { done = true; clearTimeout(t); dropView(v); toast("Te aviso cuando Steam esté listo"); res("later"); setTimeout(off, 150000); } return true; }
    };
    openView(v);
    off = Events.on("install.enabled", d => {
      if (done) { toast(d && d.ok ? "Scarlet ya está conectado con Steam" : (d && d.error) || "No se pudo conectar con Steam"); off(); return; }
      done = true; off(); clearTimeout(t); dropView(v);
      if (d && d.ok) { toast("Scarlet ya está conectado con Steam"); res(true); } else { toast((d && d.error) || "No se pudo conectar con Steam"); res(false); }
    });
    t = setTimeout(() => { if (done) return; done = true; off(); dropView(v); toast("Steam tardó demasiado en reiniciarse"); res(false); }, 150000);
  });
}

setInterval(() => {   // simulación del progreso en el navegador
  if (Host.has) return;
  let done = [];
  for (const id in state.downloads) { const d = state.downloads[id]; if (d.st === "paused" || d.st === "queued") continue; d.st = "active"; d.p = Math.min(1, d.p + .018 + Math.random() * .02); if (d.p >= 1) done.push(id); }
  paintDownloads(); done.forEach(finishDownload);
}, 600);

