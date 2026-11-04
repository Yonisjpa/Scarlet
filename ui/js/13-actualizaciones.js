/* --- actualizaciones desde GitHub --- */
let APP_VERSION = "1.6";
const verName = v => (/^0\./.test(String(v)) ? "alpha " : "beta ") + v;
const updState = { available: false, latest: null };
function updateView() {
  return {
    hints() { return this.info && this.info.available && !this.busy ? [["B", "Atrás"], ["A", "Descargar e instalar"]] : [["B", "Atrás"]]; },
    render(el) {
      el.innerHTML = head(I.gear, "Actualizaciones") + `<div class="vbody"><div class="upd"><p class="note">Buscando la versión más nueva…</p></div></div>`;
      if (!Host.has) { $(".upd", el).innerHTML = `<p class="note">En la consola aquí se revisa GitHub y se instala la versión nueva.</p>`; return; }
      Host.call("update.check").then(r => { this.info = r || {}; this.paint(); }).catch(() => { this.info = { error: "No se pudo revisar." }; this.paint(); });
    },
    paint() {
      const r = this.info, box = $(".upd", this.el); if (!box) return;
      if (r.available) { updState.available = true; updState.latest = r.latest; }
      box.innerHTML = `<dl class="kv"><dt>Tienes</dt><dd>${esc(verName(r.current || APP_VERSION))}</dd>${r.latest ? `<dt>Publicada</dt><dd>${esc(verName(r.latest))}${r.sizeMb ? ` · ${r.sizeMb} MB` : ""}</dd>` : ""}</dl>
        ${r.error ? `<p class="note">${esc(r.error)}</p>` : r.available ? `<h2 style="margin-top:2rem">${esc(r.name || "Versión nueva")}</h2><p class="note" style="white-space:pre-wrap">${esc((r.notes || "").replace(/[#*`]/g, "").trim())}</p>
        <p class="note">Presiona A para descargarla. Scarlet se cerrará unos segundos y volverá a abrir ya actualizada. Tu configuración y portadas se quedan igual. Si la versión nueva fallara al abrir, se regresa sola a la que tienes ahora.</p>
        <div class="prog" style="display:none"><div class="bar"><i style="width:0"></i></div><span>0 %</span></div>` : `<p class="note">Ya tienes la versión más nueva.</p>`}`;
      updateFocus();
    },
    onKey(k) {
      if (k !== "a") return false;
      if (!this.info || !this.info.available || this.busy) { play("bump"); return true; }
      this.busy = true; play("select"); updateFocus();
      const pg = $(".prog", this.el); pg.style.display = "";
      const off = Events.on("update.progress", d => { $(".prog i", this.el).style.width = d.p * 100 + "%"; $(".prog span", this.el).textContent = d.p >= 1 ? "Instalando…" : Math.round(d.p * 100) + " %"; });
      Host.call("update.install").then(r => {
        if (r && r.ok) { $(".prog span", this.el).textContent = "Instalando… Scarlet se reinicia"; return; }
        off(); this.busy = false; toast((r && r.error) || "No se pudo actualizar"); this.paint();
      }).catch(() => { off(); this.busy = false; toast("No se pudo actualizar"); this.paint(); });
      return true;
    },
    onB() { return this.busy; },
    items() { return []; }
  };
}

