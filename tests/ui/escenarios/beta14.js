// Pruebas de la beta 1.1: juegos externos, nube de Steam, Álbum que se relee, menú transparente, teclado con mayúsculas.
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

// Beta 1.4: barra con Steam y Armoury Crate, y la pantalla táctil (toques, deslizar, orilla izquierda, toque largo).
async function finger(p) {
  const c = await p.context().newCDPSession(p);
  const send = (type, pts) => c.send('Input.dispatchTouchEvent', { type, touchPoints: pts.map(([x, y]) => ({ x, y, id: 1 })) });
  return {
    tap: async (x, y) => { await send('touchStart', [[x, y]]); await p.waitForTimeout(40); await send('touchEnd', []); await p.waitForTimeout(250); },
    hold: async (x, y, ms = 700) => { await send('touchStart', [[x, y]]); await p.waitForTimeout(ms); await send('touchEnd', []); await p.waitForTimeout(300); },
    drag: async (x0, y0, x1, y1, steps = 12, ms = 220) => {
      await send('touchStart', [[x0, y0]]);
      for (let i = 1; i <= steps; i++) { await send('touchMove', [[x0 + (x1 - x0) * i / steps, y0 + (y1 - y0) * i / steps]]); await p.waitForTimeout(ms / steps); }
      await send('touchEnd', []); await p.waitForTimeout(400);
    }
  };
}
const center = (p, sel, k = 0) => ev(p, ([s, k]) => { const r = document.querySelectorAll(s)[k].getBoundingClientRect(); return [r.x + r.width / 2, r.y + r.height / 2]; }, [sel, k]);

(async () => {
  browser = await chromium.launch();

  await run('Táctil: el primer toque elige el juego y el segundo lo abre (Inicio)', async () => {
    const p = await page(); const f = await finger(p);
    const [x, y] = await center(p, '#track .tile', 2);
    await f.tap(x, y);
    const a = await ev(p, () => [home.zone, home.g, mode]);
    const n1 = (await calls(p, 'game.launch')).length;
    await f.tap(x, y); await p.waitForTimeout(900);
    const n2 = (await calls(p, 'game.launch')).length;
    return a[0] === 'games' && a[1] === 2 && n1 === 0 && n2 === 1 ? { p } : { p, fail: JSON.stringify({ a, n1, n2 }) };
  });

  await run('Táctil: deslizar la fila del Inicio elige otro juego sin abrir nada', async () => {
    const p = await page(); const f = await finger(p);
    const [x, y] = await center(p, '#track .tile', 3);
    await f.drag(x, y, x - 700, y + 10);
    const r = await ev(p, () => [home.g, home.offset > 0, $('#track').classList.contains('dragging')]);
    const n = (await calls(p, 'game.launch')).length;
    return r[0] >= 2 && r[1] && !r[2] && n === 0 ? { p } : { p, fail: JSON.stringify({ r, n }) };
  });

  await run('Táctil: las ayudas de abajo se tocan (B Atrás cierra Ajustes; A en la lista de la biblioteca)', async () => {
    const p = await page(); const f = await finger(p);
    await ev(p, () => openView(settingsView())); await p.waitForTimeout(600);
    const keys0 = await ev(p, () => $$('#hints .hint').map(h => h.dataset.key || '-').join());
    const [x, y] = await center(p, '#hints .hint[data-key="b"]');
    await f.tap(x, y); await p.waitForTimeout(500);
    const closed = await ev(p, () => views.length === 0);
    return keys0 === 'b,a' && closed ? { p } : { p, fail: JSON.stringify({ keys0, closed }) };
  });

  await run('Táctil: deslizar desde la orilla izquierda regresa (con flechita); un tirón corto no', async () => {
    const p = await page(); const f = await finger(p);
    await ev(p, () => openView(settingsView())); await p.waitForTimeout(600);
    await f.drag(4, 400, 40, 404);
    const still = await ev(p, () => views.length);
    await f.drag(4, 400, 240, 410);
    const after = await ev(p, () => [views.length, $('#edgeback') && $('#edgeback').className]);
    return still === 1 && after[0] === 0 && after[1] === '' ? { p } : { p, fail: JSON.stringify({ still, after }) };
  });

  await run('Táctil: toque largo en un juego abre sus opciones; en el Álbum empieza a elegir', async () => {
    const p = await page(); const f = await finger(p);
    const [x, y] = await center(p, '#track .tile', 1);
    await f.hold(x, y);
    const props = await ev(p, () => [!!topView(), topView() && $('.vhead', topView().el).textContent, home.g]);
    const n = (await calls(p, 'game.launch')).length;
    await ev(p, () => { closeAllViews(); window.__qa.album = albumData = [1, 2, 3].map(i => ({ url: shotSvg(games[0], i), path: "x" + i, game: "A", date: "hoy", type: "image" })); openView(albumView()); });
    await p.waitForTimeout(900);
    const [ax, ay] = await center(p, '.shot', 1);
    await f.hold(ax, ay);
    const sel = await ev(p, () => [topView().sel && [...topView().sel].join(), topView().i]);
    return props[0] && /ELDEN RING/.test(props[1]) && props[2] === 1 && n === 0 && sel[0] === 'x2' && sel[1] === 1 ? { p } : { p, fail: JSON.stringify({ props, n, sel }) };
  });

  await run('Táctil: biblioteca con dos toques y pestañas tocables', async () => {
    const p = await page(); const f = await finger(p);
    await ev(p, () => openView(libraryView())); await p.waitForTimeout(800);
    const [x, y] = await center(p, '.grid .tile', 4);
    await f.tap(x, y);
    const i = await ev(p, () => topView().i), n1 = (await calls(p, 'game.launch')).length;
    const [tx, ty] = await center(p, '.tabs .tab', 1);
    await f.tap(tx, ty); await p.waitForTimeout(300);
    const col = await ev(p, () => lib.col);
    return i === 4 && n1 === 0 && col === 1 ? { p } : { p, fail: JSON.stringify({ i, n1, col }) };
  });

  await run('Táctil: en el menú rápido, tocar «B Atrás» lo cierra', async () => {
    const p = await page(); const f = await finger(p);
    await ev(p, () => openQuick()); await p.waitForTimeout(700);
    const [x, y] = await center(p, '#quick .hint[data-key="b"]');
    await f.tap(x, y); await p.waitForTimeout(500);
    return !(await ev(p, () => QM.open)) ? { p } : { p, fail: 'sigue abierto' };
  });

  await run('Mouse: un click en el juego ya elegido lo abre', async () => {
    const p = await page();
    await ev(p, () => { home.zone = "games"; home.g = 0; updateFocus(); });
    await p.click('#track .tile >> nth=0'); await p.waitForTimeout(900);
    return (await calls(p, 'game.launch')).length === 1 ? { p } : { p, fail: 'no abrió' };
  });

  await browser.close();
  const bad = results.filter(r => !r.ok);
  console.log(`\n${results.length - bad.length}/${results.length} escenarios OK`);
})();
