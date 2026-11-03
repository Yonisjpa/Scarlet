/* ================= v2.9.8: gestión de datos ================= */
const SIZES = { 3489700: 52.4, 1245620: 61.8, 1145350: 12.9, 2379780: 0.2, 900101: 34.6, 1086940: 128.5, 1091500: 72.3, 367520: 9.1, 900102: 74.9, 2246340: 139.2, 504230: 1.2, 1551360: 116.4, 1794680: 0.9, 413150: 0.7, 1817070: 74.1, 2050650: 67.8, 900103: 0.1, 900104: 0.2, 900105: 0.4, 900106: 0.5 };
const gameSize = g => g.size || SIZES[g.appid] || (g.kind === "app" ? .3 : 8 + seeded(g.name, 60));
const fmtSize = v => v < 1 ? Math.round(v * 1024) + " MB" : v.toFixed(1) + " GB";
const DISK = { total: 1000, system: 64 };
function storageView() {
  return {
    hints: [["B", "Atrás"], ["A", "Opciones"]],
    render(el) {
      el.innerHTML = head(I.gear, "Gestión de datos") + `<div class="vbody"></div>`;
      if (Host.has) Host.call("storage.info").then(d => { this.disk = d; if (el.isConnected) this.refresh(); }).catch(() => {});
      this.refresh();
    },
    refresh() {
      const inst = games.filter(g => g.installed).sort((a, b) => gameSize(b) - gameSize(a));
      const used = inst.reduce((s, g) => s + gameSize(g), 0);
      const D = this.disk ? { total: Math.round(this.disk.total), system: Math.max(0, Math.round(this.disk.total - this.disk.free - used)) } : DISK;
      const free = this.disk ? this.disk.free : Math.max(0, D.total - D.system - used);
      const DISK_ = D, pct = v => (100 * v / DISK_.total).toFixed(2) + "%";
      $(".vbody", this.el).innerHTML = `<div class="stor"><h3>Almacenamiento interno <span>${free.toFixed(0)} GB libres de ${DISK_.total} GB</span></h3>
        <div class="sbar"><i style="width:${pct(DISK_.system)};background:var(--fg2)"></i><i style="width:${pct(used)};background:var(--accent)"></i></div>
        <div class="slegend"><span><i style="background:var(--fg2)"></i>${this.disk ? "Sistema y otros" : "Sistema"} · ${DISK_.system} GB</span><span><i style="background:var(--accent)"></i>Juegos y apps · ${used.toFixed(0)} GB</span><span><i style="background:var(--line)"></i>Libre · ${free.toFixed(0)} GB</span></div></div>
        <div class="sect">Instalados · de mayor a menor</div>` +
        inst.map(g => `<button class="lrow fi" data-g="${g.appid}"><img class="mini" alt="" data-art="${g.appid}"><span class="meta"><b>${esc(g.name)}</b><span>${esc(SOURCES[g.source] || "Steam")}${g.appid === state.running ? " · en ejecución" : uninstalling.has(g.appid) ? " · desinstalando…" : ""}</span></span><span class="val">${fmtSize(gameSize(g))}</span></button>`).join("");
      $$("[data-art]", this.el).forEach(im => Art.apply(im, byId(im.dataset.art)));
      $$("[data-g]", this.el).forEach(b => b.onclick = () => this.options(byId(b.dataset.g)));
      updateFocus();
    },
    options(g) {
      openView(pickerView(g.name, I.gear, [{ label: g.kind === "app" ? "Desinstalar aplicación" : "Desinstalar juego", value: "rm" }, { label: "Abrir carpeta de instalación", value: "dir" }], null, v => {
        if (v === "dir") return openSystemApp({ ...SYS.explorer, name: g.name + " · carpeta" });
        if (g.appid === state.running) return toast("Cierra el juego antes de desinstalarlo");
        if (uninstalling.has(g.appid)) return toast(`Ya se está desinstalando ${g.name}…`);
        confirmDialog(`¿Desinstalar ${g.name}?\nSe liberarán ${fmtSize(gameSize(g))}. Tus partidas en la nube no se borran.`, "Desinstalar").then(ok => {
          if (ok) uninstallGame(g, () => this.refresh());
        });
      }));
    },
    items() { return fis(this.el); }
  };
}

