/* ===================== beta 1.8: Ajustes › Pantalla =====================
   Brillo (barra), brillo automático, frecuencia, resolución y HDR de la pantalla que se usa. Con una tele o monitor
   conectado, Scarlet usa solo esa pantalla (como la Switch) y aquí aparecen sus opciones: resolución hasta lo que dé
   (2160p…), HDR, tamaño de la interfaz y margen para la tele. Cambiar la resolución o la frecuencia pregunta «¿Mantener?»
   y, si no eliges nada en 15 s, regresa sola (la app también lo hace por su cuenta, por si la pantalla se queda en negro).
   HDR siempre se ve: sin pantalla con HDR solo «Desactivado» y «Automático»; con una que lo tenga, también «Activado». */
const DISP_DEMO = tv => tv
  ? { ok: true, name: "LG OLED", external: true, w: 3840, h: 2160, hz: 60, maxH: 2160, resolutions: [{ w: 3840, h: 2160 }, { w: 2560, h: 1440 }, { w: 1920, h: 1080 }, { w: 1280, h: 720 }], rates: [120, 60], hdr: { supported: true, on: true, mode: "auto" } }
  : { ok: true, name: "Pantalla de la consola", external: false, w: 1920, h: 1080, hz: 120, maxH: 1080, resolutions: [{ w: 1920, h: 1080 }, { w: 1600, h: 900 }, { w: 1280, h: 720 }], rates: [120, 60], hdr: { supported: false, on: false, mode: "auto" } };
let disp = null;
async function loadDisplay() {
  if (!Host.has) disp = DISP_DEMO(document.documentElement.classList.contains("tv"));
  else disp = await Host.call("display.state").catch(() => null);
  if (disp && !disp.ok) disp = null;
  return disp;
}
Events.on("display.changed", () => { loadDisplay().then(() => { const v = views.find(x => x.isSettings); if (v && v.refresh) v.refresh(); }); });

const resLabel = h => h >= 2160 ? `${h}p (4K)` : h >= 1440 ? `${h}p (2K)` : `${h}p`;
const HDR_OPTS = () => [["Desactivado", "off"], ["Automático", "auto"], ...(disp && disp.hdr.supported ? [["Activado", "on"]] : [])];
const hdrLabel = () => { const m = disp ? disp.hdr.mode : "auto"; return (HDR_OPTS().find(o => o[1] === m) || HDR_OPTS()[0])[0]; };
const hdrDesc = () => disp && disp.hdr.supported
  ? `${disp.external ? "Esta pantalla" : "La pantalla"} tiene HDR. «Automático» lo enciende solo; «Activado» lo deja siempre prendido.`
  : "Esta pantalla no tiene HDR. En «Automático» se enciende solo al conectar una pantalla que sí lo tenga.";

