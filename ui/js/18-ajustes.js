/* ================= beta 1.0: Ajustes como en la Switch 2 =================
   Dos columnas: a la izquierda las categorías, a la derecha sus opciones en secciones. El foco empieza a la izquierda;
   A (o ▶) entra a las opciones, B (o ◀) regresa a las categorías y B otra vez cierra Ajustes. La ayuda de cada opción
   aparece debajo de la fila elegida. Los interruptores se cambian con A. */
const NF_FRIENDS = [["Todos", "all"], ["Mejores amigos", "best"], ["Nadie", "none"]];
const NF_GAME = [["Todos", "all"], ["Solo importantes", "important"], ["Ninguno", "none"]];
function settingsCats() {
  const H = Host.has;
  const sw = (label, desc, get, set) => ({ label, desc, sw: true, val: get, toggle() { set(!get()); } });
  const go = (label, desc, act, val) => ({ label, desc, go: true, val: val || (() => ""), act });
  const opt = (label, desc, opts, key) => ({ label, desc, val: () => optLabel(opts, settings[key]), act() { pickOption(label, opts, key); } });
  const theme = (label, value, swatch) => ({ label, radio: true, swatch, val: () => (settings.theme || "dark") === value, act() { settings.theme = value; applyTheme(); } });
  const cats = [
    { label: "Usuario y cuentas", secs: [
      ["Tu cuenta", [
        go("Perfil", "Tu nivel de Steam, horas jugadas, amigos y logros", () => openView(profileView()), () => profile.name || ""),
        ...(H ? [go("Cuentas y arranque", "Claves de Steam y SteamGridDB, inicio con Windows, botón Xbox, app de inicio", () => openView(accountsView()))] : [])]],
      ["Portadas", [
        { label: "SteamGridDB", desc: "API key para portadas (steamgriddb.com › Preferencias › API)", val: () => H ? "Gestionada por la app" : settings.sgdbKey ? "Configurada ✓" : "Sin configurar",
          act() {
            if (H) return Host.call("app.setup").catch(() => {});
            const k = prompt("Pega tu API key de SteamGridDB:", settings.sgdbKey || "");
            if (k === null) return;
            settings.sgdbKey = k.trim(); Art.clear(); renderHome(); toast(settings.sgdbKey ? "Buscando portadas en SteamGridDB…" : "API key eliminada");
          } }]]] },
    { label: "Mandos y vibración", secs: [
      ["Mandos", [go("Mandos", "Mandos conectados, orden de jugadores y emparejar por Bluetooth", () => openView(controlsView()))]],
      ["Vibración", [sw("Vibración", "Vibración sutil al moverte y seleccionar", () => !!settings.rumble, v => settings.rumble = v)]]] },
    { label: "Audio", secs: [
      ["Sonidos de Scarlet", [
        sw("Sonidos del menú", "Efectos al navegar por la consola", () => !!settings.sound, v => settings.sound = v),
        ...(H ? [{ label: "Paquete de sonidos", desc: "Sonidos de un paquete para Steam Deck (carpeta o .zip con pack.json) que tengas en Descargas, Escritorio o Documentos", val: () => soundPack || "Los de Scarlet", act() { soundPackMenu(this); } }] : [])]],
      ["Juegos y apps", [
        sw("Silenciar al volver al menú", "El juego o video que dejas atrás se calla; la música sigue sonando", () => settings.muteBehind !== false, v => { settings.muteBehind = v; Host.call("audio.mutebehind", { on: v }).catch(() => {}); }),
        ...(H ? [go("Restablecer sonido", "Quita el silencio a todas las apps (juegos, Scarlet, música) y sube las que quedaron en cero", () => Host.call("audio.reset").then(n => toast(n ? "Sonido restablecido" : "Todo ya tenía sonido")).catch(() => toast("No se pudo restablecer")))] : [])]],
      ["Dolby Atmos", [
        { label: "Aviso de Dolby Atmos", desc: "«Powered by Dolby Atmos» solo cuando Atmos está en uso (Ajustes de Windows › Sonido › Audio espacial)", val: () => settings.dolbyTag === false ? "Nunca" : "Cuando está en uso",
          act() { if (settings.dolbyTag === false) delete settings.dolbyTag; else settings.dolbyTag = false; paintDolby(); } },
        sw("Letras doradas", "El aviso de Dolby Atmos en dorado y blanco, con un brillo suave", () => settings.dolbyGold !== false, v => { settings.dolbyGold = v; paintDolby(); })]]] },
    { sep: true },
    { label: "Pantalla", secs: [
      ["Pantalla de la consola", [
        opt("Atenuar pantalla", "Oscurece la pantalla si no la usas. Cualquier botón la vuelve a encender.", DIM_OPTS, "dimAfter"),
        sw("Ocultar el puntero del mouse", "Como en una consola: no se ve en Scarlet ni en los juegos (sí en el Explorador y Ajustes de Windows)", () => settings.hideCursor !== false,
          v => { settings.hideCursor = v; document.documentElement.classList.toggle("showcursor", !v); Host.call("cursor.hide", { on: v }).catch(() => {}); }),
        { label: "Formato del reloj", desc: "Cómo se muestra la hora en la consola", val: () => settings.clock24 !== false ? "24 horas" : "12 horas (a.m./p.m.)", act() { settings.clock24 = settings.clock24 === false; tick(); } }]],
      ["Pantalla del juego", [
        { label: "Tamaño de los logos", desc: "El logo que sale al entrar o salir de un juego. Cada juego se puede ajustar aparte en sus opciones (X › Logo).",
          val: () => optLabel(LOGO_SIZES, settings.logoSize ?? 100), act() { if (settings.logoSize == null) settings.logoSize = 100; pickOption("Tamaño de los logos", LOGO_SIZES, "logoSize"); } }]],
      ...(H ? [] : [["Televisión", [sw("Probar: modo TV", "Simula conectar la consola a una pantalla externa", () => document.documentElement.classList.contains("tv"), v => setTvMode(v))]]])] },
    { label: "Temas", secs: [
      [null, [theme("Claro", "light", "#fff"), theme("Oscuro", "dark", "#000"), theme("Automático (como Windows)", "auto", "linear-gradient(90deg,#fff 50%,#000 50%)")]],
      ["Estilo", [
        sw("Lúmina", "Paneles de cristal translúcido y un cursor que fluye entre opciones. Funciona con el tema claro y el oscuro; apagado, todo queda como siempre.",
          () => settings.lumina === true, v => { settings.lumina = v; Lumina.apply(); }),
        ...(settings.lumina === true && Lumina.canRefract() ? [sw("Refracción del cursor", "El borde del cursor dobla lo que tiene debajo, como una lente. Apágala si notas que el menú se mueve con tirones.",
          () => settings.luminaRefract !== false, v => { settings.luminaRefract = v; Lumina.apply(); })] : [])]]] },
    { label: "Notificaciones", secs: [
      ["Avisos", [
        sw("Descargas completadas", "Avisa cuando termina de descargarse o actualizarse un juego", () => settings.nDl !== false, v => settings.nDl = v),
        sw("Logros", "Avisa cuando desbloqueas un logro", () => settings.nAch !== false, v => settings.nAch = v),
        sw("Batería baja", "Avisa al llegar al 20 % y al 10 %", () => settings.nBatt !== false, v => settings.nBatt = v)]],
      ["Amigos", [
        opt("Amigos que se conectan", "Marca a tus mejores amigos con ★ en su perfil (Perfil › Lista de amigos)", NF_FRIENDS, "nFriendOn"),
        opt("Amigos que empiezan a jugar", "Marca a tus mejores amigos con ★ en su perfil (Perfil › Lista de amigos)", NF_FRIENDS, "nFriendPlay")]],
      ["Durante el juego", [
        opt("Avisos encima del juego", "Solo importantes: batería baja, descargas y capturas. Lo que no veas te espera al volver al menú, en «Mientras jugabas».", NF_GAME, "nInGame")]],
      ["Logros", [
        { label: "Posición de los logros", desc: "Dónde salen los logros, en el menú y encima del juego", val: () => optLabel(ACH_POS, settings.achPos || "tl"),
          act() { if (!settings.achPos) settings.achPos = "tl"; pickOption("Posición de los logros", ACH_POS, "achPos"); } },
        ...["normal", "rare", "full"].map(k => ({ label: { normal: "Volumen: logro", rare: "Volumen: logro raro", full: "Volumen: juego al 100 %" }[k],
          desc: "Qué tan fuerte suena ese logro (aparte del volumen de la consola)", val: () => volLabel(settings["achVol_" + k] ?? ACH_VOL[k]),
          act() { if (settings["achVol_" + k] == null) settings["achVol_" + k] = ACH_VOL[k]; pickOption(this.label, ACH_VOLS, "achVol_" + k); } })),
        { label: "Sonidos de logros", desc: "Los de Scarlet o los que importaste (por ejemplo, los de Xbox). Los importados se quedan solo en tu consola.",
          val: () => settings.achSounds === "custom" ? "Personalizados" : "Scarlet", act() { settings.achSounds = settings.achSounds === "custom" ? "scarlet" : "custom"; if (settings.achSounds === "custom" && !achCustomCount()) toast("Primero importa tus sonidos de logros"); } },
        ...(H ? [go("Importar sonidos de logros", "Busca en Descargas, Escritorio y Documentos archivos llamados scarlet_logro_normal, scarlet_logro_raro y scarlet_logro_100 (.wav, .mp3, .ogg)", () => importAchSounds(), () => achCustomCount() ? `${achCustomCount()} importados` : "")] : []),
        go("Probar: logro", "Así se ve y se oye un logro", () => demoAchievement("normal")),
        go("Probar: logro raro", "Lo tiene menos del 10 % de los jugadores", () => demoAchievement("rare")),
        go("Probar: juego al 100 %", "Con el último logro que desbloqueaste", () => demoAchievement("full"))]],
      ["General", [sw("Sonido de notificación", "Un sonido corto cuando llega un aviso", () => settings.nSound !== false, v => settings.nSound = v)]]] },
    { label: "Energía", secs: [
      ["Ahorro de energía", [opt("Suspender automáticamente", "Suspende la consola tras un tiempo sin uso en el menú", SLEEP_OPTS, "sleepAfter")]],
      ["Arranque", [{ label: "Al encender la consola", desc: "Qué se abre cuando la consola arranca", val: () => settings.bootLast ? "El último juego" : "El Inicio", act() { settings.bootLast = !settings.bootLast; } }]]] },
    { sep: true },
    { label: "Red y VPN", secs: [
      ["VPN", [go("VPN", "Cloudflare WARP o WireGuard, para redes que bloquean Steam o Xbox", () => openView(vpnView()), () => VPN.provider === "none" ? "Sin configurar" : vpnLabel())]],
      ["Red", [go("Internet", "Redes Wi-Fi, conectarse, prueba de conexión y estado de la conexión", () => openView(wifiView()), () => WIFI.data && WIFI.data.current ? WIFI.data.current.ssid : ""),
        go("Wi-Fi y red en Windows", "Redes, contraseñas y datos de la conexión", () => openSystemApp(SYS.winsettings))]]] },
    { label: "Juegos externos", secs: [
      ["Agregar", [go("Agregar un juego", "Un .exe, un acceso directo (.lnk) o un .url de tu PC (por ejemplo un emulador o un juego de otra tienda). Se busca con un explorador para el mando.", () => addExternalGame())]],
      ...(games.some(g => g.source === "ext") ? [["Tus juegos", games.filter(g => g.source === "ext").map(g => go(g.name, g.path || "", () => openView(propsView(g)), () => g.installed === false ? "No está" : ""))]] : [])] },
    { label: "Gestión de datos", secs: [
      ["Almacenamiento", [go("Gestión de datos", "Espacio en disco y desinstalar juegos", () => openView(storageView()))]],
      ["Biblioteca", [go("Juegos ocultos", "Los que ocultaste no salen en ningún lado; aquí los vuelves a mostrar", () => openView(hiddenView()), () => { const n = games.filter(isHidden).length; return n ? String(n) : "Ninguno"; })]],
      ["Caché", [{ label: "Borrar caché de portadas", desc: "Vuelve a descargar las imágenes", val: () => "", act() { Art.clear(); renderHome(); toast("Caché de portadas borrada"); } }]]] },
    { label: "Sistema", secs: [
      ["Menú rápido", [sw("Fondo transparente", "El fondo del menú rápido al 50 % para seguir viendo el juego detrás. Los textos y las barras no cambian.", () => settings.quickGlass !== false, v => { settings.quickGlass = v; applyGlass(); }),
        sw("Menú rápido clásico", "El menú rápido de antes, con cuadritos. Úsalo si el nuevo te da algún problema.", () => !!settings.quickClassic, v => settings.quickClassic = v)]],
      ["Herramientas", [
        ...(H ? [go("Diagnóstico", "Lo que Scarlet detecta ahora: juego, apps con sonido, Dolby, música, Steam", () => openView(diagView()))] : []),
        go("Explorador de archivos", "Abre tus carpetas en pantalla completa", () => openSystemApp(SYS.explorer)),
        go("Ajustes de Windows", "Red, pantalla, cuentas y más", () => openSystemApp(SYS.winsettings))]],
      ...(H ? [] : [["Pruebas", [
        { label: "Probar: amigo conectado", desc: "Notificación de amigos", val: () => "", act() { demoFriendOnline(true); } },
        { label: "Probar: captura guardada", desc: "Notificación de captura con miniatura", val: () => "", act() { demoCapture(); } },
        { label: "Probar: batería baja", desc: "Aviso de batería", val: () => "", act() { notifyBattery(.15); } },
        { label: "Probar: mando conectado", desc: "Aviso de mando", val: () => "", act() { notify({ icon: I.pad, title: "Mando conectado", text: "Mando Xbox inalámbrico" }); } },
        { label: "Probar: actualización", desc: "Barra de descarga sobre un juego", val: () => "", act() { const g = games.find(x => x.installed && x.kind !== "app" && x.appid !== state.running && !state.downloads[x.appid]); if (g) { startDownload(g, "Actualizando"); toast("Actualizando " + g.name); } } }]]]),
      ["Scarlet", [
        go("Buscar actualizaciones", "Descarga e instala la versión más nueva de Scarlet desde GitHub", () => openView(updateView()), () => updState.available ? "Nueva: " + verName(updState.latest) : ""),
        ...(H ? [go("Salir al escritorio", "Cierra Scarlet", () => confirmDialog("¿Salir al escritorio de Windows?", "Salir").then(ok => { if (ok) Host.call("app.exit").catch(() => {}); }))] : []),
        { label: "Acerca de", desc: `Scarlet · ${verName(APP_VERSION)} · proyecto personal`, val: () => verName(APP_VERSION), act() {} }]]] }
  ];
  return cats;
}
function settingsView() {
  let cats = settingsCats(), real = cats.filter(c => !c.sep);
  const rowsOf = c => c.secs.flatMap(([, rows]) => rows);
  const rowHTML = (r, j) => {
    const v = r.val ? r.val() : "";
    const end = r.sw ? `<span class="nsw${v ? " on" : ""}"></span>` : r.radio ? `<span class="nrad${v ? " on" : ""}"></span>` : `<span class="nv${r.go && !v ? " go" : ""}">${r.go && !v ? "›" : esc(v)}</span>`;
    return `<button class="nsrow fi${r.radio && v ? " on" : ""}" data-r="${j}">${r.swatch ? `<span class="nswatch" style="background:${r.swatch}"></span>` : ""}<span class="nl">${esc(r.label)}</span>${end}</button>`;
  };
  return {
    isSettings: true, side: "L", ci: 0, ri: 0,
    onReturn() { this.refresh(); },   // al regresar de otra pantalla, los valores al día (p. ej. cuántos juegos ocultos)
    syncFromIndex() { if (this.side === "R") this.ri = this.i; },
    hints() {
      if (this.side === "L") return [["B", "Atrás"], ["A", "Aceptar"]];
      const r = this.rows()[this.ri] || {};
      return [["B", "Atrás"], ["A", r.sw ? "Cambiar" : r.radio ? "Elegir" : "Aceptar"]];
    },
    rows() { return rowsOf(real[this.ci]); },
    render(el) {
      el.innerHTML = head(I.gear, "Ajustes") + `<div class="nsb"><div class="nsl">${cats.map(c => c.sep ? "<hr>" : `<button class="nscat fi" data-c="${real.indexOf(c)}">${esc(c.label)}</button>`).join("")}</div><div class="nsr"></div></div>`;
      $$(".nscat", el).forEach(b => b.onclick = () => {
        const c = +b.dataset.c;
        if (this.side === "L" && c === this.ci) return this.enter();
        this.side = "L"; this.ci = c; this.i = c; this.paneRender(); updateFocus();
      });
      this.paneRender();
    },
    /* Pinta la columna derecha (la categoría elegida). */
    paneRender() {
      const pane = $(".nsr", this.el); let j = 0;
      $$(".nscat", this.el).forEach(b => b.classList.toggle("sel", +b.dataset.c === this.ci));
      pane.innerHTML = real[this.ci].secs.map(([t, rows]) => (t ? `<div class="nssec">${esc(t)}</div>` : `<div class="nssec blank"></div>`) + rows.map(r => rowHTML(r, j++)).join("")).join("");
      $$(".nsrow", pane).forEach(b => b.onclick = () => {
        const j2 = +b.dataset.r, r = this.rows()[j2];
        if (this.side !== "R" || this.ri !== j2) { this.side = "R"; this.ri = j2; this.i = j2; }
        this.run(r);
      });
      if (this.side === "L") pane.scrollTop = 0;
    },
    run(r) {
      if (!r) return;
      if (r.sw || r.radio) { r.toggle ? r.toggle() : r.act.call(r); saveSettings(); play(r.sw ? (r.val() ? "on" : "off") : "select"); this.refresh(); return; }
      const ret = r.act.call(r); saveSettings();
      Promise.resolve(ret).then(() => { if (this.el && this.el.isConnected) this.refresh(); });
    },
    enter() {
      if (!this.rows().length) return bump(this.items()[this.i], "right");
      this.side = "R"; this.ri = 0; this.i = 0; play("move"); updateFocus();
    },
    nav(dir) {
      if (this.side === "L") {
        if (dir === "right") return this.enter();
        if (dir === "left") return bump(this.items()[this.i], dir);
        const c = this.ci + (dir === "down" ? 1 : -1);
        if (c < 0 || c >= real.length) return bump(this.items()[this.i], dir);
        this.ci = c; this.i = c; this.paneRender(); play("move"); updateFocus(); return;
      }
      if (dir === "left") { this.side = "L"; this.i = this.ci; play("move"); return updateFocus(); }
      if (dir === "right") return bump(this.items()[this.i], dir);
      const n = this.rows().length, j = this.ri + (dir === "down" ? 1 : -1);
      if (j < 0 || j >= n) return bump(this.items()[this.i], dir);
      this.ri = j; this.i = j; play("move"); updateFocus();
    },
    onB() { if (this.side === "R") { this.side = "L"; this.i = this.ci; play("back"); updateFocus(); return true; } return false; },
    /* La ayuda va debajo de la opción elegida (como en la consola). */
    onFocus() {
      $$(".nshelp", this.el).forEach(h => h.remove());
      if (this.side !== "R") return;
      const r = this.rows()[this.ri], el = this.items()[this.ri];
      if (r && r.desc && el) { el.insertAdjacentHTML("afterend", `<p class="nshelp">${esc(r.desc)}</p>`); el.nextElementSibling.scrollIntoView({ block: "nearest" }); }
    },
    /* Vuelve a pintar los valores (después de cambiar algo aquí o desde otra pantalla). */
    refresh() {
      if (!this.el || !this.el.isConnected) return;
      const top = $(".nsr", this.el).scrollTop;
      // Las opciones pueden cambiar (un juego externo nuevo): se vuelven a armar.
      cats = settingsCats(); real = cats.filter(c => !c.sep); this.ri = Math.min(this.ri, Math.max(0, this.rows().length - 1)); if (this.side === "R") this.i = this.ri;
      this.paneRender(); $(".nsr", this.el).scrollTop = top;
      if (topView() === this) updateFocus();
    },
    /* Ir directo a una opción por su nombre (desde otros avisos y desde las pruebas). */
    goto(label) {
      for (let c = 0; c < real.length; c++) {
        const j = rowsOf(real[c]).findIndex(r => r.label === label);
        if (j >= 0) { this.ci = c; this.side = "R"; this.ri = j; this.i = j; this.paneRender(); updateFocus(); return true; }
      }
      return false;
    },
    items() { return this.el ? $$(this.side === "L" ? ".nscat" : ".nsrow", this.el) : []; }
  };
}

