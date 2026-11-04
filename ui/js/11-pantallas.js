/* ================= pantallas ================= */
function openView(v, origin) {
  v.i = v.i || 0;
  const el = document.createElement("section"); v.el = el;
  el.className = "view" + (v.cls ? " " + v.cls : "") + (origin && !v.cls ? " zoom" : "");
  if (origin && !v.cls) {
    const r = origin.getBoundingClientRect(), app = $("#app").getBoundingClientRect();
    el.style.transformOrigin = `${r.left + r.width / 2 - app.left}px ${r.top + r.height / 2 - app.top}px`;
  }
  el.addEventListener("click", e => {
    const it = e.target.closest(".fi"); if (!it) return;
    const idx = v.items().indexOf(it); if (idx >= 0 && idx !== v.i) { v.i = idx; if (v.syncFromIndex) v.syncFromIndex(); updateFocus(); }
  }, true);
  v.render(el);
  $("#app").append(el);
  views.push(v);
  requestAnimationFrame(() => requestAnimationFrame(() => el.classList.add("in")));
  play(v.cls === "dialog" ? "popup" : v.detail ? "detail" : "select"); updateFocus();
}
function closeView() {
  const v = views.pop(); v.el.classList.remove("in"); v.el.classList.add("out");
  setTimeout(() => v.el.remove(), 340);
  play(v.cls === "dialog" ? "popupclose" : "back");
  const t = topView(); if (t && t.onReturn && v.cls !== "dialog") t.onReturn();   // la pantalla de abajo se pone al día (beta 1.3)
  updateFocus();
}
function closeAllViews() { while (views.length) views.pop().el.remove(); updateFocus(); }
const head = (icon, title, sub = "") => `<div class="vhead">${icon}<span>${esc(title)}</span><span class="sub">${esc(sub)}</span></div>`;
const fis = el => [...el.querySelectorAll(".fi")];

/* Diálogo con varias opciones; devuelve el valor elegido (null si se cancela). */
function choiceDialog(text, options, focus = 0) {
  return new Promise(res => {
    openView({
      cls: "dialog", cols: options.length, i: focus, hints: [["B", "Cancelar"], ["A", "Aceptar"]],
      render(el) {
        el.innerHTML = `<div class="dbox"><p>${esc(text)}</p><div class="dbtns" style="grid-template-columns:repeat(${options.length},1fr)">${options.map((o, i) => `<button class="fi" data-i="${i}">${esc(o.label)}</button>`).join("")}</div></div>`;
        fis(el).forEach(b => b.onclick = () => { closeView(); res(options[+b.dataset.i].value); });
      },
      onB() { closeView(); res(null); return true; },
      items() { return fis(this.el); }
    });
  });
}
function confirmDialog(text, okLabel) {
  return new Promise(res => {
    openView({
      cls: "dialog", cols: 2, hints: [["B", "Cancelar"], ["A", "Aceptar"]],
      render(el) {
        el.innerHTML = `<div class="dbox"><p>${esc(text)}</p><div class="dbtns"><button class="fi" data-r="0">Cancelar</button><button class="fi" data-r="1">${esc(okLabel)}</button></div></div>`;
        fis(el).forEach(b => b.onclick = () => { closeView(); res(b.dataset.r === "1"); });
      },
      onBack() { res(false); },
      items() { return fis(this.el); }
    });
  });
}
function pickerView(title, icon, options, current, onPick) {
  return {
    i: Math.max(0, options.findIndex(o => o.value === current)), hints: [["B", "Atrás"], ["A", "Elegir"]],
    render(el) {
      el.innerHTML = head(icon, title) + `<div class="vbody">${options.map((o, i) => `<button class="lrow fi" data-i="${i}"><span class="meta"><b>${esc(o.label)}</b></span><span class="val">${o.value === current ? "✓" : esc(o.extra || "")}</span></button>`).join("")}</div>`;
      fis(el).forEach(b => b.onclick = () => { closeView(); onPick(options[+b.dataset.i].value); });
    },
    items() { return fis(this.el); }
  };
}

