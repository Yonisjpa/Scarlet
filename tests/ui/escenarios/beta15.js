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

// Beta 1.5: la pantalla del juego (fondo y logo, sin textos) y ocultar juegos de verdad.
(async () => {
  browser = await chromium.launch();

  await run('Pantalla del juego: sin textos de la demo; logo de SteamGridDB o, si no hay, el nombre', async () => {
    const p = await page();
    await ev(p, () => { showGame(byId(1245620)); }); await p.waitForTimeout(500);
    const noLogo = await ev(p, () => [$('#game').textContent.trim(), !!$('#glogo img')]);
    await ev(p, () => { window.__qa.sgdb = true; window.__qa.cacheOk = true; delete Art.cache['logo:1245620']; showGame(byId(1245620)); }); await p.waitForTimeout(800);
    const withLogo = await ev(p, () => [!!$('#glogo img'), /logos 1/.test(decodeURIComponent($('#glogo img') ? $('#glogo img').src : '')), /simulación|Doble toque/.test($('#game').textContent)]);
    await ev(p, () => showGame(SYS.steam)); await p.waitForTimeout(300);
    const sys = await ev(p, () => [$('#glogo').textContent, !!$('#glogo img')]);
    return noLogo[0] === 'ELDEN RING' && !noLogo[1] && withLogo[0] && withLogo[1] && !withLogo[2] && sys[0] === 'Steam' && !sys[1] ? { p } : { p, fail: JSON.stringify({ noLogo, withLogo, sys }) };
  });

  await run('Opciones del juego: Fondo y Logo; elegir un logo y un fondo se ve en la pantalla del juego', async () => {
    const p = await page();
    await ev(p, () => { window.__qa.sgdb = true; window.__qa.cacheOk = true; openView(propsView(byId(3489700))); });
    await p.waitForTimeout(500);
    const rows = await ev(p, () => topView().s.map(s => s.k).join());
    await ev(p, () => { const v = topView(); v.i = v.s.findIndex(s => s.k === "logo"); updateFocus(); });
    await keys(p, ['a'], 900);
    const picker = await ev(p, () => [topView().cls, $$('.tile .cap', topView().el).map(c => c.textContent).join('|')]);
    await ev(p, () => { const v = topView(); v.i = v.all.findIndex(o => /logos 2/.test(decodeURIComponent(o.url))); updateFocus(); });
    await keys(p, ['a'], 600);
    const lp = await ev(p, () => /logos 2/.test(decodeURIComponent(Art.logoPick[3489700] || '')));
    await ev(p, () => { const v = topView(); v.i = v.s.findIndex(s => s.k === "hero"); updateFocus(); });
    await keys(p, ['a'], 900);
    const hcaps = await ev(p, () => $$('.tile .cap', topView().el).map(c => c.textContent).join('|'));
    await ev(p, () => { const v = topView(); v.i = v.all.findIndex(o => o.url === "file"); updateFocus(); });
    await keys(p, ['a'], 700);
    await keys(p, ['a', 'down', 'a'], 500);   // explorador: Escritorio → (Emus) → p.png
    await p.waitForTimeout(500);
    const hp = await ev(p, () => Art.heroPick[3489700] || '');
    await ev(p, () => { closeAllViews(); showGame(byId(3489700)); }); await p.waitForTimeout(900);
    // (la imagen de la PC se sirve desde la app: en esta prueba no carga, así que se revisa lo que pide la pantalla)
    const shown = await ev(p, async () => [decodeURIComponent($('#glogo img') ? $('#glogo img').src : ''), await Art.get(byId(3489700), "hero")]);
    const ok = /hero,logo/.test(rows) && /apick-logo/.test(picker[0]) && /Sin logo/.test(picker[1]) && lp && /Imagen de tu PC/.test(hcaps) && /p\.png/.test(decodeURIComponent(hp)) && /logos 2/.test(shown[0]) && /p\.png/.test(decodeURIComponent(shown[1]));
    return ok ? { p } : { p, fail: JSON.stringify({ rows, picker, lp, hcaps, hp, shown }) };
  });

  await run('Ocultar de verdad: no sale en el Inicio, la Biblioteca (sin pestaña Ocultos), la búsqueda ni Logros; se recupera en Ajustes', async () => {
    const p = await page();
    await ev(p, () => { openView(propsView(byId(1245620))); const v = topView(); v.i = v.s.findIndex(s => s.k === "hide"); updateFocus(); });
    await keys(p, ['a'], 500);
    await ev(p, () => closeAllViews());
    const r = await ev(p, () => {
      lib.col = 0; lib.query = "";
      const home = homeGames().some(g => g.appid === 1245620), cols = collections().join(), lib1 = libraryList().some(g => g.appid === 1245620), q = libraryList("elden").length;
      openView(achHubView()); const ach = topView().el.textContent.includes("ELDEN RING"); closeAllViews();
      return [home, /Ocultos/.test(cols), lib1, q, ach];
    });
    await ev(p, () => { openView(settingsView()); topView().goto("Juegos ocultos"); });
    const val = await ev(p, () => $('.nsrow.focused .nv').textContent);
    await keys(p, ['a'], 600);
    const listed = await ev(p, () => $$('.lrow b', topView().el).map(b => b.textContent).join());
    await keys(p, ['a'], 500);
    const back = await ev(p, () => [isHidden(byId(1245620)), $('.vbody', topView().el).textContent.includes('No tienes juegos ocultos')]);
    await keys(p, ['b'], 500);
    const val2 = await ev(p, () => $('.nsrow.focused .nv').textContent);
    const ok = !r[0] && !r[1] && !r[2] && r[3] === 0 && !r[4] && val === '1' && listed === 'ELDEN RING' && !back[0] && back[1] && val2 === 'Ninguno';
    return ok ? { p } : { p, fail: JSON.stringify({ r, val, listed, back, val2 }) };
  });

  await browser.close();
  const bad = results.filter(r => !r.ok);
  console.log(`\n${results.length - bad.length}/${results.length} escenarios OK`);
})();
