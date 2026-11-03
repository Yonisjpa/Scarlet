/* ================= sonidos: clics percusivos sintetizados ================= */
const Snd = {
  ctx: null, noise: null, bus: null,
  ensure() {
    if (!this.ctx) {
      const C = window.AudioContext || window.webkitAudioContext; if (!C) return null;
      const c = this.ctx = new C();
      const comp = c.createDynamicsCompressor(); comp.threshold.value = -14; comp.ratio.value = 3;
      this.bus = c.createGain(); this.bus.gain.value = .9; this.bus.connect(comp).connect(c.destination);
      const n = c.sampleRate * .6, b = c.createBuffer(1, n, c.sampleRate), d = b.getChannelData(0);
      for (let i = 0; i < n; i++) d[i] = Math.random() * 2 - 1;
      this.noise = b;
    }
    if (this.ctx.state === "suspended") this.ctx.resume();
    return this.ctx;
  },
  env(g, at, v, dur, atk = .0015) {
    g.gain.setValueAtTime(.0001, at); g.gain.exponentialRampToValueAtTime(v, at + atk); g.gain.exponentialRampToValueAtTime(.0001, at + dur);
  },
  // ruido filtrado muy corto: el "tac" de plástico/madera
  click(t, freq, q, dur, v) {
    const c = this.ensure(); if (!c) return; const at = c.currentTime + t;
    const s = c.createBufferSource(), f = c.createBiquadFilter(), g = c.createGain();
    s.buffer = this.noise; f.type = "bandpass"; f.frequency.value = freq; f.Q.value = q;
    this.env(g, at, v, dur); s.connect(f).connect(g).connect(this.bus); s.start(at, Math.random() * .4); s.stop(at + dur + .02);
  },
  // tono con caída de afinación: da el "cuerpo" del clic
  tone(t, f0, f1, dur, v, type = "sine") {
    const c = this.ensure(); if (!c) return; const at = c.currentTime + t;
    const o = c.createOscillator(), g = c.createGain(); o.type = type;
    o.frequency.setValueAtTime(f0, at); o.frequency.exponentialRampToValueAtTime(f1, at + dur * .7);
    this.env(g, at, v, dur, .002); o.connect(g).connect(this.bus); o.start(at); o.stop(at + dur + .02);
  },
  // nota tipo marimba (fundamental + armónicos que se apagan rápido)
  pluck(t, f, dur, v) {
    [[1, 1], [3.98, .22], [9.1, .06]].forEach(([m, a], i) => {
      const c = this.ensure(); if (!c) return; const at = c.currentTime + t;
      const o = c.createOscillator(), g = c.createGain(); o.frequency.value = f * m;
      this.env(g, at, v * a, dur / (1 + i * 1.8), .003); o.connect(g).connect(this.bus); o.start(at); o.stop(at + dur + .02);
    });
  },
  /* sonidos propios: archivos en la carpeta "sounds" junto a la app (move.wav, select.wav…) reemplazan a los sintetizados */
  custom: {},
  async loadCustom(map, replace) {
    const c = this.ensure(); if (!c || !map) return;
    if (replace) this.custom = {};
    for (const [name, url] of Object.entries(map)) {
      try { const r = await fetch(url); this.custom[name] = await c.decodeAudioData(await r.arrayBuffer()); }
      catch (e) { console.warn("sonido", name, e); }
    }
  },
  sample(name, rate = 1) {
    const c = this.ensure(), b = this.custom[name]; if (!c || !b) return false;
    const s = c.createBufferSource(); s.buffer = b; s.playbackRate.value = rate; s.connect(this.bus); s.start();
    return true;
  },
  whoosh(t, f0, f1, dur, v) {
    const c = this.ensure(); if (!c) return; const at = c.currentTime + t;
    const s = c.createBufferSource(), f = c.createBiquadFilter(), g = c.createGain();
    s.buffer = this.noise; f.type = "bandpass"; f.Q.value = 1.4;
    f.frequency.setValueAtTime(f0, at); f.frequency.exponentialRampToValueAtTime(f1, at + dur);
    g.gain.setValueAtTime(.0001, at); g.gain.exponentialRampToValueAtTime(v, at + dur * .6); g.gain.exponentialRampToValueAtTime(.0001, at + dur);
    s.connect(f).connect(g).connect(this.bus); s.start(at); s.stop(at + dur + .02);
  }
};
/* Sonidos de un paquete importado (Switch 2…) que Scarlet sintetizado no tiene: si faltan, suena su equivalente. */
const SND_ALIAS = { movelist: "move", detail: "select", popupclose: "back", qopen: "select", qclose: "back", on: "select", off: "move", up: "tick", down: "tick", volume: "tick", tab: "move",
  capture: "notif", recstart: "notif", recstop: "notif", friend: "notif", friendgame: "notif", error: "bump" };
