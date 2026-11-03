/* ================= puente con la app anfitriona ================= */
const Events = {
  h: {},
  on(n, f) { (this.h[n] = this.h[n] || []).push(f); return () => { this.h[n] = this.h[n].filter(x => x !== f); }; },
  emit(n, d) { (this.h[n] || []).slice().forEach(f => f(d)); }
};
const Host = {
  has: !!(window.chrome && window.chrome.webview), seq: 0, pending: new Map(),
  init() {
    if (!this.has) return;
    window.chrome.webview.addEventListener("message", e => {
      const m = e.data; if (!m) return;
      if (m.event) return Events.emit(m.event, m.data);
      const p = this.pending.get(m.id); if (!p) return;
      this.pending.delete(m.id); m.ok ? p.res(m.data) : p.rej(new Error(m.error || "host"));
    });
  },
  call(type, payload = {}) {
    if (!this.has) return Promise.reject(new Error("sin anfitrión"));
    const id = ++this.seq;
    return new Promise((res, rej) => {
      this.pending.set(id, { res, rej });
      window.chrome.webview.postMessage({ id, type, payload });
      setTimeout(() => { if (this.pending.delete(id)) rej(new Error("timeout")); }, 45000);
    });
  }
};
Host.init();
Events.on("sounds.changed", m => Snd.loadCustom(m || {}, true));   // se importó o quitó un paquete de sonidos

