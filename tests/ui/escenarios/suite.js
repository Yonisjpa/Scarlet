const { chromium } = require('playwright');
const URL = 'http://localhost:8799/index.html';
const results = [];
let browser;
async function page(opts = {}) {
  const p = await browser.newPage({ viewport: opts.vp || { width: 1280, height: 800 }, hasTouch: true });
  p.errs = [];
  p.on('pageerror', e => p.errs.push('JS: ' + e.message));
  p.on('console', m => { if (m.type() === 'error' && !/favicon|Failed to load resource|ERR_|net::/.test(m.text())) p.errs.push('consola: ' + m.text()); });
  if (opts.init) await p.addInitScript(opts.init);
  await p.goto(URL + (opts.q || '')); await p.waitForTimeout(opts.wait || 3200);
  return p;
}
const ev = (p, f, a) => p.evaluate(f, a);
const keys = async (p, list, ms = 70) => { for (const k of list) { await p.evaluate(k => input(k), k); await p.waitForTimeout(ms); } };
async function run(name, fn) {
  if (process.env.ONLY && !new RegExp(process.env.ONLY).test(name)) return;
  let p, ok = true, note = '';
  try { const r = await Promise.race([fn(), new Promise((_, rej) => setTimeout(() => rej(new Error('tardó más de 60 s (¿interfaz colgada?)')), 60000))]); p = r && r.p; note = (r && r.note) || ''; if (r && r.fail) { ok = false; note = r.fail; } }
  catch (e) { ok = false; note = 'excepción de la prueba: ' + e.message.split('\n')[0]; }
  const errs = p ? p.errs : [];
  if (errs.length) ok = false;
  results.push({ name, ok, note, errs: [...new Set(errs)].slice(0, 5) });
  if (p) await p.close();
  console.log((ok ? 'OK   ' : 'FALLA') + ' ' + name + (note ? ' — ' + note : '') + (errs.length ? '\n        ' + [...new Set(errs)].slice(0, 3).join('\n        ') : ''));
}
const dismiss = p => ev(p, () => { if (mode === "boot" && typeof dismissBoot === "function") dismissBoot(); });