/* --- opciones del juego (X) --- */
function propsView(g) {
  const sections = () => {
    const s = [{ k: "info", label: "Información" }];
    if (hasAch(g)) s.push({ k: "ach", label: "Logros" });
    if (g.source === "ext") s.push({ k: "rename", label: "Cambiar nombre" }, { k: "extart", label: "Ícono y portada" }, { k: "args", label: "Parámetros de arranque" }, { k: "where", label: "Archivo del juego" });
    else s.push({ k: "art", label: "Portada" });
    if (g.kind !== "app") s.push({ k: "hero", label: "Fondo" }, { k: "logo", label: "Logo" });   // beta 1.5: la pantalla del juego
    if (!g.installed && g.source === "steam") s.push({ k: "install", label: "Instalar" });
    if (g.source === "steam" && g.kind !== "app") s.push({ k: "controls", label: "Controles (Steam Input)" });
    if (g.source === "steam" || (g.source === "xbox" && g.storeId)) s.push({ k: "store", label: "Ver en la tienda" });
    s.push({ k: "groups", label: "Grupos" });
    s.push({ k: "hide", label: isHidden(g) ? "Mostrar otra vez" : "Ocultar" });
    if (Host.has && g.kind !== "app") s.push({ k: "pause", label: "Pausar al ir al Inicio" });
    if (Host.has && g.kind !== "app") s.push({ k: "keepfocus", label: "Menú sin quitarle el foco" });
    if (Host.has && g.kind !== "app") s.push({ k: "compat", label: "Compatibilidad de pantalla" });
    if (state.running === g.appid) s.push({ k: "close", label: g.kind === "app" ? "Cerrar aplicación" : "Cerrar juego" });
    if (g.source === "ext" && state.running !== g.appid) s.push({ k: "remove", label: "Quitar de Scarlet" });
    return s;
  };
  return {
    detail: true,
    // beta 1.6: en «Logo», ◀ ▶ cambian el tamaño (de 10 en 10 %, de 50 a 150 %) y Y la posición
    onLR(i, dir) {
      if (!this.s[i] || this.s[i].k !== "logo") return play("bump");
      const s = (logoLook(g).s ?? 100) + (dir === "right" ? 10 : -10);
      if (s < 50 || s > 150) return bump(this.items()[i], dir);
      setLogoLook(g, { s }); play("tick"); this.lookChanged();
    },
    onKey(k) {
      if (k !== "y" || !this.s[this.i] || this.s[this.i].k !== "logo") return false;
      const ps = LOGO_POS.map(x => x[1]), p = ps[(ps.indexOf(logoLook(g).p || "bl") + 1) % ps.length];
      setLogoLook(g, { p }); play("select"); this.lookChanged(); return true;
    },
    lookChanged() {
      const p = $(".ppane", this.el), l = logoLook(g); if (!p) return;
      const st = $(".gpstage", p); if (st) applyLogoLook(st, g);
      const a = $(".glsz", p), b = $(".glpos", p); if (a) a.textContent = (l.s ?? 100) + " %"; if (b) b.textContent = optLabel(LOGO_POS, l.p || "bl");
    },
    hints() { const k = this.s && this.s[this.i] && this.s[this.i].k; if (k === "logo") return [["B", "Atrás"], ["◀▶", "Tamaño"], ["Y", "Posición"], ["A", "Elegir logo"]]; const a = { ach: "Ver todos", art: "Elegir portada", hero: "Elegir fondo", logo: "Elegir logo", store: "Abrir", close: "Cerrar", install: "Instalar", controls: "Configurar", groups: "Elegir", hide: isHidden(g) ? "Mostrar" : "Ocultar", pause: g.pause ? "Desactivar" : "Activar", compat: "Opciones", keepfocus: g.keepFocus ? "Desactivar" : "Activar", rename: "Cambiar", extart: "Elegir", args: "Cambiar", where: "Elegir otro", remove: "Quitar" }[k]; return [["B", "Atrás"], ...(a ? [["A", a]] : [])]; },
    render(el) {
      this.s = sections();
      el.innerHTML = head(I.gear, g.name, "Opciones") + `<div class="props"><div class="pmenu">${this.s.map((s, i) => `<button class="lrow fi" data-i="${i}"><span class="meta"><b>${esc(s.label)}</b></span></button>`).join("")}</div><div class="ppane"></div></div>`;
      fis(el).forEach(b => b.onclick = () => this.act(this.s[+b.dataset.i].k));
    },
    onFocus(i) { const k = this.s[i].k; if (k !== this.cur) { this.cur = k; this.pane(k); } },
    pane(k) {
      const p = $(".ppane", this.el), running = state.running === g.appid, d = state.downloads[g.appid];
      if (k === "info") {
        p.innerHTML = `<div class="pinfo"><img class="pcover" alt=""><div><h2>${esc(g.name)}</h2><dl class="kv">
          <dt>Origen</dt><dd>${esc(SOURCES[g.source] || "Steam")}</dd>
          <dt>Estado</dt><dd>${running ? "En ejecución" : d ? `${esc(d.kind)} ${Math.round(d.p * 100)} %` : g.installed ? "Instalado" : "No instalado"}</dd>
          <dt>Tiempo de uso</dt><dd>${(+g.hours || 0).toFixed(1)} h</dd>
          <dt>Última vez</dt><dd>${esc(g.last || "—")}</dd>
          ${hasAch(g) ? `<dt>Logros</dt><dd>${g.ach[0]} de ${g.ach[1]}</dd>` : ""}
          ${g.source === "steam" ? `<dt>ID de Steam</dt><dd>${g.appid}</dd>` : ""}</dl></div></div>`;
        Art.apply($(".pcover", p), g);
      } else if (k === "ach") {
        p.innerHTML = `<h2>Logros</h2><div class="prog"><div class="bar"><i style="width:0"></i></div><span>Cargando…</span></div><div class="alist"></div>`;
        Data.achievements(g).then(a => {
          if (this.cur !== "ach") return;
          $(".prog i", p).style.width = (a.total ? 100 * a.unlocked / a.total : 0) + "%";
          $(".prog span", p).textContent = `${a.unlocked} de ${a.total}`;
          $(".alist", p).innerHTML = a.list.slice(0, 7).map(x => `<div style="${x.unlocked ? "" : "opacity:.5"}">${achImg(x, "achi")}<span><b>${esc(x.name)}</b> · ${esc(x.desc)}</span></div>`).join("");
        });
      } else if (k === "art") {
        p.innerHTML = `<h2>Portada</h2><div class="artprev"><img alt=""><p class="note">Presiona A para ver todas las portadas disponibles en SteamGridDB y elegir la que más te guste. ${settings.sgdbKey || Host.has ? "" : "Sin API key solo se alternan los colores de la portada de respaldo."}</p></div>`;
        Art.apply($("img", p), g);
      } else if (k === "hero" || k === "logo") {
        const l = logoLook(g);
        p.innerHTML = `<h2>${k === "hero" ? "Fondo" : "Logo"}</h2><div class="gprev"><div class="gpstage"><img class="gpbg" alt=""><div class="gshade"></div><div class="ginfo"><span class="gplogo"></span></div></div></div>
          ${k === "logo" ? `<div class="glook"><span>Tamaño: <b class="glsz">${l.s ?? 100} %</b></span><span>Posición: <b class="glpos">${esc(optLabel(LOGO_POS, l.p || "bl"))}</b></span></div>` : ""}<p class="note">${k === "hero"
          ? "La imagen detrás del logo cuando entras o sales del juego. Presiona A para elegir entre los fondos de SteamGridDB, una imagen de tu PC o el arte original de Steam."
          : "◀ ▶ cambian el tamaño y Y la posición; la vista previa es igual a la pantalla del juego. A: elegir entre los logos de SteamGridDB o el de Steam, o dejar solo el nombre."}</p>`;
        fitPreview(p); paintGameScreen(p, g);
      } else if (k === "install") {
        p.innerHTML = `<h2>Instalar</h2><p class="note">Steam descargará el juego. Verás el progreso sobre su ícono en el menú.</p>`;
      } else if (k === "controls") {
        p.innerHTML = `<h2>Controles (Steam Input)</h2><p class="note">Abre la configuración de controles de Steam para este juego: remapear botones, sensibilidad de sticks, giroscopio y plantillas de la comunidad. Solo aplica mientras juegas con Steam; el esquema general de la consola se cambia en Armoury Crate.</p>`;
      } else if (k === "store") {
        p.innerHTML = `<h2>Ver en la tienda</h2><p class="note">${g.source === "xbox" ? "Abre la página del juego en la Microsoft Store (Xbox): actualizaciones, DLC y reseñas." : "Abre la página del juego en Steam: actualizaciones, DLC y reseñas."}</p>`;
      } else if (k === "groups") {
        const mine = groupNames().filter(n => inGroup(g, n));
        p.innerHTML = `<h2>Grupos</h2><p class="note">Ordena tu biblioteca en grupos propios (por ejemplo "Terminados" o "Por jugar"). Aparecen como pestañas en la Biblioteca.</p><p class="note">${mine.length ? "Está en: " + esc(mine.join(", ")) : "No está en ningún grupo."}</p>`;
      } else if (k === "hide") {
        p.innerHTML = isHidden(g) ? `<h2>Mostrar otra vez</h2><p class="note">Está oculto: no aparece en el Inicio, la Biblioteca, la búsqueda ni Logros. Presiona A para que vuelva.</p>` : `<h2>Ocultar</h2><p class="note">Deja de aparecer en todos lados: Inicio, Biblioteca, búsqueda y Logros. Para que vuelva: Ajustes › Gestión de datos › Juegos ocultos.</p>`;
      } else if (k === "pause") {
        p.innerHTML = `<h2>Pausar al ir al Inicio</h2><p class="note"><b>${g.pause ? "Activado" : "Desactivado"}.</b> Como en la consola: al regresar al Inicio, el juego se congela (no gasta batería ni suena) y sigue justo donde estaba al volver. Viene activado en todos los juegos; desactívalo en juegos en línea o con antitrampas, donde congelarlos puede desconectarte o cerrarlos.</p>`;
      } else if (k === "keepfocus") {
        p.innerHTML = `<h2>Menú sin quitarle el foco</h2><p class="note">${g.keepFocus ? "<b>Activado.</b> " : ""}El menú rápido se abre encima sin que el juego pierda el foco: sigue sonando y no se pausa (útil si el juego se silencia solo al abrir el menú). La contra: el juego también recibe tus botones mientras usas el menú.</p>`;
      } else if (k === "compat") {
        p.innerHTML = `<h2>Compatibilidad de pantalla</h2><p class="note">Para juegos sin «ventana sin bordes», donde el menú no se ve encima.<br><br>
          <b>Forzar ventana sin bordes: ${g.borderless ? "sí" : "no"}.</b> Pon el juego en «ventana» en sus opciones: Scarlet le quita los bordes y lo estira a toda la pantalla.<br>
          <b>Menú en la pantalla de Scarlet: ${g.fullMenu ? "sí" : "no"}.</b> Para juegos que solo tienen pantalla completa: el botón Xbox abre el menú en Scarlet (el juego se congela mientras tanto).<br><br>
          A: opciones (también revisa las de Windows).</p>`;
      } else if (k === "rename") {
        p.innerHTML = `<h2>Cambiar nombre</h2><p class="note">Ahora se llama <b>${esc(g.name)}</b>. Presiona A para escribir otro con el teclado. La portada de SteamGridDB se busca con el nombre nuevo.</p>`;
      } else if (k === "extart") {
        p.innerHTML = `<h2>Ícono y portada</h2><div class="artprev"><img alt=""${g.iconArt ? ' class="appicon"' : ""}><p class="note">Presiona A para elegir: una portada de SteamGridDB (se busca por el nombre), el ícono del programa o una imagen de tu PC.</p></div>`;
        Art.apply($("img", p), g);
      } else if (k === "args") {
        p.innerHTML = `<h2>Parámetros de arranque</h2><p class="note">Lo que se le pasa al abrirlo (por ejemplo <b>-fullscreen</b> o, en un emulador, la ruta del juego). ${g.args ? `Ahora: <b>${esc(g.args)}</b>` : "Ahora no tiene."}</p>`;
      } else if (k === "where") {
        p.innerHTML = `<h2>Archivo del juego</h2><p class="note" style="word-break:break-all">${esc(g.path || "—")}</p><p class="note">${g.installed === false ? "<b>Ya no está ahí.</b> " : ""}Presiona A para elegir otro archivo (si lo moviste de carpeta).</p>`;
      } else if (k === "remove") {
        p.innerHTML = `<h2>Quitar de Scarlet</h2><p class="note">Deja de aparecer en Scarlet. No se borra nada de tu PC: el juego y sus partidas se quedan donde están.</p>`;
      } else if (k === "close") {
        p.innerHTML = `<h2>${g.kind === "app" ? "Cerrar aplicación" : "Cerrar juego"}</h2><p class="note">Está abierto en segundo plano. Al cerrarlo se perderá el progreso que no hayas guardado.</p>`;
      }
    },
    act(k) {
      if (k === "ach") openView(achView(g));
      else if (k === "art") {
        if (!Host.has && !settings.sgdbKey) { Art.next(g); this.pane("art"); renderHome(); play("select"); }
        else openView(artPickerView(g, () => { if (this.cur === "art") this.pane("art"); }));
      }
      else if (k === "hero" || k === "logo") {
        if (!Host.has && !settings.sgdbKey) { play("bump"); return toast("Falta la API key de SteamGridDB (Ajustes › Cuentas y arranque)"); }
        const kk = k; openView(artPickerView(g, () => { if (this.cur === kk) this.pane(kk); }, k));
      }
      else if (k === "install") { closeView(); startInstall(g); }
      else if (k === "rename") openView(keyboardView(g.name, async name => { name = name.trim(); if (!name || name === g.name) return; if (await extUpdate(g, { name })) { toast("Ahora se llama " + name); this.render(this.el); this.cur = null; updateFocus(); } }, null, { title: "Nombre del juego", placeholder: "Nombre del juego…", max: 40, caps: true }));
      else if (k === "extart") extArtMenu(g, () => { if (this.cur === "extart") this.pane("extart"); });
      else if (k === "args") openView(keyboardView(g.args || "", async args => { if (await extUpdate(g, { args: args.trim() })) { toast(args.trim() ? "Parámetros guardados" : "Sin parámetros"); this.pane("args"); } }, null, { title: "Parámetros de arranque", placeholder: "-fullscreen", max: 120, caps: true, paste: true }));
      else if (k === "where") openView(fileBrowserView("Archivo del juego", false, async path => { if (await extUpdate(g, { path })) { g.installed = true; toast("Archivo cambiado"); this.pane("where"); } }));
      else if (k === "remove") extRemove(g).then(ok => { if (ok) closeView(); });
      else if (k === "groups") openView(groupsView(g, () => this.pane("groups")));
      else if (k === "hide") {
        const h = new Set(settings.hidden || []);
        if (h.has(g.appid)) h.delete(g.appid); else h.add(g.appid);
        settings.hidden = [...h]; saveSettings(); play("select");
        toast(isHidden(g) ? `${g.name} se ocultó` : `${g.name} vuelve a aparecer`);
        renderHome(); const lv = views.find(v => v.cols === 6 && v.refresh); if (lv) lv.refresh(true);
        this.render(this.el); updateFocus();
      }
      else if (k === "compat") { compatMenu(g).then(() => { this.pane("compat"); updateFocus(); }); }
      else if (k === "keepfocus") {
        g.keepFocus = !g.keepFocus; play("select");
        Host.call("game.menufocus", { appid: g.appid, on: g.keepFocus }).catch(() => {});
        toast(g.keepFocus ? "El menú ya no le quitará el foco" : "El menú vuelve a tomar el foco");
        this.pane("keepfocus"); updateFocus();
      }
      else if (k === "pause") {
        g.pause = !g.pause; play("select");
        Host.call("game.pausepref", { appid: g.appid, on: g.pause }).catch(() => {});
        toast(g.pause ? "Se pausará al ir al Inicio" : "Ya no se pausará");
        this.pane("pause"); updateFocus();
      }
      else if (k === "store" && g.source === "xbox") { closeView(); Host.call("system.open", { uri: "ms-windows-store://pdp/?ProductId=" + g.storeId }).catch(() => {}); }
      else if (k === "store") { closeView(); openSystemApp({ ...SYS.store, name: g.name + " · Tienda", url: "steam://store/" + g.appid }); }
      else if (k === "controls") { closeView(); openSystemApp({ ...SYS.store, name: "Controles · " + g.name, sub: "Configuración de Steam Input en Big Picture", url: "steam://controllerconfig/" + g.appid }); }
      else if (k === "close") confirmDialog(`¿Cerrar ${g.name}?\nSe perderá el progreso que no hayas guardado.`, "Cerrar").then(ok => { if (ok) { closeRunning(); closeView(); } });
      else bump(this.items()[this.i]);
    },
    onX() { closeView(); return true; },
    items() { return fis(this.el); }
  };
}

