// Pruebas del menú de descarga, pausar/reanudar/cancelar y abrir un juego con una descarga en curso.
const { chromium } = require('playwright');
const URL = 'http://localhost:8799/index.html';
const results = []; let browser;
const CP = 1091500, SB = 3489700;
async function page(opts = {}) {
  const p = await browser.newPage({ viewport: opts.vp || { width: 1280, height: 800 }, hasTouch: true });
  p.errs = [];
  p.on('pageerror', e => p.errs.push('JS: ' + e.message));
  p.on('console', m => { if (m.type() === 'error' && !/favicon|Failed to load resource|ERR_|net::/.test(m.text())) p.errs.push('consola: ' + m.text()); });
  await p.goto(URL); await p.waitForTimeout(3000);
  await p.evaluate(() => { if (mode === "boot") dismissBoot(); });
  await p.waitForTimeout(400);
  if (opts.setup) await p.evaluate(opts.setup);
  return p;
}
const ev = (p, f, a) => p.evaluate(f, a);
const keys = async (p, list, ms = 120) => { for (const k of list) { await p.evaluate(k => { input(k); }, k); await p.waitForTimeout(ms); } };
async function run(name, fn) {
  if (process.env.ONLY && !new RegExp(process.env.ONLY).test(name)) return;
  let p, ok = true, note = '';
  try { const r = await Promise.race([fn(), new Promise((_, rej) => setTimeout(() => rej(new Error('tardó más de 60 s')), 60000))]); p = r && r.p; note = (r && r.note) || ''; if (r && r.fail) { ok = false; note = r.fail; } }
  catch (e) { ok = false; note = 'excepción de la prueba: ' + e.message.split('\n')[0]; }
  const errs = p ? p.errs : []; if (errs.length) ok = false;
  results.push({ name, ok }); if (p) await p.close().catch(() => {});
  console.log((ok ? 'OK   ' : 'FALLA') + ' ' + name + (note ? ' — ' + note : '') + (errs.length ? '\n        ' + [...new Set(errs)].slice(0, 3).join('\n        ') : ''));
}
const calls = (p, t) => ev(p, t => window.__qa.log.filter(x => x.type === t).map(x => x.payload), t);
const prog = (p, o) => ev(p, o => window.__emit("download.progress", Object.assign({ appid: 1091500, kind: "Descargando", state: "active", bytes: 965476556, total: 84610793062, eta: 8051, rate: 10276044, phase: "download", progress: .0114 }, o)), o);
const focusOn = (p, id) => ev(p, id => { closeAllViews(); home.zone = "games"; home.g = homeGames().findIndex(g => g.appid === id); updateFocus(); }, id);
const menu = p => ev(p, () => { const v = topView(); if (!v || !v.isDownload) return null; const q = s => $(s, v.el).textContent; return { t: q('.dlh'), pc: q('.pc'), e: q('.dle'), z: q('.dlz'), w: $('.dlp .bar i', v.el).style.width, focus: v.items()[v.i].textContent, still: $('.dlp .sp', v.el).classList.contains('still') }; });

