// Pruebas de la beta 1.8: Ajustes › Pantalla (brillo, resolución con «¿Mantener?», frecuencia, HDR, pantalla externa)
// y lo táctil que faltaba (ayudas ◀ ▶ tocables, deslizar en el visor del Álbum, arrastrar el recorte, salir de Probar botones).
const { chromium } = require('playwright');
const URL = 'http://localhost:8799/index.html';
const results = []; let browser;
async function page(opts = {}) {
  const p = await browser.newPage({ viewport: opts.vp || { width: 1280, height: 800 }, hasTouch: true });
  p.errs = [];
  p.on('pageerror', e => p.errs.push('JS: ' + e.message));
  p.on('console', m => { if (m.type() === 'error' && !/favicon|Failed to load resource|ERR_|net::/.test(m.text())) p.errs.push('consola: ' + m.text()); });
  await p.goto(URL + (opts.q || '')); await p.waitForTimeout(opts.wait || 3000);
  await p.evaluate(() => { if (mode === "boot") dismissBoot(); $('#notifs').innerHTML = ''; });
  await p.waitForTimeout(400);
  return p;
}
const ev = (p, f, a) => p.evaluate(f, a);
const keys = async (p, list, ms = 150) => { for (const k of list) { await p.evaluate(k => { input(k); }, k); await p.waitForTimeout(ms); } };
const calls = (p, t) => ev(p, t => window.__qa.log.filter(x => x.type === t).map(x => x.payload), t);
async function run(name, fn) {
  if (process.env.ONLY && !new RegExp(process.env.ONLY).test(name)) return;
  let p, ok = true, note = '';
  try { const r = await Promise.race([fn(), new Promise((_, rej) => setTimeout(() => rej(new Error('tardó más de 60 s')), 60000))]); p = r && r.p; note = (r && r.note) || ''; if (r && r.fail) { ok = false; note = r.fail; } }
  catch (e) { ok = false; note = 'excepción de la prueba: ' + e.message.split('\n')[0]; }
  const errs = p ? p.errs : []; if (errs.length) ok = false;
  results.push({ name, ok }); if (p) await p.close().catch(() => {});
  console.log((ok ? 'OK   ' : 'FALLA') + ' ' + name + (note ? ' — ' + note : '') + (errs.length ? '\n        ' + [...new Set(errs)].slice(0, 3).join('\n        ') : ''));
}

async function finger(p) {
  const c = await p.context().newCDPSession(p);
  const send = (type, pts) => c.send('Input.dispatchTouchEvent', { type, touchPoints: pts.map(([x, y]) => ({ x, y, id: 1 })) });
  return {
    tap: async (x, y) => { await send('touchStart', [[x, y]]); await p.waitForTimeout(40); await send('touchEnd', []); await p.waitForTimeout(250); },
    drag: async (x0, y0, x1, y1, steps = 12, ms = 220) => {
      await send('touchStart', [[x0, y0]]);
      for (let i = 1; i <= steps; i++) { await send('touchMove', [[x0 + (x1 - x0) * i / steps, y0 + (y1 - y0) * i / steps]]); await p.waitForTimeout(ms / steps); }
      await send('touchEnd', []); await p.waitForTimeout(400);
    }
  };
}
const center = (p, sel, k = 0) => ev(p, ([s, k]) => { const r = document.querySelectorAll(s)[k].getBoundingClientRect(); return [r.x + r.width / 2, r.y + r.height / 2]; }, [sel, k]);
const rowVal = (p, label) => ev(p, l => { const r = $$('.nsrow').find(e => ($('.nl', e) || {}).textContent === l); return r ? ($('.nv', r) || {}).textContent || '' : null; }, label);
const openDisp = async (p, label) => { await ev(p, async l => { await loadDisplay(); openView(settingsView()); topView().goto(l); }, label); await p.waitForTimeout(500); };

