// Pruebas de la beta 1.0: menú rápido nuevo, energía, notificaciones, mejores amigos, mando activo, batería, Dolby, cancelar todas.
const { chromium } = require('playwright');
const URL = 'http://localhost:8799/index.html';
const results = []; let browser;
async function page(opts = {}) {
  const p = await browser.newPage({ viewport: opts.vp || { width: 1280, height: 800 }, hasTouch: true });
  p.errs = [];
  p.on('pageerror', e => p.errs.push('JS: ' + e.message));
  p.on('console', m => { if (m.type() === 'error' && !/favicon|Failed to load resource|ERR_|net::/.test(m.text())) p.errs.push('consola: ' + m.text()); });
  if (opts.init) await p.addInitScript(opts.init);
  await p.goto(URL + (opts.q || '')); await p.waitForTimeout(opts.wait || 3000);
  await p.evaluate(() => { if (mode === "boot") dismissBoot(); $('#notifs').innerHTML = ''; });
  await p.waitForTimeout(400);
  return p;
}
const ev = (p, f, a) => p.evaluate(f, a);
const keys = async (p, list, ms = 120) => { for (const k of list) { await p.evaluate(k => { input(k); }, k); await p.waitForTimeout(ms); } };
const calls = (p, t) => ev(p, t => window.__qa.log.filter(x => x.type === t).map(x => x.payload), t);
const clearNotifs = p => ev(p, () => { notifQ.length = 0; if (notifCur) { clearTimeout(notifCur.t); notifCur.el.remove(); notifCur = null; } $('#notifs').innerHTML = ''; });
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

  await run('Menú rápido nuevo: orden de la lista, arriba/abajo y barras con ◀▶', async () => {
    const p = await page();
    await ev(p, () => { window.__qa.media = { app: "Apple Music", title: "Canción", artist: "Artista", playing: true, position: 10, duration: 200, now: Date.now() }; window.__qa.mixer = [{ key: "g", exe: "SB", name: "Stellar Blade", volume: 80, appid: 3489700 }]; openQuick(); });
    await p.waitForTimeout(900);
    const order = await ev(p, () => qmRows().map(r => r.k).join(','));
    await ev(p, () => { QM.i = qmRows().findIndex(r => r.k === "volume"); renderQuick(); });
    const v0 = await ev(p, () => sys.volume);
    await keys(p, ['right', 'right', 'left'], 60);
    const v1 = await ev(p, () => sys.volume), volCalls = (await calls(p, 'system.volume')).length;
    const bubble = await ev(p, () => getComputedStyle($('#quick .focused .nqbar em')).display);
    await keys(p, ['down'], 60);
    const mixOk = await ev(p, () => qmRows()[QM.i].type === 'mix');
    await keys(p, ['left'], 60);
    const mix = (await calls(p, 'mixer.set')).pop();
    // hasta abajo y de vuelta arriba (con tope en los extremos)
    for (let i = 0; i < 14; i++) await keys(p, ['down'], 30);
    const last = await ev(p, () => qmRows()[QM.i].k);
    for (let i = 0; i < 14; i++) await keys(p, ['up'], 30);
    const first = await ev(p, () => qmRows()[QM.i].k);
    // canción: ◀▶ eligen botón; A lo usa
    await keys(p, ['right', 'a'], 80);
    const cmd = (await calls(p, 'media.cmd')).pop();
    const ok = order === 'media,hibernate,brightness,volume,mix:SB,airplane,wifi' && v1 === v0 + 5 && volCalls === 3 && bubble === 'block' && mixOk && mix && mix.value === 75 && last === 'wifi' && first === 'media' && cmd && cmd.cmd === 'next';
    return ok ? { p } : { p, fail: JSON.stringify({ order, v0, v1, volCalls, bubble, mixOk, mix, last, first, cmd }) };
  });

  await run('Menú rápido nuevo: Wi-Fi con nombre de la red, modo avión con ayuda, VPN; ya sin Apagar… ni Ajustes (también el clásico)', async () => {
    const p = await page();
    await ev(p, () => openQuick()); await p.waitForTimeout(900);
    const wifi = await ev(p, () => [...$$('#quick .nqit')].find(e => /Wi-Fi/.test(e.textContent)).querySelector('.nqv').textContent);
    await ev(p, () => { QM.i = qmRows().findIndex(r => r.k === "airplane"); renderQuick(); });
    const help = await ev(p, () => !!$('#quick .nqhelp'));
    await keys(p, ['a'], 300);
    const air = await ev(p, () => [sys.airplane, sys.wifi]);
    await keys(p, ['a'], 300);
    const gone = await ev(p, () => { const ns = qmRows().map(r => r.k); settings.quickClassic = true; const cl = qmRows().map(r => r.k); settings.quickClassic = false; return [ns, cl, $('#quick').textContent.includes('Apagar'), $('#quick').textContent.includes('Ajustes')]; });
    const ok = wifi === 'Casa_5G' && help && air[0] && !air[1] && !gone[0].includes("sleep") && !gone[0].includes("settings") && !gone[1].includes("sleep") && !gone[1].includes("settings") && !gone[2] && !gone[3];
    return ok ? { p } : { p, fail: JSON.stringify({ wifi, help, air, gone }) };
  });

  await run('Menú rápido nuevo: táctil (barra, botones de la canción, filas y Atrás)', async () => {
    const p = await page();
    await ev(p, () => { window.__qa.media = { app: "Apple Music", title: "Canción", artist: "Artista", playing: true, position: 10, duration: 200, now: Date.now() }; openQuick(); });
    await p.waitForTimeout(900);
    const bar = await p.locator('#quick [aria-label="Brillo"] .qbar').boundingBox();
    await p.mouse.click(bar.x + bar.width * .25, bar.y + bar.height / 2); await p.waitForTimeout(200);
    const bri = await ev(p, () => sys.brightness);
    await p.locator('#quick .nqmu .qmb[data-m="1"]').click(); await p.waitForTimeout(200);
    const prev = (await calls(p, 'media.cmd')).pop();
    await p.locator('#quick .nqit', { hasText: 'Modo avión' }).click(); await p.waitForTimeout(300);
    const air = await ev(p, () => sys.airplane);
    await p.locator('#quick .nqfoot .hint', { hasText: 'Atrás' }).click(); await p.waitForTimeout(300);
    const open = await ev(p, () => QM.open);
    return bri === 25 && prev && prev.cmd === 'prev' && air && !open ? { p } : { p, fail: JSON.stringify({ bri, prev, air, open }) };
  });

  await run('Menú rápido nuevo: Hibernar (en el menú y encima del juego)', async () => {
    const p = await page();
    await ev(p, () => openQuick()); await p.waitForTimeout(700);
    await ev(p, () => { QM.i = qmRows().findIndex(r => r.k === "hibernate"); renderQuick(); });
    await keys(p, ['a'], 600);
    const hib = (await calls(p, 'system.hibernate')).length;
    const p2 = await page({ q: '?overlay=quick', wait: 1500, vp: { width: 712, height: 1080 } });
    await ev(p2, () => window.__emit("quick.open")); await p2.waitForTimeout(800);
    await ev(p2, () => { QM.i = qmRows().findIndex(r => r.k === "hibernate"); renderQuick(); });
    await ev(p2, () => window.__emit("pad.key", "a")); await p2.waitForTimeout(600);
    const act = (await calls(p2, 'quick.action')).pop();
    await p2.close();
    return hib === 1 && act && act.action === 'hibernate' ? { p } : { p, fail: JSON.stringify({ hib, act }) };
  });

  await run('Menú rápido nuevo: la fila VPN prende y apaga WARP', async () => {
    const p = await page({ init: () => { const t = setInterval(() => { if (window.__qa) { Object.assign(window.__qa.vpn, { provider: "warp", warp: true }); clearInterval(t); } }, 0); } });
    await ev(p, () => openQuick()); await p.waitForTimeout(1200);
    await ev(p, () => { QM.i = qmRows().findIndex(r => r.k === "vpn"); renderQuick(); });
    await keys(p, ['a'], 900);
    const on = await ev(p, () => [vpnOn(), $('#quick .nqit.focused .nqv').textContent, $('#quick .nqit.focused .nqv').classList.contains('on')]);
    await keys(p, ['a'], 900);
    const off = await ev(p, () => vpnOn());
    return on[0] && on[2] && !off ? { p, note: on[1] } : { p, fail: JSON.stringify({ on, off }) };
  });

  await run('Brillo automático: solo con sensor; al prenderlo la barra de brillo queda fija', async () => {
    const p = await page();
    await ev(p, () => openQuick()); await p.waitForTimeout(900);
    const without = await ev(p, () => qmRows().some(r => r.k === 'autobri'));
    await ev(p, () => closeQuick(true));
    await ev(p, () => { window.__qa.sensor = true; openQuick(); }); await p.waitForTimeout(900);
    const withS = await ev(p, () => qmRows().some(r => r.k === 'autobri'));
    await ev(p, () => { QM.i = qmRows().findIndex(r => r.k === "autobri"); renderQuick(); });
    await keys(p, ['a'], 400);
    const on = await ev(p, () => sys.autoBri), call = (await calls(p, 'system.autobright')).pop();
    await ev(p, () => { QM.i = qmRows().findIndex(r => r.k === "brightness"); renderQuick(); });
    const b0 = await ev(p, () => sys.brightness); await keys(p, ['right'], 200); const b1 = await ev(p, () => sys.brightness);
    const t = await ev(p, () => $('#toast').textContent);
    return !without && withS && on && call && call.on === true && b0 === b1 && /brillo automático/i.test(t) ? { p } : { p, fail: JSON.stringify({ without, withS, on, call, b0, b1, t }) };
  });

  await run('Opciones de energía: cuenta de 10 s que hiberna sola, se detiene con un botón, y Reiniciar', async () => {
    const p = await page();
    await ev(p, () => powerMenu()); await p.waitForTimeout(500);
    const txt = await ev(p, () => $('.pwcount span').textContent);
    await ev(p, () => { topView().left = 1; }); await p.waitForTimeout(1600);
    const hib = (await calls(p, 'system.hibernate')).length;
    await ev(p, () => { wake(); }); await p.waitForTimeout(300);
    await ev(p, () => { mode = "ui"; $('#lock').className = ""; powerMenu(); }); await p.waitForTimeout(400);
    await keys(p, ['down'], 1500);
    const stopped = await ev(p, () => topView() && topView().isPower && topView().left == null);
    await keys(p, ['a'], 400);
    const rs = (await calls(p, 'system.restart')).length;
    return /hibernará sola en 10 s/.test(txt) && hib === 1 && stopped && rs === 1 ? { p } : { p, fail: JSON.stringify({ txt, hib, stopped, rs }) };
  });

  await run('Notificaciones: de una en una, con línea y acción con el botón Xbox', async () => {
    const p = await page(); await clearNotifs(p);
    await ev(p, () => { const f = friends[0]; notifyFriend(f); notifyBattery(.15); notifyAchievement(games.find(hasAch), "Explorador"); });
    await p.waitForTimeout(300);
    // beta 1.6: el logro va en su propia cola (estilo Xbox) y sale cuando se va la notificación de ahora
    const one = await ev(p, () => [$$('#notifs .notif').length, notifQ.length + Ach.q.length, !!$('#notifs .nlife'), !!$('#notifs .xbtn')]);
    await ev(p, () => window.__emit("quick.toggle")); await p.waitForTimeout(900);
    const r = await ev(p, () => [QM.open, !!(topView() && /Nivel|Perfil|horas|Amigo/i.test(topView().el.textContent)), Ach.cur ? Ach.cur.name : notifCur && notifCur.title]);
    await ev(p, () => closeAllViews());
    await clearNotifs(p);
    await ev(p, () => window.__emit("quick.toggle")); await p.waitForTimeout(500);
    const q = await ev(p, () => QM.open);
    return one[0] === 1 && one[1] === 2 && one[2] && one[3] && !r[0] && r[1] && r[2] === 'Explorador' && q ? { p } : { p, fail: JSON.stringify({ one, r, q }) };
  });

  await run('Notificaciones: Ajustes (apagadas, solo mejores amigos) y sin sonido', async () => {
    const p = await page(); await clearNotifs(p);
    await ev(p, () => { settings.nAch = false; settings.nFriendOn = "best"; settings.bestFriends = ["Luna"]; settings.nSound = false; window.__played = 0; const o = play; window.play = (...a) => { if (a[0] === 'notif' || a[0] === 'friend') window.__played++; return o(...a); }; });
    await ev(p, () => { notifyAchievement(games.find(hasAch), "X"); notifyFriend({ name: "NeoKaiser" }); notifyFriend({ name: "Luna" }); });
    await p.waitForTimeout(300);
    const r = await ev(p, () => [notifCur && notifCur.title, notifQ.length, !!$('#notifs .nstar'), window.__played]);
    return r[0] === 'Luna' && r[1] === 0 && r[2] && r[3] === 0 ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Jugando: solo importantes encima del juego; lo demás en «Mientras jugabas» al volver', async () => {
    const p = await page(); await clearNotifs(p);
    await ev(p, () => { settings.nInGame = "important"; launchGame(games.find(g => g.appid === 3489700)); });
    await p.waitForFunction(() => mode === "game", null, { timeout: 15000 }); await p.waitForTimeout(300); await clearNotifs(p);
    await ev(p, () => { notifyFriend({ name: "NeoKaiser" }); notifyBattery(.1); notifyAchievement(games.find(hasAch), "Explorador"); });
    await p.waitForTimeout(300);
    // beta 1.6: los logros cuentan como importantes: la app los dibuja encima del juego (ach.show)
    const ov = (await calls(p, 'overlay.notify')).map(x => x.title), miss = await ev(p, () => missed.length), achs = (await calls(p, 'ach.show')).length;
    await ev(p, () => goHome()); await p.waitForTimeout(3600);
    const sum = await ev(p, () => notifCur && [notifCur.title, notifCur.lines.join(' | ')]);
    await ev(p, () => window.__emit("quick.toggle")); await p.waitForTimeout(800);
    const view = await ev(p, () => topView() && topView().el.querySelector('.vhead').textContent);
    return ov.join() === 'Batería baja' && miss === 1 && achs === 1 && sum && sum[0] === 'Mientras jugabas' && /NeoKaiser/.test(sum[1]) && /Mientras jugabas/.test(view) ? { p } : { p, fail: JSON.stringify({ ov, miss, achs, sum, view }) };
  });

  await run('Capturas: «Guardando video…» → «Video guardado» con ▶ y duración; foto con «Ver» que abre el Álbum', async () => {
    const p = await page(); await clearNotifs(p);
    await ev(p, () => window.__emit("capture.saving", { path: "C:\\v.mp4", type: "video", game: "Stellar Blade", url: "" })); await p.waitForTimeout(300);
    const s = await ev(p, () => [notifCur.title, !!$('#notifs .rec')]);
    await ev(p, () => window.__emit("capture.saved", { path: "C:\\v.mp4", type: "video", game: "Stellar Blade", url: "x.mp4", thumb: shotSvg(games[0], 2), duration: 30 })); await p.waitForTimeout(300);
    const v = await ev(p, () => [notifCur.title, $('#notifs .du') && $('#notifs .du').textContent, !!$('#notifs .pl'), $$('#notifs .notif').length]);
    await clearNotifs(p);
    await ev(p, () => window.__emit("capture.saved", { path: "C:\\f.png", type: "image", game: "Stellar Blade", url: shotSvg(games[0], 5) })); await p.waitForTimeout(300);
    const f = await ev(p, () => [notifCur.title, notifCur.text, !!$('#notifs .thumb img')]);
    await ev(p, () => window.__emit("quick.toggle")); await p.waitForTimeout(900);
    const viewer = await ev(p, () => topView() && topView().cls);
    return s[0] === 'Guardando video…' && s[1] && v[0] === 'Video guardado' && v[1] === '0:30' && v[2] && v[3] === 1 && f[0] === 'Captura guardada' && /Stellar Blade · en el Álbum/.test(f[1]) && f[2] && viewer === 'viewer' ? { p } : { p, fail: JSON.stringify({ s, v, f, viewer }) };
  });

  await run('Amigos: ★ en su perfil lo pone primero en la lista (se guarda); el mosaico Amigos abre la lista', async () => {
    const p = await page();
    await ev(p, () => { settings.bestFriends = []; const a = APPS.find(x => x.label === "Amigos"); a.act(document.body); }); await p.waitForTimeout(700);
    const before = await ev(p, () => [topView().isProfile, topView().ci, $$(".fcardb b", topView().el).map(b => b.textContent)]);
    await ev(p, () => { const v = topView(); const k = v.rows.findIndex(r => r.name === "Luna"); v.side = "R"; v.ri = v.i = k; updateFocus(); });
    await keys(p, ['a'], 800);
    await ev(p, () => $('[data-a="best"]', topView().el).click()); await p.waitForTimeout(200);
    await keys(p, ['b'], 500);
    const after = await ev(p, () => [$$(".fcardb b", topView().el)[0].textContent, store.get("settings", {}).bestFriends]);
    return before[0] && before[1] === 1 && before[2][0] !== "Luna★" && after[0] === "Luna★" && after[1].includes("Luna") ? { p } : { p, fail: JSON.stringify({ before, after }) };
  });

  await run('Mando activo: el ícono de abajo cambia al mando que usas y marca su número', async () => {
    const p = await page();
    await p.waitForTimeout(500);
    await ev(p, () => window.__emit("pad.active", { key: "m1", layout: "xbox" })); await p.waitForTimeout(200);
    const x = await ev(p, () => [$('.hints .padicon').classList.contains('xbox'), $$('#padsq i').map(i => i.classList.contains('on') ? 1 : 0).join('')]);
    await ev(p, () => window.__emit("pad.active", { key: "ds", layout: "ps" })); await p.waitForTimeout(200);
    const ps = await ev(p, () => $('.hints .padicon').classList.contains('ps'));
    await ev(p, () => window.__emit("pad.active", { key: "ally", layout: "" })); await p.waitForTimeout(200);
    const ally = await ev(p, () => [$('.hints .padicon').className, $$('#padsq i').map(i => i.classList.contains('on') ? 1 : 0).join('')]);
    await ev(p, () => window.__emit("pad.active", { key: "m1", layout: "xbox" })); await ev(p, () => openQuick()); await p.waitForTimeout(400);
    const q = await ev(p, () => $('#quick .padicon').classList.contains('xbox'));
    return x[0] && x[1] === '0100' && ps && ally[0] === 'padicon' && ally[1] === '1000' && q ? { p } : { p, fail: JSON.stringify({ x, ps, ally, q }) };
  });

  await run('Batería: tocarla muestra el % y lo que queda', async () => {
    const p = await page();
    await p.locator('.status .batt').click(); await p.waitForTimeout(300);
    const r = await ev(p, () => [$('#battpop').classList.contains('show'), $('#battpop').textContent]);
    await keys(p, ['down'], 200);
    const closed = await ev(p, () => !$('#battpop').classList.contains('show'));
    return r[0] && /%/.test(r[1]) && closed ? { p, note: r[1] } : { p, fail: JSON.stringify({ r, closed }) };
  });

  await run('Dolby dorado: animado en el menú rápido; se puede volver al blanco', async () => {
    const p = await page();
    await ev(p, () => { ATMOS = true; openQuick(); }); await p.waitForTimeout(900);
    const g = await ev(p, () => { const l = $('#quick .glg'); return l ? [getComputedStyle(l, '::before').animationName, (getComputedStyle(l).maskImage || getComputedStyle(l).webkitMaskImage).slice(0, 40)] : null; });   // beta 1.2: el brillo va en una capa (GPU)
    await ev(p, () => { closeQuick(true); settings.dolbyGold = false; openQuick(); }); await p.waitForTimeout(500);
    const w = await ev(p, () => [!!$('#quick .dlogo'), !$('#quick .glg')]);
    return g && g[0] === 'dgoldT' && /url/.test(g[1]) && w[0] && w[1] ? { p } : { p, fail: JSON.stringify({ g, w }) };
  });

  await run('Descargas: «Cancelar todas (2)» cancela las dos', async () => {
    const p = await page();
    await ev(p, () => { window.__emit("download.progress", { appid: 1091500, kind: "Descargando", state: "active", bytes: 3e10, total: 8.46e10, eta: 2400, rate: 2e7, phase: "download", progress: .36, fresh: true }); window.__emit("download.progress", { appid: 1245620, kind: "Actualizando", state: "queued", bytes: 0, total: 1e9, eta: -1, rate: 0, phase: "download", progress: 0 }); });
    await p.waitForTimeout(500);
    await ev(p, () => { downloadOptions(byId(1091500)); }); await p.waitForTimeout(500);
    const labels = await ev(p, () => topView().items().map(b => b.textContent));
    await ev(p, () => { const v = topView(); v.i = v.items().findIndex(b => /Cancelar todas/.test(b.textContent)); updateFocus(); });
    await keys(p, ['a'], 500);
    await ev(p, () => { const v = topView(); v.i = 1; updateFocus(); }); await keys(p, ['a'], 1500);
    const c = (await calls(p, 'download.cancel')).map(x => x.appid).sort();
    return labels.includes('Cancelar todas (2)') && c.length === 2 ? { p } : { p, fail: JSON.stringify({ labels, c }) };
  });

  await run('Ajustes › Notificaciones: los valores se cambian y se guardan', async () => {
    const p = await page();
    await ev(p, () => { openView(settingsView()); topView().goto("Logros"); }); await p.waitForTimeout(200);
    await keys(p, ['a'], 200);
    const ach = await ev(p, () => [settings.nAch, $('.nsrow.focused .nsw').classList.contains('on'), !!$('.nshelp')]);
    await ev(p, () => topView().goto("Avisos encima del juego")); await keys(p, ['a'], 500);
    await ev(p, () => { const v = topView(); v.i = v.items().findIndex(b => /Ninguno/.test(b.textContent)); updateFocus(); }); await keys(p, ['a'], 500);
    const g = await ev(p, () => [settings.nInGame, store.get("settings", {}).nInGame, $('.nsrow.focused .nv').textContent]);
    return ach[0] === false && !ach[1] && ach[2] && g[0] === 'none' && g[1] === 'none' && g[2] === 'Ninguno' ? { p } : { p, fail: JSON.stringify({ ach, g }) };
  });

  await browser.close();
  const bad = results.filter(r => !r.ok);
  console.log(`\n${results.length - bad.length}/${results.length} escenarios OK`);
})();