(async () => {
  browser = await chromium.launch();

  await run('A sobre el juego que se descarga: menú "Descargando datos…" con barra, %, tiempo y tamaño', async () => {
    const p = await page(); await prog(p, { progress: .37, bytes: 31306000000 }); await focusOn(p, CP);
    await keys(p, ['a'], 600);
    const m = await menu(p);
    const want = { t: 'Descargando datos…', pc: '37 %', e: 'Tiempo restante: 2 h 14 min', z: '29.16 GB de 78.80 GB · 9.8 MB/s', w: '37%', focus: 'Cerrar', still: false };
    return JSON.stringify(m) === JSON.stringify(want) ? { p } : { p, fail: JSON.stringify(m) };
  });

  await run('El menú avanza en vivo (cada aviso de Steam) y pasa a "Instalando archivos…"', async () => {
    const p = await page(); await prog(p, {}); await focusOn(p, CP); await keys(p, ['a'], 500);
    const a = await menu(p);
    await prog(p, { progress: .5, bytes: 42305396531, eta: 3000 }); await p.waitForTimeout(300);
    const b = await menu(p);
    await prog(p, { progress: 1, bytes: 84610793062, eta: 120, phase: "install" }); await p.waitForTimeout(300);
    const c = await menu(p);
    return a.pc === '1 %' && b.pc === '50 %' && b.e === 'Tiempo restante: 50 min' && c.t === 'Instalando archivos…' && c.e === 'Tiempo restante: 2 min' ? { p, note: `${a.pc} → ${b.pc} → ${c.t}` } : { p, fail: JSON.stringify({ a, b, c }) };
  });

  await run('Termina mientras el menú está abierto: se cierra y avisa', async () => {
    const p = await page(); await prog(p, {}); await focusOn(p, CP); await keys(p, ['a'], 500);
    await ev(p, () => window.__emit("download.done", { appid: 1091500 })); await p.waitForTimeout(8000);
    const r = await ev(p, () => [topView() && topView().isDownload ? $(".dlh", topView().el).textContent : 'cerrado', $('#notifs').textContent, byId(1091500).installed]);
    return r[0] === 'cerrado' && /Descarga completada/.test(r[1]) && r[2] ? { p, note: r[0] } : { p, fail: JSON.stringify(r) };
  });

  await run('Opciones de descarga → Pausar: Steam pausa, el menú dice "Descarga en pausa" y la franja se detiene', async () => {
    const p = await page(); await prog(p, {}); await focusOn(p, CP); await keys(p, ['a'], 500);
    await keys(p, ['up', 'a'], 400);
    const btns = await ev(p, () => $$(".dialog .dbtns button", topView().el).map(b => b.textContent));
    await keys(p, ['a'], 700);
    const m = await menu(p), c = await calls(p, 'download.pause');
    const strip = await ev(p, () => getComputedStyle(document.querySelector('#track [data-dl="1091500"]')).animationPlayState);
    return btns.join() === 'Atrás,Cancelar instalación,Pausar descarga' && c.length === 1 && c[0].appid === 1091500 && m.t === 'Descarga en pausa' && m.still && strip === 'paused' ? { p } : { p, fail: JSON.stringify({ btns, c, m, strip }) };
  });

  await run('Opciones → Reanudar (cuando está en pausa)', async () => {
    const p = await page(); await prog(p, { state: "paused" }); await focusOn(p, CP); await keys(p, ['a'], 500);
    await keys(p, ['up', 'a'], 400);
    const btn = await ev(p, () => $$(".dialog .dbtns button", topView().el).map(b => b.textContent)[2]);
    await keys(p, ['a'], 700);
    const m = await menu(p), c = await calls(p, 'download.resume');
    return btn === 'Reanudar descarga' && c.length === 1 && m.t === 'Descargando datos…' ? { p } : { p, fail: JSON.stringify({ btn, c, m }) };
  });

  await run('Opciones → Cancelar instalación: confirma, Steam la desinstala, regresa a "no instalado"', async () => {
    const p = await page(); await prog(p, {}); await focusOn(p, CP); await keys(p, ['a'], 500);
    await keys(p, ['up', 'a'], 400); await keys(p, ['left', 'a'], 500);
    const conf = await ev(p, () => topView().el.textContent);
    await keys(p, ['right', 'a'], 900);
    const r = await ev(p, () => ({ d: !!state.downloads[1091500], inst: byId(1091500).installed, views: views.length, strip: !!document.querySelector('[data-dl="1091500"]'), ic: $("#gameTitle").dataset.ic }));
    const c = await calls(p, 'download.cancel');
    if (!/Se borrará lo que ya se descargó/.test(conf)) return { p, fail: 'no confirmó: ' + conf.slice(0, 80) };
    return c.length === 1 && !r.d && !r.inst && !r.views && !r.strip && r.ic === 'cloud' ? { p } : { p, fail: JSON.stringify({ c, r }) };
  });

  await run('Cancelar instalación: si dices que no en la confirmación, no se cancela', async () => {
    const p = await page(); await prog(p, {}); await focusOn(p, CP); await keys(p, ['a'], 500);
    await keys(p, ['up', 'a'], 400); await keys(p, ['left', 'a'], 500); await keys(p, ['b'], 500);
    const c = await calls(p, 'download.cancel'), d = await ev(p, () => !!state.downloads[1091500]);
    return !c.length && d ? { p } : { p, fail: JSON.stringify({ c, d }) };
  });

  await run('Cancelar falla en Steam: aviso claro y la descarga sigue', async () => {
    const p = await page({ setup: () => { window.__qa.cancelFail = true; } }); await prog(p, {}); await focusOn(p, CP); await keys(p, ['a'], 500);
    await keys(p, ['up', 'a'], 400); await keys(p, ['left', 'a'], 500); await keys(p, ['right', 'a'], 900);
    const t = await ev(p, () => topView() && topView().el.textContent), d = await ev(p, () => !!state.downloads[1091500]);
    return /No se pudo cancelar/.test(t || '') && d ? { p } : { p, fail: JSON.stringify({ t, d }) };
  });

  await run('Abrir otro juego con una descarga: "Seguir descargando" NO abre el juego', async () => {
    const p = await page(); await prog(p, {}); await focusOn(p, SB); await keys(p, ['a'], 500);
    const t = await ev(p, () => [topView().el.textContent, $$(".dialog .dbtns button", topView().el).map(b => b.textContent), topView().items()[topView().i].textContent]);
    await keys(p, ['left', 'left', 'a'], 400);
    const r = await ev(p, () => [mode, window.__qa.calls.includes("game.launch"), window.__qa.calls.includes("download.pause")]);
    if (!/Cyberpunk 2077 se está descargando/.test(t[0]) || t[1].join() !== 'Seguir descargando,Cancelar y jugar,Pausar y jugar' || t[2] !== 'Pausar y jugar') return { p, fail: JSON.stringify(t) };
    return r[0] === 'ui' && !r[1] && !r[2] ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('B en esa pregunta = tampoco abre el juego', async () => {
    const p = await page(); await prog(p, {}); await focusOn(p, SB); await keys(p, ['a'], 500); await keys(p, ['b'], 400);
    const r = await ev(p, () => [mode, window.__qa.calls.includes("game.launch")]);
    return r[0] === 'ui' && !r[1] ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('"Pausar y jugar": pausa, abre el juego, y al cerrarlo la descarga sigue sola', async () => {
    const p = await page(); await prog(p, {}); await focusOn(p, SB); await keys(p, ['a'], 500); await keys(p, ['a'], 2800);
    const a = await ev(p, () => [mode, state.running, window.__qa.calls.filter(c => c === "download.pause").length, state.downloads[1091500].st]);
    await ev(p, () => window.__emit("game.exited", { appid: 3489700 })); await p.waitForTimeout(2200);
    const b = await ev(p, () => [mode, window.__qa.calls.filter(c => c === "download.resume").length, state.downloads[1091500].st, $('#notifs').textContent]);
    if (a[0] !== 'game' || a[1] !== SB || a[2] !== 1 || a[3] !== 'paused') return { p, fail: 'al jugar: ' + JSON.stringify(a) };
    return b[1] === 1 && b[2] === 'active' && /Descarga reanudada/.test(b[3]) ? { p } : { p, fail: 'al salir: ' + JSON.stringify(b) };
  });

  await run('"Pausar y jugar" y cerrar el juego desde Scarlet (X): también reanuda', async () => {
    const p = await page(); await prog(p, {}); await focusOn(p, SB); await keys(p, ['a'], 500); await keys(p, ['a'], 2800);
    await ev(p, () => { window.__emit("guide.double", { hidden: true }); }); await p.waitForTimeout(600);
    await ev(p, () => closeRunning(true)); await p.waitForTimeout(2200);
    const n = await ev(p, () => window.__qa.calls.filter(c => c === "download.resume").length);
    return n === 1 ? { p } : { p, fail: 'resume ' + n };
  });

  await run('"Cancelar y jugar": confirma, desinstala lo descargado y abre el juego', async () => {
    const p = await page(); await prog(p, {}); await focusOn(p, SB); await keys(p, ['a'], 500);
    await keys(p, ['left', 'a'], 500); await keys(p, ['right', 'a'], 3200);
    const r = await ev(p, () => [mode, state.running, !!state.downloads[1091500], window.__qa.calls.includes("download.cancel")]);
    return r[0] === 'game' && r[1] === SB && !r[2] && r[3] ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Descarga ya en pausa: abrir otro juego no pregunta nada', async () => {
    const p = await page(); await prog(p, { state: "paused" }); await focusOn(p, SB); await keys(p, ['a'], 2800);
    const r = await ev(p, () => [mode, state.running]);
    return r[0] === 'game' && r[1] === SB ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Apps (YouTube, Apple Music) se abren sin preguntar por la descarga', async () => {
    const p = await page(); await prog(p, {}); await focusOn(p, 900001); await keys(p, ['a'], 1500);
    const r = await ev(p, () => [window.__qa.calls.includes("app.open") || window.__qa.calls.includes("game.launch"), topView() && topView().cls === 'dialog' && /descargando/.test(topView().el.textContent)]);
    return r[0] && !r[1] ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Dos descargas: la pregunta no ofrece cancelar (solo pausar o seguir)', async () => {
    const p = await page(); await prog(p, {}); await prog(p, { appid: 1245620, kind: "Actualizando", state: "queued" }); await focusOn(p, SB); await keys(p, ['a'], 500);
    const t = await ev(p, () => [topView().el.textContent, $$(".dialog .dbtns button", topView().el).map(b => b.textContent)]);
    return /Se están descargando 2 juegos/.test(t[0]) && t[1].join() === 'Seguir descargando,Pausar y jugar' ? { p } : { p, fail: JSON.stringify(t) };
  });

  await run('La franja del mosaico se queda (no desaparece) y la barra se llena con el %', async () => {
    const p = await page(); await prog(p, { progress: .25 }); await p.waitForTimeout(300);
    await ev(p, () => { openView(settingsView()); }); await p.waitForTimeout(300); await ev(p, () => closeAllViews()); await p.waitForTimeout(300);
    await ev(p, () => renderHome()); await prog(p, { progress: .26 }); await p.waitForTimeout(800);
    const r = await ev(p, () => { const el = document.querySelector('#track [data-dl="1091500"]'); return el ? [el.querySelector('i').style.width, getComputedStyle(el.querySelector('i')).backgroundColor] : null; });
    return r && r[0] === '26%' ? { p, note: 'relleno ' + r[1] } : { p, fail: JSON.stringify(r) };
  });

  await run('El nombre arriba del juego descargando lleva solo nombre e ícono', async () => {
    const p = await page(); await prog(p, { progress: .4 }); await focusOn(p, CP); await p.waitForTimeout(300);
    const t = await ev(p, () => [$("#gameTitle").textContent.trim(), $("#gameTitle").dataset.ic, !!$("#gtPct")]);
    return t[0] === 'Cyberpunk 2077' && t[1] === 'dl' && !t[2] ? { p } : { p, fail: JSON.stringify(t) };
  });

  await run('Tocar la pantalla: "Opciones de descarga" y "Cerrar" con el dedo', async () => {
    const p = await page(); await prog(p, {}); await focusOn(p, CP); await keys(p, ['a'], 500);
    await p.locator('.view.dialog [data-b="opt"]').tap(); await p.waitForTimeout(500);
    const a = await ev(p, () => /Opciones de descarga/.test(topView().el.textContent));
    await keys(p, ['b'], 400);
    await p.locator('.view.dialog [data-b="close"]').tap(); await p.waitForTimeout(500);
    const b = await ev(p, () => views.length);
    return a && b === 0 ? { p } : { p, fail: JSON.stringify({ a, b }) };
  });

  await run('Pantalla de TV 1280×720, modo claro: el menú cabe completo', async () => {
    const p = await page({ vp: { width: 1280, height: 720 }, setup: () => { settings.theme = "light"; applyTheme(); } });
    await prog(p, {}); await focusOn(p, CP); await keys(p, ['a'], 600);
    const r = await ev(p, () => { const b = $(".dbox", topView().el).getBoundingClientRect(); return [b.top >= 0, b.bottom <= innerHeight]; });
    await p.screenshot({ path: __dirname + '/dl_claro.png' });
    return r[0] && r[1] ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('log-9: copiando desde la laptop (YONIS): "Transfiriendo desde YONIS…", 7 %, 5.90 GB de 78.80 GB', async () => {
    const p = await page(); await prog(p, { progress: .07, bytes: 6335076761, eta: 3419, phase: "lan", peer: "YONIS", rate: 2100000 }); await focusOn(p, CP);
    await keys(p, ['a'], 600);
    const m = await menu(p), w = await ev(p, () => document.querySelector('[data-dl="1091500"] i').style.width);
    const want = { t: 'Transfiriendo desde YONIS…', pc: '7 %', e: 'Tiempo restante: 57 min', z: '5.90 GB de 78.80 GB · 2.0 MB/s' };
    return m.t === want.t && m.pc === want.pc && m.e === want.e && m.z === want.z && parseFloat(w) === 7 ? { p, note: `${m.t} ${m.pc} · franja ${w}` } : { p, fail: JSON.stringify({ m, w }) };
  });

  await run('Cancelar: la franja dice "Cancelando…" mientras Steam contesta, y un avance atrasado no la revive', async () => {
    const p = await page({ setup: () => { window.__qa.cancelSlow = true; } }); await prog(p, {});
    await ev(p, () => { cancelDownload(byId(1091500), true); });
    await p.waitForTimeout(150);
    const mid = await ev(p, () => [state.downloads[1091500] && state.downloads[1091500].st, document.querySelector('[data-dl="1091500"]').getAttribute('aria-label')]);
    await ev(p, () => { cancelDownload(byId(1091500), true); });   // otra vez mientras cancela: no manda otra orden
    await p.waitForTimeout(1500);
    await prog(p, { progress: .2 }); await p.waitForTimeout(300);
    const r = await ev(p, () => [!!state.downloads[1091500], !!document.querySelector('[data-dl="1091500"]'), window.__qa.calls.filter(c => c === "download.cancel").length]);
    return mid[0] === 'canceling' && mid[1] === 'Cancelando…' && !r[0] && !r[1] && r[2] === 1 ? { p, note: JSON.stringify({ mid, r }) } : { p, fail: JSON.stringify({ mid, r }) };
  });

  await run('Lo descargado se borra después: avisos de "Borrando…", listo y si no se pudo', async () => {
    const p = await page();
    await ev(p, () => { window.__emit("download.cleanup", { appid: 1091500, state: "restarting" }); }); await p.waitForTimeout(300);
    const a = await ev(p, () => $('#notifs').textContent);
    await ev(p, () => { window.__emit("download.cleanup", { appid: 1091500, state: "done" }); }); await p.waitForTimeout(300);
    const b = await ev(p, () => document.body.textContent.includes('Se borró lo descargado de'));
    await ev(p, () => { window.__emit("download.cleanup", { appid: 1091500, state: "failed" }); }); await p.waitForTimeout(300);
    const c = await ev(p, () => $('#notifs').textContent);
    return /Borrando lo descargado/.test(a) && b && /No se pudo borrar todo/.test(c) ? { p } : { p, fail: JSON.stringify({ a: a.slice(0, 80), b, c: c.slice(0, 120) }) };
  });

  // ───── actualizaciones, trabajos extra, avisos y desinstalar (0.4.29)
  const SB_ = 3489700, ER = 1245620;
  const notifs = p => ev(p, () => $('#notifs').textContent);
  const strip = (p, id) => ev(p, id => !!document.querySelector(`[data-dl="${id}"]`), id);
  const emitP = (p, o) => ev(p, o => { window.__emit("download.progress", Object.assign({ kind: "Actualizando", state: "active", bytes: 4e8, total: 1e9, eta: 120, rate: 5e6, phase: "download", progress: .4 }, o)); }, o);

  await run('Actualización pendiente (Steam no la baja): sin franja; al jugar pregunta y "Actualizar ahora" la arranca', async () => {
    const p = await page(); await emitP(p, { appid: SB_, state: "needs", progress: 0, bytes: 0 }); await p.waitForTimeout(300);
    const s1 = await strip(p, SB_);
    await focusOn(p, SB_); await keys(p, ['a'], 600);
    const q = await ev(p, () => topView() ? topView().el.textContent : '');
    await keys(p, ['a'], 700);   // "Actualizar ahora" (la opción enfocada)
    const r = await ev(p, () => [window.__qa.calls.includes("download.now"), window.__qa.calls.includes("game.launch"), !!(topView() && topView().isDownload), state.downloads[3489700] && state.downloads[3489700].kind]);
    return !s1 && /actualización pendiente/.test(q) && r[0] && !r[1] && r[2] && r[3] === "Actualizando" ? { p, note: q.slice(0, 60) } : { p, fail: JSON.stringify({ s1, q: q.slice(0, 120), r }) };
  });

  await run('Actualización pendiente → "Jugar así": se intenta abrir (si Steam la exige, se ve como descarga)', async () => {
    const p = await page(); await emitP(p, { appid: SB_, state: "needs", progress: 0 }); await focusOn(p, SB_);
    await keys(p, ['a'], 600); await keys(p, ['left', 'a'], 900);
    const r = await ev(p, () => window.__qa.calls.includes("game.launch"));
    return r ? { p } : { p, fail: 'no intentó abrir' };
  });

  await run('Steam exige actualizar al abrir: muestra la descarga, avisa y abre el juego solo al terminar', async () => {
    const p = await page({ setup: () => { window.__qa.updateOnLaunch = 3489700; } }); await focusOn(p, SB_);
    await keys(p, ['a'], 1600);
    await emitP(p, { appid: SB_, progress: .5 }); await p.waitForTimeout(300);
    const a = await ev(p, () => [mode, !!(topView() && topView().isDownload), $('#notifs').textContent]);
    const n0 = await ev(p, () => window.__qa.calls.filter(c => c === "game.launch").length);
    await ev(p, () => { window.__emit("download.done", { appid: 3489700, fresh: false }); }); await p.waitForTimeout(4500);
    const b = await ev(p, () => [window.__qa.calls.filter(c => c === "game.launch").length, state.running]);
    return a[0] === 'ui' && a[1] && /Primero se actualiza/.test(a[2]) && b[0] === n0 + 1 && b[1] === 3489700 ? { p, note: `lanzamientos ${n0} → ${b[0]}` } : { p, fail: JSON.stringify({ a: [a[0], a[1], a[2].slice(0, 60)], n0, b }) };
  });

  await run('Termina la instalación y siguen sombreadores: un solo aviso "Descarga completada", sin franja de más', async () => {
    const p = await page(); await prog(p, { fresh: true, progress: .99 }); await p.waitForTimeout(200);
    await prog(p, { fresh: false, kind: "Actualizando", phase: "extra", progress: .2 }); await p.waitForTimeout(300);
    const s1 = await strip(p, CP), inst = await ev(p, () => byId(1091500).installed);
    await ev(p, () => { window.__emit("download.done", { appid: 1091500, extra: true }); }); await p.waitForTimeout(8500);
    const t = await notifs(p);
    const n = (t.match(/Descarga completada/g) || []).length + (t.match(/Actualización completada/g) || []).length;
    return !s1 && inst && n === 1 && /Descarga completada/.test(t) ? { p } : { p, fail: JSON.stringify({ s1, inst, n, t: t.slice(0, 160) }) };
  });

  await run('Justo después de instalar Steam baja más del mismo juego (DLC): sigue siendo "Descargando" y avisa una vez al final', async () => {
    const p = await page(); await prog(p, { fresh: true, progress: .99 });
    await ev(p, () => { window.__emit("download.done", { appid: 1091500, fresh: true }); }); await p.waitForTimeout(1500);
    await prog(p, { fresh: false, kind: "Actualizando", progress: .1 }); await p.waitForTimeout(300);
    const mid = await ev(p, () => [state.downloads[1091500] && state.downloads[1091500].kind, !!document.querySelector('[data-dl="1091500"]'), $('#notifs').textContent]);
    await ev(p, () => { window.__emit("download.done", { appid: 1091500, fresh: false }); }); await p.waitForTimeout(8500);
    const t = await notifs(p);
    const n = (t.match(/completada/g) || []).length;
    return mid[0] === "Descargando" && mid[1] && !/completada/.test(mid[2]) && n === 1 && /Descarga completada/.test(t) ? { p } : { p, fail: JSON.stringify({ mid: [mid[0], mid[1], mid[2].slice(0, 60)], n, t: t.slice(0, 160) }) };
  });

  await run('Varias actualizaciones terminan juntas: un solo aviso "2 juegos actualizados"', async () => {
    const p = await page(); await emitP(p, { appid: SB_ }); await emitP(p, { appid: ER, state: "queued", progress: 0 });
    await ev(p, () => { window.__emit("download.done", { appid: 3489700 }); window.__emit("download.done", { appid: 1245620 }); }); await p.waitForTimeout(4000);
    const t = await notifs(p);
    return /2 juegos actualizados/.test(t) && !/Actualización completada/.test(t) ? { p } : { p, fail: t.slice(0, 160) };
  });

  await run('Cancelar una ACTUALIZACIÓN: dice "Cancelar actualización", no desinstala y el juego sigue instalado', async () => {
    const p = await page({ setup: () => { window.__qa.cancelUpdate = true; } }); await emitP(p, { appid: SB_ }); await focusOn(p, SB_);
    await keys(p, ['a'], 600); await keys(p, ['up', 'a'], 500);
    const opts = await ev(p, () => topView().el.textContent);
    await keys(p, ['left', 'a'], 500);
    const conf = await ev(p, () => topView().el.textContent);
    await keys(p, ['right', 'a'], 900);
    const r = await ev(p, () => [byId(3489700).installed, !!state.downloads[3489700], document.body.textContent.includes('Se canceló la actualización')]);
    return /Cancelar actualización/.test(opts) && /el juego se queda como está/.test(conf) && r[0] && !r[1] ? { p, note: r[2] ? 'aviso ok' : '' } : { p, fail: JSON.stringify({ opts: opts.slice(0, 80), conf: conf.slice(0, 100), r }) };
  });

  await run('Steam detiene la descarga (sin espacio): aviso una vez, el menú dice por qué y "Reintentar" funciona', async () => {
    const p = await page(); await prog(p, { state: "error", err: 12, errText: "No hay espacio suficiente en ese disco." });
    await prog(p, { state: "error", err: 12, errText: "No hay espacio suficiente en ese disco." }); await p.waitForTimeout(300);
    const t = await notifs(p); const n = (t.match(/Descarga detenida/g) || []).length;
    await focusOn(p, CP); await keys(p, ['a'], 600);
    const m = await menu(p);
    await keys(p, ['up', 'a'], 500); const o = await ev(p, () => topView().el.textContent); await keys(p, ['a'], 600);
    const c = await calls(p, 'download.now');
    return n === 1 && m.t === 'Descarga detenida' && /espacio/.test(m.e) && /Reintentar/.test(o) && c.length === 1 ? { p } : { p, fail: JSON.stringify({ n, m, o: o.slice(0, 80), c }) };
  });

  await run('Sin internet a media descarga: "Esperando conexión…" y sigue sola', async () => {
    const p = await page(); await prog(p, {}); await ev(p, () => { NET.internet = false; }); await prog(p, { progress: .02 });
    await focusOn(p, CP); await keys(p, ['a'], 600);
    const m = await menu(p);
    return m.t === 'Esperando conexión…' && /vuelva el internet/.test(m.e) ? { p } : { p, fail: JSON.stringify(m) };
  });

  await run('Desinstalar desde Gestión de datos: sin ventana de Steam, "desinstalando…" y "Desinstalado" cuando de verdad se borró', async () => {
    const p = await page({ setup: () => { window.__qa.uninstallMs = 1200; } });
    await ev(p, () => { closeAllViews(); openView(storageView()); }); await p.waitForTimeout(600);
    await ev(p, () => { const b = document.querySelector('[data-g="3489700"]'); b.click(); }); await p.waitForTimeout(400);
    await keys(p, ['a'], 500);   // "Desinstalar juego"
    await keys(p, ['right', 'a'], 600);   // confirmar
    const mid = await ev(p, () => [byId(3489700).installed, document.body.textContent.includes('desinstalando…'), $('#notifs').textContent.includes('Desinstalado')]);
    await p.waitForTimeout(1500);
    const end = await ev(p, () => [byId(3489700).installed, $('#notifs').textContent]);
    const c = await calls(p, 'game.uninstall');
    return c.length === 1 && mid[0] && mid[1] && !mid[2] && !end[0] && /Desinstalado/.test(end[1]) ? { p } : { p, fail: JSON.stringify({ c, mid, end: [end[0], end[1].slice(0, 80)] }) };
  });

  await run('Desinstalar falla en Steam: aviso claro y el juego sigue instalado', async () => {
    const p = await page({ setup: () => { window.__qa.uninstallBad = true; window.__qa.uninstallMs = 300; } });
    await ev(p, () => { closeAllViews(); openView(storageView()); }); await p.waitForTimeout(600);
    await ev(p, () => { document.querySelector('[data-g="3489700"]').click(); }); await p.waitForTimeout(400);
    await keys(p, ['a'], 500); await keys(p, ['right', 'a'], 1200);
    const r = await ev(p, () => [byId(3489700).installed, topView() ? topView().el.textContent : '']);
    return r[0] && /No se pudo desinstalar/.test(r[1]) ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Estrés: 300 botones al azar con descargas en curso', async () => {
    const p = await page(); await prog(p, {});
    const r = await ev(p, async () => {
      const ks = ["up", "down", "left", "right", "a", "b", "x", "y"]; let seed = 3, errs = [];
      const rnd = () => (seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648;
      for (let n = 0; n < 300; n++) {
        try {
          if (views.some(v => v.isPower) || (topView() && /Salir al escritorio/.test(topView().el.textContent))) closeAllViews();
          if (mode === "game") { window.__emit("game.exited", { appid: state.running }); mode = "ui"; }
          if (n % 30 === 0) { closeAllViews(); home.zone = "games"; home.g = Math.floor(rnd() * 4); updateFocus(); }
          if (n % 7 === 0) window.__emit("download.progress", { appid: 1091500, kind: "Descargando", state: rnd() > .7 ? "paused" : "active", progress: n / 300, bytes: n * 1e8, total: 3e10, eta: 300 - n, rate: 1e7 });
          input(ks[Math.floor(rnd() * ks.length)]);
        } catch (e) { errs.push(e.message); }
        await new Promise(r => setTimeout(r, 30));
      }
      return errs;
    });
    return r.length ? { p, fail: r.slice(0, 3).join(' | ') } : { p };
  });

  await browser.close();
  const bad = results.filter(r => !r.ok);
  console.log(`\n${results.length - bad.length}/${results.length} escenarios de descarga OK`);
})();