/* --- Dolby Atmos: un renglón discreto al jugar, en la pantalla de carga y en el menú rápido con música --- */
let DOLBY = false;
const DOLBY_HTML = '<span>Powered by</span><img class="dlogo" alt="Dolby Atmos" src="__DOLBY__">';
/* Solo cuando Dolby Atmos está de verdad en uso: la salida de audio lo tiene activo y algo está sonando. */
let ATMOS = false;
const dolbyOn = () => settings.dolbyTag !== false && ATMOS;
function paintDolby() { const l = $("#launch .ldolby"); if (l) { l.innerHTML = dolbyHTML(); l.classList.toggle("gold", dolbyGold()); l.classList.toggle("on", dolbyOn()); } if (QM.open) renderQuick(); }
function checkAtmos() { if (!Host.has) return; Host.call("audio.atmos").then(a => { ATMOS = !!a; paintDolby(); }).catch(() => {}); }

/* Dolby dorado (beta 1.0) */
document.documentElement.style.setProperty("--dlogo", `url("${DOLBY_HTML.match(/src="([^"]+)"/)[1]}")`);
const dolbyGold = () => settings.dolbyGold !== false;
const dolbyHTML = () => dolbyGold() ? '<span class="dgw"><span class="dgt">Powered by</span><i class="glg" role="img" aria-label="Dolby Atmos"></i></span>' : DOLBY_HTML;
/* --- diagnóstico: para mandar una foto cuando algo no cuadra --- */
function diagView() {
  return {
    hints: [["B", "Atrás"], ["▲▼", "Desplazar"], ["A", "Actualizar"]],
    render(el) { el.innerHTML = head(I.gear, "Diagnóstico", verName(APP_VERSION)) + `<div class="vbody"><dl class="kv diag"><dt>Revisando…</dt></dl></div>`; this.load(); },
    load() {
      Host.call("diag").then(d => {
        const dl = $(".diag", this.el); if (!dl || !d) return;
        dl.innerHTML = Object.entries(d).map(([k, v]) => `<dt>${esc(k)}</dt><dd>${esc(String(v))}</dd>`).join("") +
          `<dt>Música (Scarlet)</dt><dd>${media ? esc(`${media.title} · ${media.playing ? "sonando" : "en pausa"} · ${media.art ? "con carátula" : "sin carátula"}`) : "nada"}</dd>`;
      }).catch(() => {});
    },
    onKey(k) {
      if (k === "a") { play("select"); this.load(); return true; }
      if (k === "up" || k === "down") { const b = $(".vbody", this.el); if (b) b.scrollBy({ top: k === "down" ? 220 : -220, behavior: "smooth" }); return true; }   // la lista de salidas es larga
      return false;
    },
    items() { return []; }
  };
}

