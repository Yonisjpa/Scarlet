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

(async () => {
  browser = await chromium.launch();

  await run('Juego externo: agregar con el explorador del mando (entrar a carpeta, subir con B, elegir)', async () => {
    const p = await page();
    await ev(p, () => { openView(settingsView()); topView().goto("Agregar un juego"); });
    await keys(p, ['a', 'a'], 400);                     // explorador → Escritorio
    await keys(p, ['a'], 400);                          // Emus
    const inside = await ev(p, () => topView().path);
    await keys(p, ['b'], 400);                          // sube y el foco queda en Emus
    const back = await ev(p, () => [topView().path, topView().items()[topView().i].textContent]);
    await keys(p, ['a', 'a'], 900);                     // Emus → Ryujinx.exe
    const r = await ev(p, () => [topView().el.querySelector('.vhead').textContent, games.filter(g => g.source === 'ext').map(g => g.name).join()]);
    return inside === 'C:\\D\\Emus' && back[0] === 'C:\\D' && /Emus/.test(back[1]) && /Ryujinx/.test(r[0]) && r[1] === 'Ryujinx' ? { p } : { p, fail: JSON.stringify({ inside, back, r }) };
  });

  await run('Juego externo: cambiar nombre (con mayúsculas), parámetros, portada, abrir y quitar', async () => {
    const p = await page();
    await ev(p, () => window.__qa.ext = [{ appid: 2000000001, name: "ryujinx", source: "ext", kind: "game", installed: true, hours: 0, last: "nunca", lastTs: 0, ach: [0, 0], cols: ["Externos"], size: 0, path: "C:\\D\\Emus\\Ryujinx.exe", args: "" }]);
    await ev(p, () => reloadLibrary()); await p.waitForTimeout(500);
    await ev(p, () => { openView(propsView(byId(2000000001))); const v = topView(); v.i = v.s.findIndex(s => s.k === "rename"); updateFocus(); });
    await keys(p, ['a'], 400);
    await ev(p, () => { const v = topView(); v.text = ""; v.upd(); });
    for (const ch of "zelda totk") await ev(p, ch => ch === " " ? topView().type(" ") : topView().type(ch), ch);
    await keys(p, ['start'], 500);
    const name = await ev(p, () => byId(2000000001).name);
    await ev(p, () => { const v = topView(); v.i = v.s.findIndex(s => s.k === "args"); updateFocus(); });
    await keys(p, ['a'], 400);
    for (const ch of "-f") await ev(p, ch => topView().type(ch), ch);
    await keys(p, ['start'], 500);
    const args = (await calls(p, 'ext.update')).map(x => x.args).filter(Boolean).pop();
    await ev(p, () => { const v = topView(); v.i = v.s.findIndex(s => s.k === "extart"); updateFocus(); });
    await keys(p, ['a'], 500);
    await ev(p, () => { const v = topView(); v.i = v.items().findIndex(b => /Ícono del programa/.test(b.textContent)); updateFocus(); });
    await keys(p, ['a'], 600);
    const art = await ev(p, () => [!!byId(2000000001).art, byId(2000000001).iconArt]);
    await ev(p, () => closeAllViews());
    await ev(p, () => launchGame(byId(2000000001))); await p.waitForTimeout(1500);
    const launch = (await calls(p, 'game.launch')).pop();
    await ev(p, () => { goHome(); }); await p.waitForTimeout(600);
    await ev(p, () => { closeRunning(true); closeAllViews(); openView(propsView(byId(2000000001))); const v = topView(); v.i = v.s.findIndex(s => s.k === "remove"); updateFocus(); });
    await p.waitForTimeout(300);
    await keys(p, ['a'], 400);
    await ev(p, () => { const v = topView(); v.i = 1; updateFocus(); }); await keys(p, ['a'], 600);
    const gone = await ev(p, () => !byId(2000000001));
    const ok = name === 'Zelda Totk' && args === '-f' && art[0] && art[1] && launch && launch.source === 'ext' && gone;
    return ok ? { p } : { p, fail: JSON.stringify({ name, args, art, launch, gone }) };
  });

  await run('Nube de Steam: barra naranja con %, sin % va y viene, y aviso al terminar', async () => {
    const p = await page();
    await ev(p, () => window.__emit("cloud.progress", { appid: 1245620, pct: 40 })); await p.waitForTimeout(300);
    const a = await ev(p, () => { const el = $('[data-cl="1245620"]'); return el && [getComputedStyle(el.querySelector('i')).backgroundColor, el.querySelector('i').style.width, el.classList.contains('indet')]; });
    await ev(p, () => window.__emit("cloud.progress", { appid: 1245620, pct: -1 })); await p.waitForTimeout(300);
    const b = await ev(p, () => $('[data-cl="1245620"]').classList.contains('indet'));
    await ev(p, () => window.__emit("cloud.done", { appid: 1245620, ok: true })); await p.waitForTimeout(400);
    const c = await ev(p, () => [!$('[data-cl="1245620"]'), notifCur && notifCur.title]);
    return a && a[0] === 'rgb(240, 138, 36)' && a[1] === '40%' && !a[2] && b && c[0] && c[1] === 'Partida guardada en la nube' ? { p } : { p, fail: JSON.stringify({ a, b, c }) };
  });

  await run('Álbum: se vuelve a leer al abrirlo y con RB', async () => {
    const p = await page();
    await ev(p, () => { albumData = [{ url: shotSvg(games[0], 1), path: "x1", game: "A", date: "hoy", type: "image" }]; openView(albumView()); }); await p.waitForTimeout(700);
    const n1 = (await calls(p, 'album.list')).length;
    await keys(p, ['rb'], 700);
    const n2 = (await calls(p, 'album.list')).length;
    return n1 >= 1 && n2 === n1 + 1 ? { p } : { p, fail: JSON.stringify({ n1, n2 }) };
  });

  await run('Menú rápido: fondo al 50 % (también con la carátula), textos sólidos; se puede apagar', async () => {
    const p = await page();
    await ev(p, () => openQuick()); await p.waitForTimeout(800);
    const on = await ev(p, () => { const q = $('#quick'), b = getComputedStyle(q, '::before'); return [document.documentElement.classList.contains('qglass'), b.opacity, getComputedStyle(q).backgroundColor, getComputedStyle($('#quick .nqit')).opacity]; });
    await ev(p, () => { closeQuick(true); openView(settingsView()); topView().goto("Fondo transparente"); });
    await keys(p, ['a'], 300);
    const off = await ev(p, () => [document.documentElement.classList.contains('qglass'), settings.quickGlass]);
    const sent = (await calls(p, 'quick.glass')).pop();
    return on[0] && on[1] === '0.5' && on[2] === 'rgba(0, 0, 0, 0)' && on[3] === '1' && !off[0] && off[1] === false && sent && sent.on === false ? { p } : { p, fail: JSON.stringify({ on, off, sent }) };
  });

  await run('Encima del juego: la página no pinta fondo (lo pone la app) y manda los colores de la carátula', async () => {
    const p = await page({ q: '?overlay=quick', wait: 1500, vp: { width: 712, height: 1080 } });
    await ev(p, () => { window.__emit("quick.glass", true); window.__qa.media = { app: "Apple Music", title: "C", artist: "A", playing: true, position: 1, duration: 100, now: Date.now() }; window.__emit("quick.open"); });
    await p.waitForTimeout(800);
    const r = await ev(p, () => [getComputedStyle($('#quick'), '::before').display, document.documentElement.classList.contains('hostglass')]);
    await ev(p, () => { const svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10" height="10" fill="#c4161c"/></svg>'; setMedia({ app: "Apple Music", title: "X", artist: "Y", art: "data:image/svg+xml;base64," + btoa(svg), playing: true, position: 1, duration: 100, at: Date.now() }); });
    await p.waitForTimeout(800);
    const tint = (await calls(p, 'quick.tint')).filter(x => x.colors).pop();
    return r[0] === 'none' && r[1] && tint && tint.colors.length === 3 ? { p } : { p, fail: JSON.stringify({ r, tint }) };
  });

  await browser.close();
  const bad = results.filter(r => !r.ok);
  console.log(`\n${results.length - bad.length}/${results.length} escenarios OK`);
})();
