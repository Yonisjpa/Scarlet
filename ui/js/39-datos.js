
/* ================= beta 1.5: juegos ocultos =================
   Ocultar quita un juego de todos lados (Inicio, Biblioteca, búsqueda, Logros). Aquí está la lista para regresarlos. */
function hiddenView() {
  return {
    hints() { return this.list && this.list.length ? [["B", "Atrás"], ["A", "Mostrar otra vez"]] : [["B", "Atrás"]]; },
    render(el) {
      this.list = games.filter(isHidden).sort((a, b) => a.name.localeCompare(b.name, "es"));
      el.innerHTML = head(I.all, "Juegos ocultos", this.list.length ? `${this.list.length}` : "") + `<div class="vbody">${this.list.length
        ? this.list.map((g, i) => `<button class="lrow fi" data-i="${i}"><img class="mini" alt="" data-art="${g.appid}"><span class="meta"><b>${esc(g.name)}</b><span>${g.kind === "app" ? "Aplicación" : g.installed ? "Instalado" : "No instalado"}</span></span><span class="val">Mostrar</span></button>`).join("")
        : `<p class="note">No tienes juegos ocultos. Para ocultar uno: X sobre el juego › Ocultar.</p>`}</div>`;
      $$("[data-art]", el).forEach(im => Art.apply(im, byId(im.dataset.art)));
      fis(el).forEach(b => b.onclick = () => this.show(this.list[+b.dataset.i]));
    },
    show(g) {
      if (!g) return;
      settings.hidden = (settings.hidden || []).filter(id => id !== g.appid); saveSettings(); play("select");
      toast(`${g.name} vuelve a aparecer`);
      renderHome(); const lv = views.find(v => v.cols === 6 && v.refresh); if (lv) lv.refresh(true);
      const k = this.i; this.render(this.el); this.i = Math.min(k, Math.max(0, this.items().length - 1)); updateFocus();
    },
    items() { return fis(this.el); }
  };
}