(async () => {
  browser = await chromium.launch();

  await run('Pantalla (consola): brillo con barra, resolución 1080p, 120 Hz y HDR con solo Desactivado/Automático', async () => {
    const p = await page();
    await openDisp(p, 'Brillo');
    const r = await ev(p, () => ({ sec: $$('.nsr h3, .nsr .nsh').map(e => e.textContent), labels: $$('.nsrow .nl').map(e => e.textContent), slide: !!$('.nsrow.nsslide .nqbar'), b0: sys.brightness }));
    await keys(p, ['right', 'right']);
    const b1 = await ev(p, () => sys.brightness);
    const res = await rowVal(p, 'Resolución'), hz = await rowVal(p, 'Frecuencia de actualización'), hdr = await rowVal(p, 'HDR');
    await ev(p, () => topView().goto('HDR')); await p.waitForTimeout(200); await keys(p, ['a']); await p.waitForTimeout(400);
    const opts = await ev(p, () => $$('.fi', topView().el).map(e => e.textContent.trim()));
    const ok = r.slide && ['Brillo', 'Resolución', 'Frecuencia de actualización', 'HDR'].every(l => r.labels.includes(l)) && b1 === Math.min(100, r.b0 + 10)
      && /1080p/.test(res) && /120 Hz/.test(hz) && hdr === 'Automático' && opts.length === 2 && /Desactivado/.test(opts[0]) && /Automático/.test(opts[1]);
    return ok ? { p } : { p, fail: JSON.stringify({ r, b1, res, hz, hdr, opts }) };
  });

  await run('Resolución: elegir 720p pregunta «¿Mantener?»; con B regresa; con Mantener se queda', async () => {
    const p = await page();
    await openDisp(p, 'Resolución'); await keys(p, ['a']); await p.waitForTimeout(400);
    await keys(p, ['down', 'down', 'a']); await p.waitForTimeout(600);
    const d1 = await ev(p, () => ({ keep: !!(topView() && topView().isKeep), txt: topView() && topView().el.textContent, st: window.__qa.disp.h }));
    await keys(p, ['b']); await p.waitForTimeout(600);
    const back = { rev: (await calls(p, 'display.revert')).length, h: await ev(p, () => window.__qa.disp.h), val: await rowVal(p, 'Resolución') };
    await keys(p, ['a']); await p.waitForTimeout(400); await keys(p, ['down', 'a']); await p.waitForTimeout(600);
    const d2 = await ev(p, () => !!(topView() && topView().isKeep));
    await ev(p, () => { const b = $$('.dbtns .fi', topView().el).find(e => e.textContent === 'Mantener'); b.click(); }); await p.waitForTimeout(600);
    const kept = { keep: (await calls(p, 'display.keep')).length, set: await calls(p, 'display.set'), val: await rowVal(p, 'Resolución') };
    const ok = d1.keep && /¿Mantener 720p/.test(d1.txt) && /1080p en 15 s/.test(d1.txt) && d1.st === 720 && back.rev === 1 && back.h === 1080 && /1080p/.test(back.val)
      && d2 && kept.keep === 1 && kept.set.length === 2 && kept.set[1].h === 900 && /900p/.test(kept.val);
    return ok ? { p } : { p, fail: JSON.stringify({ d1, back, d2, kept }) };
  });

  await run('Si no eliges nada, «¿Mantener?» cuenta 15 s y regresa sola', async () => {
    const p = await page();
    await openDisp(p, 'Frecuencia de actualización'); await keys(p, ['a']); await p.waitForTimeout(400); await keys(p, ['down', 'a']); await p.waitForTimeout(1300);
    const a = await ev(p, () => ({ keep: !!(topView() && topView().isKeep), n: +$('.kc', topView().el).textContent, hz: window.__qa.disp.hz }));
    await p.waitForTimeout(14500);
    const b = await ev(p, () => ({ keep: !!views.find(v => v.isKeep), hz: window.__qa.disp.hz }));
    const rev = (await calls(p, 'display.revert')).length;
    return a.keep && a.n <= 14 && a.n >= 12 && a.hz === 60 && !b.keep && b.hz === 120 && rev === 1 ? { p } : { p, fail: JSON.stringify({ a, b, rev }) };
  });

  await run('Con una tele 4K con HDR: su sección, hasta 2160p, HDR con «Activado», tamaño de la interfaz y margen', async () => {
    const p = await page();
    await ev(p, () => { window.__qa.disp = { ok: true, name: "LG OLED", external: true, w: 3840, h: 2160, hz: 60, maxH: 2160, resolutions: [{ w: 3840, h: 2160 }, { w: 2560, h: 1440 }, { w: 1920, h: 1080 }, { w: 1280, h: 720 }], rates: [120, 60], hdr: { supported: true, on: true, mode: "auto" } }; });
    await openDisp(p, 'HDR');
    const r = await ev(p, () => ({ txt: $('.nsr').textContent, labels: $$('.nsrow .nl').map(e => e.textContent), slide: !!$('.nsrow.nsslide') }));
    const res = await rowVal(p, 'Resolución');
    await keys(p, ['a']); await p.waitForTimeout(400);
    const opts = await ev(p, () => $$('.fi', topView().el).map(e => e.textContent.trim()));
    await keys(p, ['down', 'down', 'a']); await p.waitForTimeout(500);
    const hdr = { calls: await calls(p, 'display.hdr'), val: await rowVal(p, 'HDR') };
    await ev(p, () => topView().goto('Tamaño de la interfaz')); await keys(p, ['a']); await p.waitForTimeout(300); await keys(p, ['down', 'down', 'a']); await p.waitForTimeout(400);
    const size = await ev(p, () => ({ s: settings.tvScale, v: document.documentElement.style.getPropertyValue('--tvscale') }));
    const ok = /Pantalla externa · LG OLED/.test(r.txt) && /Apagada mientras usas la pantalla externa/.test(r.txt) && !r.slide && r.labels.includes('Margen para TV')
      && /2160p \(4K\)/.test(res) && opts.length === 3 && /Activado/.test(opts[2]) && hdr.calls.length === 1 && hdr.calls[0].mode === 'on' && hdr.val === 'Activado'
      && size.s === 125 && size.v === '1';   // sin la clase «tv» en la prueba, la escala no se aplica
    return ok ? { p } : { p, fail: JSON.stringify({ r, res, opts, hdr, size }) };
  });

  await run('Táctil: la barra de brillo se arrastra con el dedo', async () => {
    const p = await page(); const f = await finger(p);
    await openDisp(p, 'Brillo');
    const box = await ev(p, () => { const r = $('.nsrow.nsslide .nqbar').getBoundingClientRect(); return [r.left, r.top + r.height / 2, r.width]; });
    await f.drag(box[0] + box[2] * .5, box[1], box[0] + box[2] * .2, box[1]);
    const b = await ev(p, () => sys.brightness);
    const calls1 = (await calls(p, 'system.brightness')).length;
    return b >= 15 && b <= 25 && calls1 > 0 ? { p, note: `brillo ${b}` } : { p, fail: JSON.stringify({ b, calls1 }) };
  });

  await run('Táctil: las ayudas ◀ ▶ se tocan (margen para TV) y Listo guarda', async () => {
    const p = await page(); const f = await finger(p);
    await ev(p, () => { openView(marginView()); }); await p.waitForTimeout(500);
    const glyphs = await ev(p, () => $$('#hints .hint.pair .glyph').map(e => e.dataset.key));
    const [x, y] = await center(p, '#hints .hint.pair .glyph', 0);
    await f.tap(x, y); await f.tap(x, y);
    const m1 = await ev(p, () => settings.tvMargin);
    const [x2, y2] = await center(p, '.tvb', 1); await f.tap(x2, y2);
    const m2 = await ev(p, () => settings.tvMargin);
    const [x3, y3] = await center(p, '.tvok'); await f.tap(x3, y3);
    const closed = await ev(p, () => !views.length);
    return glyphs.join() === 'left,right' && m1 === 98 && m2 === 99 && closed ? { p } : { p, fail: JSON.stringify({ glyphs, m1, m2, closed }) };
  });

  await run('Táctil: en el visor del Álbum se desliza a los lados para cambiar de captura, y ◀ ▶ se tocan', async () => {
    const p = await page(); const f = await finger(p);
    await ev(p, () => { closeAllViews(); window.__qa.album = albumData = [1, 2, 3].map(i => ({ url: shotSvg(games[0], i), path: "x" + i, game: "A", date: "hoy", type: "image" })); openView(albumView()); });
    await p.waitForTimeout(900); await keys(p, ['a']); await p.waitForTimeout(600);
    const i0 = await ev(p, () => [topView().cls, topView().idx]);
    // primero el toque (Chrome se come el toque que llega justo después de un deslizón rápido, como en un teléfono)
    const hk = await ev(p, () => $$('#hints [data-key]').map(e => e.dataset.key));
    const k = hk.indexOf('right'); const [x, y] = await center(p, '#hints [data-key]', k); await f.tap(x, y);
    const i1 = await ev(p, () => topView().idx);
    await f.drag(900, 400, 300, 410);
    const i2 = await ev(p, () => topView().idx);
    await p.waitForTimeout(800); await f.drag(300, 400, 900, 400);
    const i3 = await ev(p, () => topView().idx);
    return i0[0] === 'viewer' && i0[1] === 0 && hk.includes('left') && i1 === 1 && i2 === 2 && i3 === 1 ? { p } : { p, fail: JSON.stringify({ i0, hk, i1, i2, i3 }) };
  });

  await run('Táctil: en Recortar video se arrastran el inicio y el fin sobre la línea', async () => {
    const p = await page(); const f = await finger(p);
    await ev(p, () => { closeAllViews(); openView(trimView({ url: "", game: "A", path: "v" }, () => {})); });
    await p.waitForTimeout(500);
    await ev(p, () => { const t = topView(), v = t.v; Object.defineProperty(v, 'duration', { value: 20 }); t.a = 0; t.b = 20; t.paint(); });
    const box = await ev(p, () => { const r = $('.tline').getBoundingClientRect(); return [r.left, r.top + r.height / 2, r.width]; });
    await f.drag(box[0] + 2, box[1], box[0] + box[2] * .25, box[1]);
    const a = await ev(p, () => [topView().a, topView().b, topView().h]);
    await f.drag(box[0] + box[2] - 2, box[1], box[0] + box[2] * .75, box[1]);
    const b = await ev(p, () => [topView().a, topView().b, topView().h]);
    const ok = Math.abs(a[0] - 5) < .6 && a[1] === 20 && a[2] === 0 && Math.abs(b[0] - 5) < .6 && Math.abs(b[1] - 15) < .6 && b[2] === 1;
    return ok ? { p } : { p, fail: JSON.stringify({ a, b }) };
  });

  await run('Táctil: en Probar botones, tocar «Atrás» sí sale (B con el mando es un botón más)', async () => {
    const p = await page(); const f = await finger(p);
    await ev(p, () => { openView(testerView(state.pads[0] || { key: "ally", name: "ROG Xbox Ally X", type: "Integrado", layout: "xbox" })); }); await p.waitForTimeout(600);
    const pre = await ev(p, () => views.length);
    await keys(p, ['b']); const still = await ev(p, () => views.length);
    const [x, y] = await center(p, '#hints [data-key="b"]'); await f.tap(x, y);
    const post = await ev(p, () => views.length);
    return pre === 1 && still === 1 && post === 0 ? { p } : { p, fail: JSON.stringify({ pre, still, post }) };
  });

  await browser.close();
  const bad = results.filter(r => !r.ok).length;
  console.log(`\n${results.length - bad}/${results.length} escenarios bien`);
  process.exit(bad ? 1 : 0);
})();