/* Elegir resolución o frecuencia y confirmar. */
function pickResolution() {
  if (!disp) return;
  const opts = disp.resolutions.map((r, i) => ({ label: resLabel(r.h), value: r.h, extra: i === 0 ? "Recomendada" : r.h <= 720 && !disp.external ? "Más fps en juegos en ventana" : "" }));
  openView(pickerView("Resolución", I.gear, opts, disp.h, h => { const r = disp.resolutions.find(x => x.h === h); if (r) setMode(r.w, r.h, 0); }));
}
function pickRate() {
  if (!disp) return;
  const opts = disp.rates.map((hz, i) => ({ label: hz + " Hz", value: hz, extra: i === 0 && disp.rates.length > 1 ? "Más fluido" : hz <= 60 && !disp.external ? "Ahorra batería" : "" }));
  openView(pickerView("Frecuencia de actualización", I.gear, opts, disp.hz, hz => setMode(disp.w, disp.h, hz)));
}
async function setMode(w, h, hz) {
  const prev = { w: disp.w, h: disp.h, hz: disp.hz };
  if (w === prev.w && h === prev.h && (!hz || hz === prev.hz)) return;
  const r = Host.has ? await Host.call("display.set", { w, h, hz }).catch(e => ({ ok: false, error: e.message })) : { ok: true };
  if (!r || !r.ok) return toast((r && r.error) || "La pantalla no acepta ese modo");
  if (r.same) return;
  await loadDisplay(); if (!Host.has) Object.assign(disp, { w, h, hz: hz || disp.hz });
  keepDialog(h, prev);
}
/* «¿Mantener…?» con cuenta regresiva. Si no se elige nada, regresa (y la app también regresa sola por si acaso). */
const KEEP_SECS = 15;
function keepDialog(h, prev) {
  let left = KEEP_SECS, timer = 0, done = false;
  const finish = keep => {
    if (done) return; done = true; clearInterval(timer);
    if (topView() === v) closeView();
    if (keep) { if (Host.has) Host.call("display.keep").catch(() => {}); toast(`Se queda en ${resLabel(disp ? disp.h : h)}`); }
    else { if (Host.has) Host.call("display.revert").catch(() => {}); else Object.assign(disp, prev); toast(`Se regresó a ${resLabel(prev.h)}`); }
    loadDisplay().then(() => { const s = views.find(x => x.isSettings); if (s && s.refresh) s.refresh(); });
  };
  const v = {
    cls: "dialog", isKeep: true, cols: 2, i: 1, hints: [["B", "Regresar"], ["A", "Aceptar"]],
    render(el) {
      el.innerHTML = `<div class="dbox"><p>¿Mantener ${esc(resLabel(h))}${disp && disp.hz ? ` a ${disp.hz} Hz` : ""}? Si no eliges nada, se regresa a ${esc(resLabel(prev.h))} en ${KEEP_SECS} s.</p>
        <div class="dbtns"><button class="fi" data-r="0">Regresar (<b class="kc">${left}</b>)</button><button class="fi" data-r="1">Mantener</button></div></div>`;
      fis(el).forEach(b => b.onclick = () => finish(b.dataset.r === "1"));
      timer = setInterval(() => { left--; const c = $(".kc", el); if (c) c.textContent = left; if (left <= 0) finish(false); }, 1000);
    },
    items() { return fis(this.el); },
    onB() { finish(false); return true; },
    keepFinish: finish
  };
  openView(v);
}
Events.on("display.reverted", () => { const v = views.find(x => x.isKeep); if (v) v.keepFinish(false); else { toast("Se regresó a la resolución de antes"); loadDisplay(); } });

function pickHdr() {
  openView(pickerView("HDR", I.gear, HDR_OPTS().map(([label, value]) => ({ label, value, extra: value === "auto" ? (disp && disp.hdr.supported ? "Lo enciende solo" : "Se enciende al conectar una pantalla con HDR") : value === "on" ? "Siempre" : "" })),
    disp ? disp.hdr.mode : "auto", async mode => {
      if (Host.has) { const r = await Host.call("display.hdr", { mode }).catch(() => null); if (r && r.ok) disp = r; }
      else if (disp) disp.hdr.mode = mode;
      const s = views.find(x => x.isSettings); if (s && s.refresh) s.refresh();
    }));
}

/* ---- Tamaño de la interfaz y margen para la tele (solo con pantalla externa) ---- */
const TV_SCALES = [["90 %", 90], ["100 %", 100], ["110 %", 110], ["125 %", 125], ["150 %", 150]];
function applyTvLook() {
  const r = document.documentElement.style, tv = document.documentElement.classList.contains("tv");
  r.setProperty("--tvscale", tv ? (settings.tvScale || 100) / 100 : 1);
  r.setProperty("--tvmargin", tv ? (settings.tvMargin || 100) / 100 : 1);
}
const tvObs = new MutationObserver(applyTvLook); tvObs.observe(document.documentElement, { attributes: true, attributeFilter: ["class"] }); applyTvLook();

