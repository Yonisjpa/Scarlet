/* ================= beta 1.0: opciones de energía =================
   Hibernar, Reiniciar o Apagar. Si no eliges nada en 10 s, se hiberna sola (cualquier botón detiene la cuenta). */
function powerMenu() {
  if (mode !== "ui" || (topView() && topView().isPower)) return;
  const opts = [
    { k: "hibernate", icon: NQ.moon.replace(' class="qi"', ""), label: "Hibernar", sub: "Guarda todo lo abierto; al prender sigues donde te quedaste" },
    { k: "restart", icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 12a8 8 0 1 1-2.6-5.9"/><path d="M20 4v5h-5"/></svg>', label: "Reiniciar", sub: "Cierra todo y vuelve a arrancar la consola" },
    { k: "shutdown", icon: QI.power.replace(' class="qi"', ""), label: "Apagar", sub: "" }];
  const SECS = 10;
  const v = {
    cls: "dialog", isPower: true, i: 0, left: SECS, hints: [["B", "Cancelar"], ["A", "Aceptar"]],
    render(el) {
      el.innerHTML = `<div class="dbox pwbox"><h3>Opciones de energía</h3>${opts.map((o, i) => `<button class="pwrow fi" data-i="${i}">${o.icon}<span><b>${o.label}</b>${o.sub ? `<span>${o.sub}</span>` : ""}</span></button>`).join("")}
        <div class="pwcount"><span></span><i><u></u></i></div></div>`;
      fis(el).forEach(b => b.onclick = () => this.pick(opts[+b.dataset.i].k));
      this.paint(); this.t = setInterval(() => { if (!el.isConnected) return clearInterval(this.t); this.left--; this.paint(); if (this.left <= 0) { clearInterval(this.t); this.pick("hibernate"); } }, 1000);
    },
    paint() {
      const c = $(".pwcount", this.el); if (!c) return;
      if (this.left == null) { c.style.visibility = "hidden"; return; }
      $("span", c).textContent = `Se hibernará sola en ${this.left} s si no eliges nada`;
      $("u", c).style.width = (this.left / SECS * 100) + "%";
    },
    stop() { if (this.left != null) { clearInterval(this.t); this.left = null; this.paint(); } },
    onKey(k) { this.stop(); return false; },
    onB() { clearInterval(this.t); closeView(); return true; },
    pick(k) {
      clearInterval(this.t); if (topView() === this) closeView();
      if (k === "hibernate") return setTimeout(() => sleepConsole("hibernate"), 300);
      if (k === "restart") { if (Host.has) Host.call("system.restart").catch(() => {}); else toast("Prototipo: aquí se reiniciaría el equipo"); return; }
      if (Host.has) Host.call("system.shutdown").catch(() => {}); else toast("Prototipo: aquí se apagaría el equipo");
    },
    items() { return fis(this.el); }
  };
  openView(v);
}
Events.on("ui.hibernate", () => { setTimeout(() => sleepConsole("hibernate"), 200); });