(async () => {
  browser = await chromium.launch();

  await run('Arranque: carga sin errores y muestra el Inicio', async () => {
    const p = await page(); await dismiss(p); await p.waitForTimeout(800);
    const r = await ev(p, () => ({ mode, tiles: document.querySelectorAll('#track .tile').length, unknown: window.__qa.unknown || [] }));
    if (r.mode !== 'ui') return { p, fail: 'modo ' + r.mode };
    if (r.unknown.length) return { p, fail: 'mensajes sin atender: ' + r.unknown };
    return { p, note: r.tiles + ' mosaicos' };
  });

  await run('Inicio: moverse por mosaicos, apps y perfil (bordes incluidos)', async () => {
    const p = await page(); await dismiss(p);
    await keys(p, ['right','right','right','right','right','right','right','right','right','right','left','down','right','right','right','right','right','right','right','right','right','right','up','up','up','left','left','down']);
    return { p, note: 'zona final: ' + await ev(p, () => home.zone) };
  });

  await run('Juego: lanzar, salir al Inicio, volver a entrar = reanudar (no relanza)', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => launchGame(games.find(g => g.name === "Stellar Blade"))); await p.waitForTimeout(2600);
    const m1 = await ev(p, () => [mode, state.running]);
    await ev(p, () => window.__emit("guide.double", { hidden: true })); await p.waitForTimeout(700);
    const m2 = await ev(p, () => [mode, document.querySelector('#track .tile .playing b')?.textContent]);
    const before = await ev(p, () => window.__qa.calls.filter(c => c === "game.launch").length);
    await ev(p, () => launchGame(games.find(g => g.name === "Stellar Blade"))); await p.waitForTimeout(1200);
    const after = await ev(p, () => [window.__qa.calls.filter(c => c === "game.launch").length, window.__qa.calls.filter(c => c === "game.focus").length, mode]);
    if (m1[0] !== 'game' || m1[1] !== 3489700) return { p, fail: 'no quedó en juego: ' + m1 };
    if (m2[0] !== 'ui' || m2[1] !== 'En uso') return { p, fail: 'al Inicio: ' + m2 };
    if (after[0] !== before) return { p, fail: 'se volvió a lanzar el juego' };
    if (after[2] !== 'game') return { p, fail: 'no regresó al juego' };
    return { p, note: 'reanudó con game.focus' };
  });

  await run('Juego: la interfaz se resincroniza si "olvidó" el juego', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => { window.__qa.running = 3489700; state.running = null; mode = "game"; goHome(); }); await p.waitForTimeout(500);
    const r = await ev(p, () => state.running);
    return r === 3489700 ? { p, note: 'recuperó el "Jugando"' } : { p, fail: 'sigue olvidado: ' + r };
  });

  await run('Juego: abrir otro juego con uno abierto (cancelar y aceptar)', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => launchGame(games.find(g => g.name === "Stellar Blade"))); await p.waitForTimeout(2400);
    await ev(p, () => window.__emit("guide.double", { hidden: true })); await p.waitForTimeout(600);
    ev(p, () => launchGame(games.find(g => g.name === "ELDEN RING"))); await p.waitForTimeout(600);
    const dlg = await ev(p, () => !!document.querySelector('.dialog'));
    await keys(p, ['b']); await p.waitForTimeout(300);
    const still = await ev(p, () => state.running);
    ev(p, () => launchGame(games.find(g => g.name === "ELDEN RING"))); await p.waitForTimeout(500);
    await ev(p, () => document.querySelectorAll('.dialog .dbtns button')[1].click()); await p.waitForTimeout(2600);
    const r = await ev(p, () => [state.running, window.__qa.calls.includes("game.close")]);
    if (!dlg) return { p, fail: 'no preguntó' };
    if (still !== 3489700) return { p, fail: 'cancelar cerró el juego' };
    return r[0] === 1245620 && r[1] ? { p, note: 'cerró uno y abrió el otro' } : { p, fail: 'resultado ' + r };
  });

  await run('Apps junto al juego: Apple Music y YouTube sin cerrar el juego', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => launchGame(games.find(g => g.name === "Stellar Blade"))); await p.waitForTimeout(2400);
    await ev(p, () => window.__emit("guide.double", { hidden: true })); await p.waitForTimeout(600);
    await ev(p, () => launchGame(games.find(g => g.name === "Apple Music"))); await p.waitForTimeout(900);
    await ev(p, () => launchGame(games.find(g => g.name === "YouTube"))); await p.waitForTimeout(900);
    const r = await ev(p, () => [state.running, window.__qa.calls.filter(c => c === "app.open").length, window.__qa.calls.includes("game.close"), !!document.querySelector('.dialog')]);
    return r[0] === 3489700 && r[1] === 2 && !r[2] && !r[3] ? { p, note: 'juego intacto, 2 apps abiertas' } : { p, fail: JSON.stringify(r) };
  });

  await run('Juego: se cierra por fuera (game.exited) y la interfaz lo refleja', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => launchGame(games.find(g => g.name === "Stellar Blade"))); await p.waitForTimeout(2400);
    await ev(p, () => window.__emit("game.exited", { appid: 3489700 })); await p.waitForTimeout(800);
    const r = await ev(p, () => [mode, state.running]);
    return r[0] === 'ui' && !r[1] ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Juego: falla al iniciar', async () => {
    const p = await page({ init: () => { } }); await dismiss(p);
    await ev(p, () => { window.__qa.fail.add("game.launch"); launchGame(games.find(g => g.name === "Stellar Blade")); setTimeout(() => window.__emit("game.failed", { appid: 3489700 }), 900); });
    await p.waitForTimeout(2600);
    const r = await ev(p, () => [mode, state.running]);
    return r[0] === 'ui' && !r[1] ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Juego no instalado: pregunta instalar y pasa al primer lugar', async () => {
    const p = await page(); await dismiss(p);
    ev(p, () => launchGame(games.find(g => g.name === "Cyberpunk 2077"))); await p.waitForTimeout(600);
    await ev(p, () => document.querySelectorAll('.dialog .dbtns button')[1].click()); await p.waitForTimeout(400);
    const r = await ev(p, () => [homeGames()[0].name, window.__qa.calls.includes("system.open")]);
    return r[0] === 'Cyberpunk 2077' ? { p, note: 'primer lugar: ' + r[0] } : { p, fail: JSON.stringify(r) };
  });

  await run('Juego que no responde: diálogo esperar / cerrar', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => launchGame(games.find(g => g.name === "Stellar Blade"))); await p.waitForTimeout(2400);
    await ev(p, () => window.__emit("game.hung", { appid: 3489700, name: "Stellar Blade" })); await p.waitForTimeout(900);
    const t = await ev(p, () => document.querySelector('.dialog .dbox p')?.textContent || '');
    await ev(p, () => document.querySelectorAll('.dialog .dbtns button')[1].click()); await p.waitForTimeout(900);
    const r = await ev(p, () => [state.running, window.__qa.calls.includes("game.kill")]);
    return /no responde/.test(t) && !r[0] && r[1] ? { p } : { p, fail: t + ' ' + JSON.stringify(r) };
  });

  await run('Menú rápido (Inicio): cada fila y cada control', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => { window.__qa.media = { app: "Apple Music", title: "Canción", artist: "Artista", playing: true, position: 10, duration: 200, now: Date.now(), canShuffle: true }; window.__qa.mixer = [{ key: "music", exe: "AppleMusic", name: "Apple Music", volume: 70, appid: 900002, source: "xbox", kind: "app" }]; openQuick(); });
    await p.waitForTimeout(800);
    const rows = await ev(p, () => qmRows().map(r => r.k));
    for (let i = 0; i < rows.length + 2; i++) { await keys(p, ['left', 'right', 'right', 'left'], 30); await keys(p, ['down'], 40); }
    await keys(p, ['up','up','up','up','up','up','up','up','up','up'], 30);
    await keys(p, ['a', 'lb', 'rb'], 60);
    const r = await ev(p, () => [QM.open, sys.brightness, sys.volume]);
    await keys(p, ['b']); await p.waitForTimeout(300);
    return { p, note: 'filas: ' + rows.join(', ') + ' · abierto=' + r[0] };
  });

  await run('Menú rápido: Wi-Fi / modo avión, incluso si Windows no deja', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => openQuick()); await p.waitForTimeout(700);
    const sel = k => ev(p, k => { QM.i = qmRows().findIndex(r => r.k === k); renderQuick(); }, k);
    await sel('airplane'); await keys(p, ['a']); await p.waitForTimeout(250);
    const a = await ev(p, () => [sys.wifi, sys.airplane]);
    await sel('wifi'); await keys(p, ['a']); await p.waitForTimeout(250);
    const b = await ev(p, () => [sys.wifi, sys.airplane]);
    await ev(p, () => { window.__qa.radioDeny = true; }); await sel('airplane'); await keys(p, ['a']); await p.waitForTimeout(300);
    const c = await ev(p, () => [sys.wifi, sys.airplane]);
    const okA = !a[0] && a[1], okB = b[0] && b[1], okC = c[0] && c[1];
    return okA && okB && okC ? { p } : { p, fail: JSON.stringify({ a, b, c }) };
  });

  await run('Menú rápido: Windows no contesta nada (todas las lecturas fallan)', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => { ["quick.live", "system.state", "media.state", "system.radio", "system.volume", "system.brightness", "mixer.set"].forEach(t => window.__qa.fail.add(t)); openQuick(); });
    await p.waitForTimeout(2600);
    await keys(p, ['down', 'right', 'right', 'down', 'right', 'left', 'down', 'a', 'down', 'a']);
    await p.waitForTimeout(500);
    const r = await ev(p, () => [QM.open, sys.volume, typeof sys.brightness]);
    return { p, note: 'sigue usable: ' + JSON.stringify(r) };
  });

  await run('Menú rápido: Windows lento (lecturas de 3 s) sin amontonarse', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => { window.__qa.slow["quick.live"] = 3000; });
    for (let i = 0; i < 4; i++) { await ev(p, () => openQuick()); await p.waitForTimeout(900); await ev(p, () => closeQuick(true)); await p.waitForTimeout(200); }
    await ev(p, () => openQuick()); await p.waitForTimeout(6500);
    const n = await ev(p, () => window.__qa.calls.filter(c => c === "quick.live").length);
    return n <= 9 ? { p, note: n + ' lecturas en ~10 s' } : { p, fail: n + ' lecturas: se amontonan' };
  });

  await run('Menú rápido encima del juego (overlay): botones de la app, táctil y cerrar', async () => {
    const p = await page({ q: '?overlay=quick', wait: 1500, vp: { width: 712, height: 1080 } });
    await ev(p, () => { window.__qa.mixer = [{ key: "game", exe: "SB", name: "Stellar Blade", volume: 80, appid: 3489700, source: "steam", kind: "game" }, { key: "music", exe: "AppleMusic", name: "Apple Music", volume: 60, appid: 900002, source: "xbox", kind: "app" }]; window.__emit("quick.open"); });
    await p.waitForTimeout(800);
    for (const k of ['down', 'down', 'right', 'down', 'left', 'down', 'down', 'right', 'up']) { await ev(p, k => window.__emit("pad.key", k), k); await p.waitForTimeout(50); }
    const bar = await p.locator('#quick .nqmix .qbar').first().boundingBox();
    await p.mouse.click(bar.x + bar.width * .3, bar.y + bar.height / 2); await p.waitForTimeout(200);
    const v = await ev(p, () => QM.mix[0].volume);
    await ev(p, () => window.__emit("pad.key", "b")); await p.waitForTimeout(500);
    const r = await ev(p, () => [QM.open, window.__qa.calls.includes("quick.close"), window.__qa.calls.includes("quick.shown")]);
    return !r[0] && r[1] && r[2] && v === 30 ? { p } : { p, fail: JSON.stringify({ v, r }) };
  });

  await run('Ajustes: cada categoría y cada opción, y regresar', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => openView(settingsView())); await p.waitForTimeout(500);
    const cats = await ev(p, () => topView().items().map(b => b.textContent));
    let n = 0; const errs = [];
    for (let c = 0; c < cats.length; c++) {
      const rows = await ev(p, c => { const v = views.find(x => x.isSettings); while (topView() !== v) closeView(); v.side = "L"; v.ci = c; v.i = c; v.paneRender(); updateFocus(); return v.rows().map(r => r.label); }, c);
      for (let j = 0; j < rows.length; j++) {
        if (/Salir al escritorio|Probar: actualización|Atenuar|Suspender|Explorador|Ajustes de Windows|Wi-Fi y red/.test(rows[j])) continue;
        await ev(p, ([c, j]) => { const v = views.find(x => x.isSettings); while (topView() !== v) closeView(); v.ci = c; v.side = "R"; v.ri = j; v.i = j; v.paneRender(); updateFocus(); }, [c, j]);
        await keys(p, ['a'], 250);
        await keys(p, ['down', 'up', 'right', 'left'], 40);
        await ev(p, () => { const v = views.find(x => x.isSettings); while (views.length && topView() !== v) closeView(); });
        await p.waitForTimeout(120); n++;
      }
    }
    // con el control: entrar, bajar, regresar a categorías y cerrar con B
    await ev(p, () => { closeAllViews(); openView(settingsView()); }); await p.waitForTimeout(300);
    // (1.8) en Pantalla la primera fila es la barra de brillo: ahí ◀ baja el brillo, así que se regresa con B
    await keys(p, ['down', 'down', 'a', 'down', 'b', 'down', 'right', 'b', 'b'], 120);
    const closed = await ev(p, () => !views.some(x => x.isSettings));
    return closed ? { p, note: cats.length + ' categorías, ' + n + ' opciones revisadas' } : { p, fail: 'B no cerró Ajustes' };
  });

  await run('Ajustes: tema Automático (como Windows) y menú rápido clásico', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => { openView(settingsView()); topView().goto("Automático (como Windows)"); }); await p.waitForTimeout(200);
    await keys(p, ['a'], 200);
    const th = await ev(p, () => [settings.theme, document.documentElement.dataset.theme]);
    await p.emulateMedia({ colorScheme: 'light' }); await p.waitForTimeout(200);
    const th2 = await ev(p, () => document.documentElement.dataset.theme);
    await ev(p, () => { topView().goto("Menú rápido clásico"); }); await keys(p, ['a'], 200);
    await ev(p, () => { closeAllViews(); openQuick(); }); await p.waitForTimeout(500);
    const classic = await ev(p, () => !!document.querySelector('#quick .qpair') && !document.querySelector('#quick.ns'));
    await ev(p, () => { closeQuick(true); settings.quickClassic = false; saveSettings(); openQuick(); }); await p.waitForTimeout(500);
    const ns = await ev(p, () => !!document.querySelector('#quick.ns .nqlist'));
    return th[0] === 'auto' && th2 === 'light' && classic && ns ? { p } : { p, fail: JSON.stringify({ th, th2, classic, ns }) };
  });

  await run('Apps del Inicio (Steam, Armoury Crate, Álbum, Amigos, Controles, Logros, Ajustes, Apagar)', async () => {
    const p = await page(); await dismiss(p);
    const labels = await ev(p, () => APPS.map(a => a.label).join());
    if (labels !== 'Steam,Armoury Crate,Álbum,Amigos,Controles,Logros,Ajustes,Apagar') return { p, fail: 'barra: ' + labels };
    const imgs = await ev(p, () => [...document.querySelectorAll('#apps .app .appimg')].map(i => getComputedStyle(i).webkitMaskImage.startsWith('url("data:image/png')));
    if (imgs.length !== 2 || !imgs.every(Boolean)) return { p, fail: 'íconos de Steam/Armoury: ' + JSON.stringify(imgs) };
    await ev(p, () => APPS[0].act(document.querySelector('#apps .app'))); await p.waitForTimeout(900);
    const opened = (await ev(p, () => window.__qa.log.filter(x => x.type === 'system.open').map(x => x.payload.uri))).pop();
    if (opened !== 'steam://open/bigpicture') return { p, fail: 'Steam abrió: ' + opened };
    await ev(p, () => { if (mode === "game") goHome(); }); await p.waitForTimeout(500);
    const n = await ev(p, () => APPS.length);
    for (let i = 0; i < n; i++) {
      await ev(p, i => { closeAllViews(); if (mode !== "ui") { mode = "game"; goHome(); } APPS[i].act(document.querySelectorAll('#apps .app')[i]); }, i);
      await p.waitForTimeout(900);
      await keys(p, ['down', 'right', 'up', 'left'], 60);
      await ev(p, () => { closeAllViews(); if (mode === "game") goHome(); });
      await p.waitForTimeout(400);
    }
    return { p, note: n + ' apps' };
  });

  await run('Biblioteca: moverse, filtros, orden, grupos y propiedades', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => { home.zone = "games"; home.g = homeTiles().length - 1; updateFocus(); }); await keys(p, ['a'], 600);
    await keys(p, ['right', 'right', 'down', 'y', 'y', 'rb', 'rb', 'lb', 'x'], 150);
    await p.waitForTimeout(400);
    const inProps = await ev(p, () => !!topView() && !!topView().s);
    if (inProps) {
      const secs = await ev(p, () => topView().s.map(s => s.k));
      for (const s of secs) {
        if (['close', 'install', 'store', 'controls'].includes(s)) continue;
        await ev(p, s => { const v = views.find(x => x.s); while (topView() !== v) closeView(); v.i = v.s.findIndex(x => x.k === s); if (v.syncFromIndex) v.syncFromIndex(); updateFocus(); }, s);
        await keys(p, ['a'], 300);
        await ev(p, () => { const v = views.find(x => x.s); while (views.length && topView() !== v) closeView(); });
      }
      return { p, note: 'secciones: ' + secs.join(', ') };
    }
    return { p, note: 'sin propiedades (lista vacía)' };
  });

  await run('Teclado en pantalla: escribir, borrar, pegar y cancelar', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => openView(settingsView())); await p.waitForTimeout(300);
    const has = await ev(p, () => typeof accountsView === "function");
    if (!has) return { p, note: 'sin vista de cuentas' };
    await ev(p, () => openView(accountsView())); await p.waitForTimeout(600);
    await keys(p, ['a'], 400);
    await keys(p, ['right', 'a', 'a', 'down', 'a', 'x', 'y', 'b', 'b'], 120);
    return { p };
  });

  await run('Notificaciones: amigos, logros, capturas, descargas, mandos', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => {
      window.__emit("friend.online", { name: "Neo", game: "ELDEN RING" });
      window.__emit("achievement.unlocked", { appid: 3489700, name: "Logro", desc: "x" });
      window.__emit("capture.saved", { type: "image", path: "C:/a.png", url: "", game: "SB", date: "hoy" });
      window.__emit("download.progress", { appid: 1245620, progress: .4, kind: "Actualizando" });
      window.__emit("download.done", { appid: 1245620 });
      window.__emit("pad.connected", { key: "z", name: "8BitDo", type: "USB", player: 3, layout: "xbox" });
      window.__emit("pad.disconnected", { key: "z", name: "8BitDo" });
      window.__emit("profile.changed", { name: "Jhon", level: 32 });
      window.__emit("library.changed");
      window.__emit("display.changed", { tv: true }); window.__emit("display.changed", { tv: false });
    });
    await p.waitForTimeout(1200);
    return { p, note: (await ev(p, () => document.querySelectorAll('#notifs .notif').length)) + ' avisos en pantalla (máx 3)' };
  });

  await run('Suspender, pantalla de bloqueo y desbloquear', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => sleepConsole("sleep")); await p.waitForTimeout(600);
    await ev(p, () => window.__emit("system.resumed")); await p.waitForTimeout(800);
    const m = await ev(p, () => mode);
    await keys(p, ['a', 'a', 'a'], 150); await p.waitForTimeout(1200);
    const m2 = await ev(p, () => mode);
    return m2 === 'ui' ? { p, note: m + ' → ' + m2 } : { p, fail: m + ' → ' + m2 };
  });

  await run('Apagar…: menú de energía (cancelar)', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => window.__emit("ui.sleep")); await p.waitForTimeout(900);
    const open = await ev(p, () => !!(topView() && topView().isPower));
    await keys(p, ['b']); await p.waitForTimeout(300);
    return open ? { p } : { p, fail: 'no abrió' };
  });

  await run('Actualizaciones: buscar e instalar con error', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => openView(updateView())); await p.waitForTimeout(1200);
    await keys(p, ['a'], 1200);
    return { p, note: (await ev(p, () => topView()?.el.textContent.slice(0, 90))) };
  });

  await run('Diagnóstico: abre, se desplaza y actualiza', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => openView(diagView())); await p.waitForTimeout(700);
    await keys(p, ['down', 'down', 'up', 'a'], 200);
    const t = await ev(p, () => topView().el.querySelectorAll('dt').length);
    return t > 2 ? { p, note: t + ' datos' } : { p, fail: 'vacío' };
  });

  await run('Paquete de sonidos: importar y quitar', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => { openView(settingsView()); soundPackMenu(); }); await p.waitForTimeout(500);
    await ev(p, () => document.querySelectorAll('.dialog .dbtns button')[0].click()); await p.waitForTimeout(1000);
    const a = await ev(p, () => soundPack);
    await ev(p, () => { soundPackMenu(); }); await p.waitForTimeout(500);
    await ev(p, () => document.querySelectorAll('.dialog .dbtns button')[1].click()); await p.waitForTimeout(600);
    const b = await ev(p, () => soundPack);
    return a && !b ? { p, note: 'importó «' + a + '» y lo quitó' } : { p, fail: JSON.stringify([a, b]) };
  });

  await run('Pantalla completa exclusiva: aviso "Hacer compatible" una sola vez', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => window.__emit("game.exclusive", { appid: 3489700 })); await p.waitForTimeout(1300);
    const a = await ev(p, () => !!document.querySelector('.dialog'));
    await keys(p, ['b']); await p.waitForTimeout(300);
    await ev(p, () => window.__emit("game.exclusive", { appid: 3489700 })); await p.waitForTimeout(1300);
    const b = await ev(p, () => !!document.querySelector('.dialog'));
    return a && !b ? { p } : { p, fail: JSON.stringify([a, b]) };
  });

  await run('Sin anfitrión responsivo: TODO falla y la interfaz sigue viva', async () => {
    const p = await page({ init: () => { const t = setInterval(() => { if (window.__qa) { ["steam.games","steam.profile","steam.friends","pads.list","system.state","app.info","game.state","sounds.list","album.list","quick.live","media.state","diag","update.check"].forEach(x => window.__qa.fail.add(x)); clearInterval(t); } }, 1); } });
    await dismiss(p); await p.waitForTimeout(600);
    await keys(p, ['right', 'down', 'a'], 300);
    await ev(p, () => { closeAllViews(); openQuick(); }); await p.waitForTimeout(600);
    await keys(p, ['down', 'right', 'b'], 100);
    return { p, note: 'modo ' + await ev(p, () => mode) };
  });

  await run('Estrés: 600 botones al azar por toda la interfaz', async () => {
    const p = await page(); await dismiss(p);
    await ev(p, () => {
      const ks = ["up", "down", "left", "right", "a", "b", "x", "y", "lb", "rb", "start"];
      let seed = 7; const rnd = () => (seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648;
      window.__stress = setInterval(() => {
        try {
          if (views.some(v => v.isPower) || (topView() && /Salir al escritorio/.test(topView().el.textContent) && topView().cls === "dialog")) { closeAllViews(); }
          const k = ks[Math.floor(rnd() * ks.length)];
          input(k);
        } catch (e) { (window.__stressErr = window.__stressErr || []).push(e.message); }
      }, 25);
    });
    await p.waitForTimeout(15000);
    const r = await ev(p, () => { clearInterval(window.__stress); return [mode, views.length, window.__stressErr || [], window.__qa.calls.filter(c => c === "app.exit" || c === "system.shutdown").length]; });
    if (r[2].length) return { p, fail: 'errores: ' + r[2].slice(0, 3).join(' | ') };
    return { p, note: 'terminó en modo ' + r[0] + ', ' + r[1] + ' pantallas abiertas' };
  });

  await run('Pantalla chica (TV / 1280×720) y modo claro: menú rápido cabe', async () => {
    const p = await page({ vp: { width: 1280, height: 720 } }); await dismiss(p);
    await ev(p, () => { settings.theme = "light"; applyTheme(); window.__qa.media = { app: "Apple Music", title: "Canción", artist: "A", playing: true, position: 1, duration: 100, now: Date.now() }; window.__qa.mixer = [{ key: "game", exe: "SB", name: "Stellar Blade", volume: 80, appid: 3489700 }, { key: "music", exe: "AppleMusic", name: "Apple Music", volume: 60, appid: 900002 }, { key: "app", exe: "msedge", name: "YouTube", volume: 40, appid: 900001 }]; openQuick(); });
    await p.waitForTimeout(900);
    await ev(p, () => { QM.i = qmRows().length - 1; renderQuick(); }); await p.waitForTimeout(200);
    const r = await ev(p, () => { const f = document.querySelector('#quick .nqfoot'), l = document.querySelector('#quick .nqlist'), last = document.querySelector('#quick .nqlist .focused'); const lr = l.getBoundingClientRect(), fr = last.getBoundingClientRect(); return [f.getBoundingClientRect().bottom <= innerHeight + 1, fr.bottom <= lr.bottom + 2 && fr.top >= lr.top - 2]; });
    await p.screenshot({ path: __dirname + '/tv_claro.png' });
    await ev(p, () => { closeQuick(true); settings.quickClassic = true; openQuick(); }); await p.waitForTimeout(700);
    const c = await ev(p, () => { const q = document.querySelector('#quick .qrows'); return [q.scrollHeight <= q.clientHeight + 2, q.getBoundingClientRect().bottom <= innerHeight + 1]; });   // beta 1.3: ya sin Apagar/Ajustes
    await ev(p, () => { settings.quickClassic = false; });
    return r[0] && r[1] && c[0] && c[1] ? { p } : { p, fail: 'no cabe: ' + JSON.stringify({ r, c }) };
  });

  await browser.close();
  const bad = results.filter(r => !r.ok);
  console.log(`\n${results.length - bad.length}/${results.length} escenarios OK`);
  require('fs').writeFileSync(__dirname + '/resultados.json', JSON.stringify(results, null, 1));
})();
