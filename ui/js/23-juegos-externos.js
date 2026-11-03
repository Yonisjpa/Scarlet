/* ================= beta 1.1: juegos externos =================
   Un .exe, un acceso directo (.lnk) o un .url (emuladores) agregado desde Ajustes. Se abre y se sigue como cualquier juego
   (pantalla de carga, «Jugando», menú rápido, horas que cuenta Scarlet) y en sus opciones se le cambia el nombre, la portada
   o el ícono, los parámetros de arranque, el archivo, o se quita de Scarlet (sin borrar nada del disco). */
const FB_IC = {
  dir: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><path d="M3 6.5A1.5 1.5 0 0 1 4.5 5h4.2l2 2.2h8.8A1.5 1.5 0 0 1 21 8.7v9.8a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18.5z"/></svg>',
  drive: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><rect x="3" y="7" width="18" height="10" rx="2"/><circle cx="17" cy="12" r="1.2" fill="currentColor"/></svg>',
  file: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><rect x="4" y="4" width="16" height="16" rx="3"/><path d="M10 9.5v5l4.2-2.5z" fill="currentColor"/></svg>',
  image: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round"><rect x="3.5" y="5" width="17" height="14" rx="2"/><circle cx="9" cy="10" r="1.6"/><path d="m4 17 5-5 4 4 2.5-2.5L20 18"/></svg>',
  up: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 19V6M6 11l6-6 6 6"/></svg>'
};
/* Explorador de archivos para el mando: A entra a la carpeta o elige el archivo, B sube una carpeta (en el inicio, cierra). */
function fileBrowserView(title, images, onPick) {
  return {
    path: "", entries: [], parent: null,
    hints() { const e = this.entries[this.i]; return [["B", this.path ? "Subir" : "Cerrar"], ["A", !e ? "Aceptar" : e.kind === "file" || e.kind === "image" ? "Elegir" : "Abrir"]]; },
    render(el) { el.innerHTML = head(images ? FB_IC.image : I.gear, title, "") + `<div class="vbody fbody"></div>`; this.go(""); },
    async go(path, focusName) {
      const sub = $(".vhead .sub", this.el); sub.textContent = "Cargando…";
      const r = Host.has ? await Host.call("fs.list", { path, images: !!images }).catch(e => ({ error: e.message, entries: [] })) : fakeFs(path, images);
      if (!this.el.isConnected) return;
      this.path = r.path || ""; this.parent = r.parent; this.entries = r.entries || [];
      sub.textContent = this.path || "Elige dónde buscar";
      const body = $(".fbody", this.el);
      body.innerHTML = (r.error ? `<p class="note">${esc(r.error)}</p>` : "") + (this.entries.length ? this.entries.map((e, i) =>
        `<button class="lrow fi fbrow" data-i="${i}"><span class="ico">${FB_IC[e.kind === "place" ? "dir" : e.kind] || FB_IC.dir}</span><span class="meta"><b>${esc(e.name)}</b>${e.kind === "file" ? `<span>${esc(e.name.split(".").pop().toUpperCase())}${e.size ? " · " + (e.size >= 1073741824 ? (e.size / 1073741824).toFixed(1) + " GB" : e.size >= 1048576 ? Math.round(e.size / 1048576) + " MB" : Math.max(1, Math.round(e.size / 1024)) + " KB") : ""}</span>` : ""}</span><span class="val">${e.kind === "file" || e.kind === "image" ? "" : "›"}</span></button>`).join("")
        : `<p class="note">${images ? "Aquí no hay imágenes." : "Aquí no hay juegos ni accesos directos (.exe, .lnk, .url)."} Presiona B para regresar.</p>`);
      fis(body).forEach(b => b.onclick = () => this.open(this.entries[+b.dataset.i]));
      this.i = Math.max(0, focusName ? this.entries.findIndex(e => e.name === focusName) : 0); updateFocus();
    },
    open(e) {
      if (!e) return;
      if (e.kind === "file" || e.kind === "image") { play("select"); closeView(); return onPick(e.path); }
      play("select"); this.go(e.path);
    },
    onB() {
      if (!this.path) return false;
      const here = this.path.replace(/[\\/]+$/, "").split(/[\\/]/).pop();
      play("back"); this.go(this.parent || "", here); return true;
    },
    items() { return fis(this.el); }
  };
}
/* Sin la app (prototipo): unas carpetas de ejemplo. */
function fakeFs(path, images) {
  const tree = { "": [{ name: "Escritorio", path: "C:\\Users\\Jhon\\Desktop", kind: "place" }, { name: "Descargas", path: "C:\\Users\\Jhon\\Downloads", kind: "place" }, { name: "Disco (C:)", path: "C:\\", kind: "drive" }],
    "C:\\Users\\Jhon\\Desktop": [{ name: "Emuladores", path: "C:\\Users\\Jhon\\Desktop\\Emuladores", kind: "dir" }, images ? { name: "portada.png", path: "C:\\Users\\Jhon\\Desktop\\portada.png", kind: "image" } : { name: "Hollow Knight.lnk", path: "C:\\Users\\Jhon\\Desktop\\Hollow Knight.lnk", kind: "file", size: 1200 }],
    "C:\\Users\\Jhon\\Desktop\\Emuladores": [{ name: "Ryujinx.exe", path: "C:\\Users\\Jhon\\Desktop\\Emuladores\\Ryujinx.exe", kind: "file", size: 48e6 }],
    "C:\\Users\\Jhon\\Downloads": [], "C:\\": [{ name: "Games", path: "C:\\Games", kind: "dir" }], "C:\\Games": [] };
  const parent = path ? (path.replace(/\\[^\\]+\\?$/, "") || "") : null;
  return { path, parent: path && tree[parent] ? parent : path ? "" : null, entries: tree[path] || [] };
}
async function addExternalGame() {
  openView(fileBrowserView("Agregar un juego", false, async path => {
    const r = Host.has ? await Host.call("ext.add", { path }).catch(e => ({ ok: false, error: e.message })) : fakeExtAdd(path);
    if (!r || !r.ok) return alertDialog((r && r.error) || "No se pudo agregar");
    if (Host.has) await reloadLibrary(); else renderHome();
    toast(`${r.name} se agregó a tu biblioteca`);
    const g = byId(r.appid); if (g) openView(propsView(g));
    const sv = views.find(v => v.isSettings); if (sv) sv.refresh();
  }));
}
function fakeExtAdd(path) {
  const name = path.split("\\").pop().replace(/\.(exe|lnk|url)$/i, "");
  fakeExtAdd.last = Math.max(fakeExtAdd.last || 2000000000, ...games.filter(g => g.source === "ext").map(g => g.appid));
  const id = ++fakeExtAdd.last;
  games.push({ appid: id, name, source: "ext", kind: "game", installed: true, hours: 0, last: "nunca", lastTs: 0, ach: [0, 0], cols: ["Externos"], size: 0, path, args: "" });
  return { ok: true, appid: id, name };
}
async function extUpdate(g, patch) {
  const r = Host.has ? await Host.call("ext.update", { appid: g.appid, ...patch }).catch(e => ({ ok: false, error: e.message })) : { ok: true };
  if (!r || !r.ok) { alertDialog((r && r.error) || "No se pudo guardar"); return false; }
  Object.assign(g, patch); renderHome(); refreshLibrary();
  return true;
}
/* Portada o ícono de un juego externo: SteamGridDB (la de siempre), el ícono del programa o una imagen tuya. */
async function extArtMenu(g, after) {
  const v = await choiceDialog(`Ícono y portada\n${g.name}`, [{ label: "Atrás", value: null }, { label: "SteamGridDB", value: "sgdb" }, { label: "Ícono del programa", value: "icon" }, { label: "Imagen de tu PC", value: "file" }], 1);
  if (!v) return;
  if (v === "sgdb") {
    if (g.art) await extSetArt(g, "clear");
    if (!Host.has && !settings.sgdbKey) { Art.next(g); renderHome(); return after && after(); }
    return openView(artPickerView(g, after));
  }
  if (v === "icon") return extSetArt(g, "icon").then(after);
  openView(fileBrowserView("Elegir una imagen", true, path => extSetArt(g, "file", path).then(after)));
}
async function extSetArt(g, mode, path) {
  const r = Host.has ? await Host.call("ext.art", { appid: g.appid, mode, path }).catch(e => ({ ok: false, error: e.message })) : { ok: true, url: mode === "clear" ? null : Art.placeholder(g), icon: mode === "icon" };
  if (!r || !r.ok) return alertDialog((r && r.error) || "No se pudo cambiar");
  g.art = r.url || null; g.iconArt = !!r.icon;
  delete Art.pick[g.appid]; store.set("artPick", Art.pick);
  for (const k of Object.keys(Art.cache)) if (k.includes(":" + g.appid + ":")) delete Art.cache[k];
  store.set("artCache", Art.cache);
  renderHome(); refreshLibrary(); play("select");
  toast(mode === "clear" ? "Portada de SteamGridDB" : mode === "icon" ? "Ahora usa el ícono del programa" : "Portada cambiada");
}
function extRemove(g) {
  return confirmDialog(`¿Quitar ${g.name} de Scarlet?\nNo se borra nada de tu PC: solo deja de aparecer aquí.`, "Quitar").then(async ok => {
    if (!ok) return false;
    if (Host.has) await Host.call("ext.remove", { appid: g.appid }).catch(() => {});
    // Su portada elegida y la de SteamGridDB se borran: otro juego no debe heredarlas.
    delete Art.pick[g.appid]; store.set("artPick", Art.pick);
    for (const k of Object.keys(Art.cache)) if (k.includes(":" + g.appid + ":")) delete Art.cache[k];
    store.set("artCache", Art.cache);
    games = games.filter(x => x !== g); renderHome(); refreshLibrary();
    toast(`${g.name} se quitó de Scarlet`);
    const sv = views.find(v => v.isSettings); if (sv) sv.refresh();
    return true;
  });
}