function play(n, x = 0) {
  if (n === "move" && Snd.custom.movelist && (views.length || QM.open)) n = "movelist";   // (solo con paquete: ya cargó todo)   // dentro de listas y menús
  const base = SND_ALIAS[n] || n;
  rumble(base);
  if (!settings.sound) return;
  if (Snd.custom[n] && Snd.sample(n, n === "tick" ? .85 + x * .15 : 1)) return;
  const alt = n === "home" ? "back" : SND_ALIAS[n];
  if (alt && Snd.custom[alt] && Snd.sample(alt, alt === "tick" ? .85 + x * .15 : 1)) return;
  n = base;
  switch (n) {
    case "move": Snd.click(0, 4300, 5, .022, .32); Snd.tone(0, 2300, 1700, .028, .035); break;
    case "select": Snd.click(0, 2600, 3, .03, .4); Snd.tone(0, 480, 1050, .07, .12); Snd.pluck(.04, 1568, .14, .035); break;
    case "back": Snd.click(0, 2000, 3, .03, .35); Snd.tone(0, 900, 420, .085, .1); break;
    case "bump": Snd.tone(0, 150, 100, .07, .055); Snd.click(0, 650, 1.4, .035, .06); break;
    case "type": Snd.click(0, 3600, 4, .02, .28); Snd.tone(0, 1500, 1200, .025, .03); break;
    case "launch": Snd.click(0, 2400, 2.5, .04, .45); Snd.whoosh(.02, 450, 3800, .42, .12); Snd.pluck(.16, 1047, .45, .055); Snd.pluck(.26, 1568, .6, .045); break;
    case "notif": Snd.pluck(0, 1319, .3, .06); Snd.pluck(.09, 1760, .42, .05); break;
    case "achieve": [1047, 1319, 1568, 2093].forEach((f, i) => Snd.pluck(i * .065, f, .5, .05)); break;
    case "tick": Snd.click(0, 3000, 4, .03, .35); Snd.tone(0, 650 + x * 230, 600 + x * 230, .06, .07); break;
    case "wake": [784, 1047, 1319, 1568].forEach((f, i) => Snd.pluck(i * .06, f, .55, .05)); break;
    case "home": Snd.click(0, 2000, 3, .03, .35); Snd.tone(0, 900, 420, .085, .1); break;
    case "popup": Snd.click(0, 2600, 3, .03, .4); Snd.tone(0, 480, 1050, .07, .12); Snd.pluck(.04, 1568, .14, .035); break;
    case "boot": Snd.whoosh(0, 300, 2400, .5, .08); [523, 784, 1047].forEach(f => Snd.pluck(.12, f, 1.1, .045)); Snd.pluck(.3, 1568, 1.2, .04); break;
  }
}

let soundPack = null;
async function soundPackMenu() {
  const opts = [{ label: soundPack ? "Buscar otro paquete" : "Importar paquete", value: "import" }];
  if (soundPack) opts.push({ label: "Volver a los de Scarlet", value: "reset" });
  const v = await choiceDialog(soundPack ? `Sonidos: ${soundPack}` : "Descarga el paquete (.zip) en Descargas y elige Importar.", opts, 0);
  if (v === "import") {
    toast("Buscando paquete…");
    const r = await Host.call("sounds.import").catch(e => ({ ok: false, error: e.message }));
    if (r && r.ok) { soundPack = r.name; toast(`Sonidos: ${r.name} (${r.count})`); setTimeout(() => play("select"), 600); }
    else toast((r && r.error) || "No se pudo importar");
  } else if (v === "reset") { await Host.call("sounds.reset").catch(() => {}); soundPack = null; toast("Volvieron los sonidos de Scarlet"); }
  const sv = topView(); if (sv && sv.isSettings && sv.refresh) sv.refresh();
}

/* ================= vibración ================= */
let lastPad = null;
function rumble(kind) {
  if (!settings.rumble) return;
  const m = { move: [0, .16, 16], select: [.18, .35, 38], back: [0, .25, 26], bump: [.1, .06, 30], launch: [.45, .6, 170], tick: [.2, .3, 30], achieve: [.3, .5, 120] }[kind];
  if (!m) return;
  const [strong, weak, ms] = m;
  if (Host.has) return Host.call("rumble", { strong, weak, ms }).catch(() => {});
  const p = lastPad && navigator.getGamepads && navigator.getGamepads()[lastPad.index];
  if (p && p.vibrationActuator) p.vibrationActuator.playEffect("dual-rumble", { duration: ms, strongMagnitude: strong, weakMagnitude: weak }).catch(() => {});
}