function marginView() {
  const start = settings.tvMargin || 100;
  const ar = t => `<svg viewBox="0 0 24 24" class="tvar" style="${t}" fill="none" stroke="var(--accent)" stroke-width="2.4" stroke-linecap="round"><path d="M4 4h7M4 4v7M4 4l7 7"/></svg>`;
  return {
    cls: "tvmargin", hints: [["B", "Cancelar"], ["◀▶", "Ajustar"], ["A", "Listo"]],
    render(el) {
      el.innerHTML = `<div class="tvframe">${ar("left:1rem;top:1rem")}${ar("right:1rem;top:1rem;transform:scaleX(-1)")}${ar("left:1rem;bottom:1rem;transform:scaleY(-1)")}${ar("right:1rem;bottom:1rem;transform:scale(-1)")}
        <div class="tvmid"><h2>Margen para TV</h2><p>Mueve con ◀ ▶ hasta que veas completas las 4 flechas</p>
        <div class="tvval"><button class="tvb" data-d="-1">◀</button><b></b><button class="tvb" data-d="1">▶</button></div><button class="fi tvok">Listo</button></div></div>`;
      $$(".tvb", el).forEach(b => b.onclick = e => { e.stopPropagation(); this.step(+b.dataset.d); });
      $(".tvok", el).onclick = () => { saveSettings(); closeView(); toast(`Margen: ${settings.tvMargin || 100} %`); };
      this.paint();
    },
    paint() { const b = $(".tvval b", this.el); if (b) b.textContent = (settings.tvMargin || 100) + " %"; applyTvLook(); },
    step(d) { const v = clamp((settings.tvMargin || 100) + d, 85, 100); if (v === (settings.tvMargin || 100)) return bump(); settings.tvMargin = v; play("tick"); this.paint(); },
    nav(dir) { if (dir === "left" || dir === "right") return this.step(dir === "left" ? -1 : 1); },
    items() { return fis(this.el); },
    onB() { settings.tvMargin = start; applyTvLook(); closeView(); return true; }
  };
}

/* Las filas de la categoría Pantalla (las usa settingsCats). */
function displayRows(sw, opt) {
  const ext = disp && disp.external, bri = { label: "Brillo", desc: "También se cambia desde el menú del botón Xbox", slider: true,
    off: () => (typeof QM !== "undefined" && QM.noBri) || sys.autoBri, val: () => sys.brightness, set(v) { sys.brightness = v; applySys("brightness"); } };
  const general = ["General", null];   // lo llena settingsCats con lo de siempre
  const mode = [
    { label: "Resolución", desc: ext ? `La pantalla llega hasta ${resLabel(disp.maxH)}` : "La resolución de Windows. Los juegos en ventana sin bordes la usan; los de pantalla completa usan la suya.", val: () => disp ? resLabel(disp.h) + (disp.resolutions[0] && disp.resolutions[0].h === disp.h ? " (recomendada)" : "") : "—", act: pickResolution },
    { label: "Frecuencia de actualización", desc: ext ? "Lo que soporte la pantalla" : "120 Hz se ve más fluido; 60 Hz ahorra batería", val: () => disp ? disp.hz + " Hz" : "—", act: pickRate },
    { label: "HDR", desc: hdrDesc(), val: hdrLabel, act: pickHdr }
  ];
  if (ext) return [
    [`Pantalla externa · ${disp.name}`, [...mode,
      { label: "Tamaño de la interfaz", desc: "Todo un poco más grande para verlo desde el sillón", val: () => optLabel(TV_SCALES, settings.tvScale || 100),
        act() { openView(pickerView("Tamaño de la interfaz", I.gear, TV_SCALES.map(([label, value]) => ({ label, value })), settings.tvScale || 100, v => { settings.tvScale = v; saveSettings(); applyTvLook(); const s = views.find(x => x.isSettings); if (s && s.refresh) s.refresh(); })); } },
      { label: "Margen para TV", desc: "Si la tele recorta las orillas, encoge Scarlet hasta que se vea completo", go: true, val: () => (settings.tvMargin || 100) < 100 ? settings.tvMargin + " %" : "", act() { openView(marginView()); } }]],
    ["Pantalla de la consola", [{ label: "Apagada mientras usas la pantalla externa", desc: "Se vuelve a encender sola al desconectar el cable, como en la Switch", val: () => "", act() {} }]],
    general];
  return [
    ["Pantalla de la consola", [bri, ...(QM.autoBriOk || !Host.has ? [sw("Brillo automático", "Se ajusta solo según la luz del cuarto", () => !!sys.autoBri, v => setAutoBright(v))] : []), ...mode]],
    general];
}
if (settings.tvScale == null) settings.tvScale = 100;
/* La categoría completa: lo nuevo arriba y, en «General», lo de siempre (atenuar, puntero, reloj); luego lo demás. */
function displaySecs(sw, opt, old) {
  const rows = displayRows(sw, opt), gen = rows.find(x => x[0] === "General");
  gen[1] = old[0][1];
  return [...rows, ...old.slice(1)];
}
loadDisplay();
