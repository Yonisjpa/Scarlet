// Pruebas de la beta 1.7: Lúmina (estilo de cristal), su cursor que fluye con refracción y las notificaciones con bloque de color.
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
const lumiOn = async p => { await ev(p, () => { settings.lumina = true; Lumina.apply(); }); await p.waitForTimeout(1500); };
const boxes = p => ev(p, () => { const a = $('#lumi').getBoundingClientRect(), f = [...$$('.focused')].pop().getBoundingClientRect(); return { l: [a.left, a.top, a.right, a.bottom], f: [f.left, f.top, f.right, f.bottom], op: getComputedStyle($('#lumi')).opacity, g: rem() * .68 }; });
const hugs = b => b.op === '1' && [0, 1].every(i => Math.abs(b.f[i] - b.g - b.l[i]) < 3) && [2, 3].every(i => Math.abs(b.f[i] + b.g - b.l[i]) < 3);

(async () => {
  browser = await chromium.launch();

  await run('Apagado (como viene): ni estilos ni cursor de cristal; el anillo y las notificaciones de siempre', async () => {
    const p = await page();
    const r = await ev(p, () => {
      notifQ.length = 0; notifCur = null; notifyBattery(.08);
      const n = $('#notifs .notif');
      return { attr: document.documentElement.hasAttribute('data-lumina'), lens: document.documentElement.dataset.lens || '', lumi: !!$('#lumi'),
        ring: getComputedStyle($('.focused'), '::after').opacity, cls: n.className, pct: getComputedStyle($('.npct', n) || document.body).display, hasPct: !!$('.npct', n), txt: n.textContent,
        radius: getComputedStyle(n).borderTopLeftRadius, bg: getComputedStyle(document.body).backgroundImage };
    });
    const ok = !r.attr && !r.lens && !r.lumi && r.ring === '1' && r.cls === 'notif' && !r.hasPct && /Batería baja/.test(r.txt) && /Queda 8 %/.test(r.txt) && r.bg === 'none';
    return ok ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Ajustes › Temas › Lúmina: se enciende, se guarda, aparece «Refracción del cursor» y al apagar todo vuelve', async () => {
    const p = await page();
    await ev(p, () => { openView(settingsView()); topView().goto("Lúmina"); }); await p.waitForTimeout(400);
    const before = await ev(p, () => ({ row: !!$$('.nsrow .nl').find(e => e.textContent === 'Lúmina'), refr: !!$$('.nsrow .nl').find(e => e.textContent === 'Refracción del cursor') }));
    await keys(p, ['a']); await p.waitForTimeout(1500);
    const on = await ev(p, () => ({ attr: document.documentElement.hasAttribute('data-lumina'), saved: store.get("settings", {}).lumina, refr: !!$$('.nsrow .nl').find(e => e.textContent === 'Refracción del cursor'),
      lens: document.documentElement.dataset.lens, panel: getComputedStyle($('.nsl')).backdropFilter, focus: ($('.focused .nl') || {}).textContent }));
    const b = await boxes(p);
    await keys(p, ['a']); await p.waitForTimeout(600);
    const off = await ev(p, () => ({ attr: document.documentElement.hasAttribute('data-lumina'), saved: store.get("settings", {}).lumina, lens: document.documentElement.dataset.lens || '', op: getComputedStyle($('#lumi')).display,
      ring: getComputedStyle($('.focused'), '::after').opacity, panel: getComputedStyle($('.nsl')).backdropFilter }));
    const ok = before.row && !before.refr && on.attr && on.saved === true && on.refr && /refract|on/.test(on.lens) && /blur/.test(on.panel) && on.focus === 'Lúmina' && hugs(b)
      && !off.attr && off.saved === false && !off.lens && off.op === 'none' && off.ring === '1' && off.panel === 'none';
    return ok ? { p } : { p, fail: JSON.stringify({ before, on, b, off }) };
  });

  await run('El cursor de cristal viaja de un juego a otro: se estira a medio camino y termina abrazando la portada', async () => {
    const p = await page({ vp: { width: 1920, height: 1080 } }); await lumiOn(p);
    const a = await boxes(p);
    await keys(p, ['right'], 0); await p.waitForTimeout(90);
    const mid = await boxes(p);
    await p.waitForTimeout(1400);
    const end = await boxes(p);
    const ring = await ev(p, () => getComputedStyle($('.focused'), '::after').opacity);
    const wMid = mid.l[2] - mid.l[0], wEnd = end.l[2] - end.l[0];
    const ok = hugs(a) && hugs(end) && end.l[0] > a.l[0] + 100 && wMid > wEnd * 1.12 && mid.l[0] < end.l[0] - 20 && ring === '0';
    return ok ? { p, note: `ancho a medio camino ${Math.round(wMid)} px, al llegar ${Math.round(wEnd)} px` } : { p, fail: JSON.stringify({ a, mid, end, ring }) };
  });

  await run('El cursor sigue al foco en la barra de iconos, en Ajustes, en un diálogo y en el menú rápido', async () => {
    const p = await page({ vp: { width: 1920, height: 1080 } }); await lumiOn(p);
    const out = {};
    await keys(p, ['down', 'right', 'right']); await p.waitForTimeout(1400); out.barra = hugs(await boxes(p));
    await ev(p, () => { openView(settingsView()); }); await p.waitForTimeout(900); await keys(p, ['down', 'down']); await p.waitForTimeout(1400); out.ajustes = hugs(await boxes(p));
    await ev(p, () => { closeAllViews(); confirmDialog("¿Seguro?", "Sí"); }); await p.waitForTimeout(1500); out.dialogo = hugs(await boxes(p));
    await ev(p, () => { closeAllViews(); openQuick(); }); await p.waitForTimeout(1500); await keys(p, ['down']); await p.waitForTimeout(1400); out.rapido = hugs(await boxes(p));
    const z = await ev(p, () => +getComputedStyle($('#lumi')).zIndex > +getComputedStyle($('#quick')).zIndex && +getComputedStyle($('#lumi')).zIndex < +getComputedStyle($('#notifs')).zIndex);
    return Object.values(out).every(Boolean) && z ? { p } : { p, fail: JSON.stringify({ out, z }) };
  });

  await run('Refracción: el cursor usa un filtro con mapa de desplazamiento; al apagarla queda el cursor sin lente', async () => {
    const p = await page({ vp: { width: 1920, height: 1080 } }); await lumiOn(p);
    const a = await ev(p, () => ({ lens: document.documentElement.dataset.lens, bf: getComputedStyle($('#lumi')).backdropFilter, href: ($('#lumi-f feImage').getAttribute('href') || '').slice(0, 22),
      scale: +$('#lumi-f feDisplacementMap').getAttribute('scale'), w: +$('#lumi-f feImage').getAttribute('width'), lw: $('#lumi').getBoundingClientRect().width }));
    await ev(p, () => { settings.luminaRefract = false; Lumina.apply(); }); await p.waitForTimeout(600);
    const b = await ev(p, () => ({ lens: document.documentElement.dataset.lens, bf: getComputedStyle($('#lumi')).backdropFilter }));
    const ok = a.lens === 'refract' && /url\(/.test(a.bf) && a.href === 'data:image/png;base64,' && a.scale > 10 && Math.abs(a.w - a.lw) < 2 && b.lens === 'on' && !/url\(/.test(b.bf);
    return ok ? { p } : { p, fail: JSON.stringify({ a, b }) };
  });

  await run('Notificaciones con Lúmina: bloque de color a la izquierda; la batería dice el porcentaje en grande y cambia de color', async () => {
    const p = await page({ vp: { width: 1920, height: 1080 } }); await lumiOn(p);
    const one = lv => ev(p, lv => { $('#notifs').innerHTML = ''; notifQ.length = 0; if (notifCur) clearTimeout(notifCur.t); notifCur = null; notifyBattery(lv);
      const n = $('#notifs .notif'), ic = $('.ic', n).getBoundingClientRect(), nb = n.getBoundingClientRect();
      return { cls: n.className, pct: $('.npct', n).textContent, tag: $('.ntag', n).textContent, t: $('b', n).textContent, lvl: $('.nlvl i', n).style.width, icH: Math.round(ic.height), nH: Math.round(nb.height), icL: Math.round(ic.left - nb.left),
        icBg: getComputedStyle($('.ic', n)).backgroundColor, life: getComputedStyle($('.nlife', n)).display }; }, lv);
    const crit = await one(.08), low = await one(.18);
    const dl = await ev(p, () => { $('#notifs').innerHTML = ''; notifQ.length = 0; if (notifCur) clearTimeout(notifCur.t); notifCur = null; notify({ kind: "dl", icon: I.battLow, title: "Hades II", text: "Listo para jugar" });
      const n = $('#notifs .notif'); return { cls: n.className, pct: !!$('.npct', n), life: getComputedStyle($('.nlife', n)).display, bg: getComputedStyle($('.ic', n)).backgroundImage, blur: getComputedStyle(n).backdropFilter }; });
    const ok = /batt b-crit/.test(crit.cls) && crit.pct === '8 %' && crit.tag === 'Batería crítica' && crit.t === 'Guarda tu partida' && crit.lvl === '8%' && crit.icH === crit.nH && crit.icL === 0 && crit.icBg === 'rgb(255, 77, 77)' && crit.life === 'none'
      && /batt b-low/.test(low.cls) && low.pct === '18 %' && low.t === 'Conecta el cargador' && low.icBg === 'rgb(255, 176, 46)'
      && dl.cls === 'notif' && !dl.pct && dl.life !== 'none' && /gradient/.test(dl.bg) && /blur/.test(dl.blur);
    return ok ? { p } : { p, fail: JSON.stringify({ crit, low, dl }) };
  });

  await run('Lúmina respeta el tema: claro se ve claro y oscuro se ve oscuro; y sigue encendida al volver a abrir Scarlet', async () => {
    const p = await page({ vp: { width: 1920, height: 1080 } }); await lumiOn(p);
    const lum = () => ev(p, () => { const m = getComputedStyle(document.body).backgroundColor.match(/rgb\((\d+), (\d+), (\d+)\)/); return m ? (+m[1] + +m[2] + +m[3]) / 3 : -1; });
    await ev(p, () => { settings.theme = 'dark'; applyTheme(); }); await p.waitForTimeout(900); const d = await lum(), fd = await ev(p, () => getComputedStyle($('.apps')).color);
    await ev(p, () => { settings.theme = 'light'; applyTheme(); saveSettings(); }); await p.waitForTimeout(900); const l = await lum();
    await p.reload(); await p.waitForTimeout(3000);
    const again = await ev(p, () => ({ attr: document.documentElement.hasAttribute('data-lumina'), th: document.documentElement.dataset.theme }));
    await ev(p, () => { settings.lumina = false; settings.theme = 'dark'; saveSettings(); });
    return d >= 0 && d < 60 && l > 200 && again.attr && again.th === 'light' ? { p } : { p, fail: JSON.stringify({ d, l, fd, again }) };
  });

  // ───── 1.7.1 ─────
  const clean = p => ev(p, () => { $('#notifs').innerHTML = ''; notifQ.length = 0; if (notifCur) clearTimeout(notifCur.t); notifCur = null; });
  const SBART = () => { const g = games.find(x => x.appid === 3489700); Art.cache[`grid:${g.appid}:${Art.alt[g.appid] || 0}`] = "https://files.scarlet/f?p=" + encodeURIComponent("C:\\Scarlet\\art\\sb.png"); return g; };

  await run('1.7.1: con Lúmina, «Partida guardada en la nube» lleva la portada del juego, etiqueta «Nube» y tira naranja→amarillo; apagado, igual que antes', async () => {
    const p = await page({ vp: { width: 1920, height: 1080 } });
    const one = () => ev(p, SB => { const g = games.find(x => x.appid === 3489700); state.cloud[g.appid] = { st: "sync" }; window.__emit("cloud.done", { appid: g.appid, ok: true });
      const n = $('#notifs .notif'); return { cls: n.className, img: ($('.ic.game img', n) || {}).src || '', tag: ($('.ntag', n) || {}).textContent, line: getComputedStyle($('.nlife', n)).backgroundImage, tagc: getComputedStyle($('.ntag', n)).color }; });
    await clean(p); await ev(p, `(${SBART})()`);
    const off = await one();
    await clean(p); await lumiOn(p); await ev(p, `(${SBART})()`);
    const on = await one();
    const ok = off.cls === 'notif' && !off.img && off.tag === 'Descarga'
      && /g-nube/.test(on.cls) && /files\.scarlet/.test(on.img) && on.tag === 'Nube' && /255, 106, 0/.test(on.line) && /255, 212, 59/.test(on.line) && on.tagc === 'rgb(255, 138, 31)';
    return ok ? { p } : { p, fail: JSON.stringify({ off, on }) };
  });

  await run('1.7.1: jugando con Lúmina, la app recibe el estilo B (colores, portada en disco, batería); sin Lúmina, el aviso de siempre', async () => {
    const p = await page();
    await ev(p, () => { settings.nInGame = "all"; launchGame(games.find(g => g.appid === 3489700)); });
    await p.waitForFunction(() => mode === "game", null, { timeout: 15000 }); await p.waitForTimeout(300);
    await ev(p, `(${SBART})()`);
    await ev(p, () => { window.__qa.log.length = 0; const g = games.find(x => x.appid === 3489700); state.cloud[g.appid] = { st: "sync" }; window.__emit("cloud.done", { appid: g.appid, ok: true }); });
    const classic = (await calls(p, 'overlay.notify')).pop();
    await ev(p, () => { settings.lumina = true; Lumina.apply(); window.__qa.log.length = 0; const g = games.find(x => x.appid === 3489700); state.cloud[g.appid] = { st: "sync" }; window.__emit("cloud.done", { appid: g.appid, ok: true }); notifyBattery(.08); });
    const [cloud, batt] = await calls(p, 'overlay.notify');
    const ok = classic && !classic.style && classic.tag === 'Descarga'
      && cloud.style === 'b' && cloud.tag === 'Nube' && cloud.lineA === '#ff6a00' && cloud.lineB === '#ffd43b' && cloud.image === 'C:\\Scarlet\\art\\sb.png' && cloud.level === -1
      && batt.style === 'b' && batt.pct === '8 %' && batt.crit === true && batt.glyph === 'batt' && Math.abs(batt.level - .08) < 1e-9 && batt.title === 'Guarda tu partida';
    await ev(p, () => { settings.lumina = false; saveSettings(); });
    return ok ? { p } : { p, fail: JSON.stringify({ classic, cloud, batt }) };
  });

  await run('1.7.1: el menú del botón Xbox encima del juego muestra el cursor, pide su forma redondeada y se entera si apagas Lúmina', async () => {
    const p = await page({ q: '?overlay=quick', wait: 1500, vp: { width: 712, height: 1080 } });
    await ev(p, () => { settings.lumina = true; saveSettings(); Lumina.apply(); openQuick(); }); await p.waitForTimeout(900); await keys(p, ['down']); await p.waitForTimeout(1400);
    const a = await ev(p, () => ({ d: getComputedStyle($('#lumi')).display, op: getComputedStyle($('#lumi')).opacity, lens: document.documentElement.dataset.lens,
      shape: window.__qa.log.filter(x => x.type === 'quick.shape').map(x => x.payload).pop(), q: $('#quick').getBoundingClientRect().left, W: innerWidth, r: rem() }));
    const b = await boxes(p);
    await ev(p, () => { const s = store.get("settings", {}); s.lumina = false; localStorage.setItem("scarlet.settings", JSON.stringify(s)); dispatchEvent(new StorageEvent("storage", { key: "scarlet.settings" })); });
    await p.waitForTimeout(400);
    const c = await ev(p, () => ({ attr: document.documentElement.hasAttribute('data-lumina'), shape: window.__qa.log.filter(x => x.type === 'quick.shape').map(x => x.payload).pop() }));
    const ok = a.d === 'block' && a.op === '1' && a.lens === 'on' && a.shape && a.shape.inset > 10 && a.shape.radius > a.shape.inset && Math.abs(a.q - 1.8 * a.r) < 2 && hugs(b)
      && !c.attr && c.shape && c.shape.off === true;
    return ok ? { p } : { p, fail: JSON.stringify({ a, b, c }) };
  });

  await run('1.7.1: la lente dobla poco en la foto de perfil y mucho en una portada; la perilla del brillo al 100 % no se sale', async () => {
    const p = await page({ vp: { width: 1920, height: 1080 } }); await lumiOn(p);
    const tile = +(await ev(p, () => $('#lumi-f feDisplacementMap').getAttribute('scale')));
    await keys(p, ['up']); await p.waitForTimeout(1500);
    const av = await ev(p, () => [($('.focused') || {}).className, +$('#lumi-f feDisplacementMap').getAttribute('scale')]);
    await ev(p, () => { sys.brightness = 100; openQuick(); }); await p.waitForTimeout(1200);
    const k = await ev(p, () => { const r = $$('#quick .qrow').find(x => $('.nqbar', x)); const u = $('.nqbar u', r).getBoundingClientRect(), rr = r.getBoundingClientRect(); return [u.right, rr.right, u.left, rr.left]; });
    const ok = tile > 30 && /avatar/.test(av[0]) && av[1] < tile / 3 && k[0] <= k[1] - 4 && k[2] >= k[3];
    return ok ? { p, note: `curvatura: portada ${tile.toFixed(0)}, foto ${av[1].toFixed(1)}` } : { p, fail: JSON.stringify({ tile, av, k }) };
  });

  await browser.close();
  const bad = results.filter(r => !r.ok);
  console.log(`\n${results.length - bad.length}/${results.length} escenarios OK`);
})();
