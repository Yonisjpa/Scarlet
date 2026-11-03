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

  await run('Nube: el juego que abre espera la nube del otro (aviso en la carga, sin «Jugando»; barra naranja en el que sube)', async () => {
    const p = await page();
    await ev(p, () => { window.__qa.slow["game.launch"] = 0; });
    await ev(p, () => { window.__origLaunch = 1; launchGame(byId(3489700)); });
    await p.waitForTimeout(150);
    await ev(p, () => { window.__emit("game.cloudwait", { appid: 3489700, from: 1091500, on: true }); window.__emit("cloud.progress", { appid: 1091500, pct: -1 }); });
    await p.waitForTimeout(150);
    const mid = await ev(p, () => [$("#launch").classList.contains("cwait"), $("#launch .lcloud").textContent, getComputedStyle($("#launch .lcloud")).opacity, state.running]);
    await p.waitForTimeout(900);
    await ev(p, () => goHome()); await p.waitForTimeout(500);
    const bars = await ev(p, () => [!!$('[data-cl="1091500"]'), !!$('[data-cl="3489700"]')]);
    const after = await ev(p, () => $("#launch").classList.contains("cwait"));
    const ok = mid[0] && /Esperando la nube de Steam/.test(mid[1]) && /Cyberpunk/.test(mid[1]) && mid[3] !== 3489700 && bars[0] && !bars[1] && !after;
    return ok ? { p } : { p, fail: JSON.stringify({ mid, bars, after }) };
  });

  await run('«Jugando» solo cuando el juego ya está listo (game.state con ready=false no lo marca)', async () => {
    const p = await page();
    const r = await ev(p, async () => {
      const o = Host.call; let ready = false;
      Host.call = (t, a) => t === "game.state" ? Promise.resolve({ appid: 3489700, ready }) : o.call(Host, t, a);
      state.running = null; syncRunning(); await new Promise(res => setTimeout(res, 100)); const a = state.running;
      ready = true; syncRunning(); await new Promise(res => setTimeout(res, 100)); const b = state.running;
      Host.call = o; return [a, b];
    });
    return r[0] == null && r[1] === 3489700 ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Botón Xbox abrió Big Picture: aviso una sola vez con dónde apagarlo', async () => {
    const p = await page();
    await ev(p, () => { try { localStorage.removeItem("guidebpTold"); } catch (e) {} window.__emit("steam.guidebp", { changed: false }); });
    await p.waitForTimeout(400);
    const a = await ev(p, () => notifCur && [notifCur.title, notifCur.text]);
    await ev(p, () => { $('#notifs').innerHTML = ''; notifCur = null; window.__emit("steam.guidebp", { changed: false }); });
    await p.waitForTimeout(400);
    const b = await ev(p, () => notifCur && notifCur.title);
    return a && /Big Picture/.test(a[0]) && /Guía enfoca Steam/.test(a[1]) && !b ? { p } : { p, fail: JSON.stringify({ a, b }) };
  });

  await run('Música: el mismo estado repetido no rehace el menú ni vuelve a pedir la carátula', async () => {
    const p = await page();
    await ev(p, () => { openQuick(); });
    await p.waitForTimeout(500);
    const r = await ev(p, () => {
      let n = 0; const orig = window.renderQuick; window.renderQuick = function () { n++; return orig.apply(this, arguments); };
      const m = { app: "Apple Music", title: "A", artist: "B", art: "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg'/%3E", playing: true, position: 1, duration: 100, at: Date.now() };
      setMedia({ ...m }); const a = n; setMedia({ ...m, position: 5, at: Date.now() }); setMedia({ ...m, position: 9, at: Date.now() }); const b = n;
      setMedia({ ...m, title: "C" }); const c = n;
      window.renderQuick = orig; return [a, b, c];
    });
    return r[0] === 1 && r[1] === 1 && r[2] === 2 ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Menú rápido: una lectura que trae lo mismo no repinta', async () => {
    const p = await page();
    await ev(p, () => openQuick()); await p.waitForTimeout(2600);
    const r = await ev(p, async () => {
      let n = 0; const orig = window.renderQuick; window.renderQuick = function () { n++; return orig.apply(this, arguments); };
      await new Promise(res => { liveQuick(false); setTimeout(res, 300); }); const a = n;
      await new Promise(res => { liveQuick(false); setTimeout(res, 300); }); const b = n;
      window.renderQuick = orig; return [a, b];
    });
    return r[1] === r[0] && r[0] <= 1 ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Juego externo quitado: su portada elegida no la hereda otro', async () => {
    const p = await page();
    await ev(p, () => window.__qa.ext = [{ appid: 2000000001, name: "Viejo", source: "ext", kind: "game", installed: true, hours: 3, last: "ayer", lastTs: 1, ach: [0, 0], cols: ["Externos"], size: 0, path: "C:\\v.exe", args: "" }]);
    await ev(p, () => reloadLibrary()); await p.waitForTimeout(400);
    await ev(p, () => { Art.pick[2000000001] = "x"; Art.cache["grid:2000000001:0"] = "y"; extRemove(byId(2000000001)); });
    await p.waitForTimeout(400);
    await ev(p, () => { const v = topView(); v.i = 1; updateFocus(); }); await keys(p, ['a'], 500);
    const r = await ev(p, () => [!byId(2000000001), !(2000000001 in Art.pick), !Object.keys(Art.cache).some(k => k.includes(":2000000001:"))]);
    return r.every(Boolean) ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Dolby dorado: el brillo del logo se mueve con transform (GPU), mismo degradado', async () => {
    const p = await page();
    const r = await ev(p, () => { settings.dolbyGold = true; const d = document.createElement("div"); d.innerHTML = dolbyHTML(); document.body.append(d); const g = d.querySelector(".glg"); const b = getComputedStyle(g, "::before"); return [b.animationName, getComputedStyle(g).animationName, b.backgroundImage.includes("linear-gradient")]; });
    return r[0] === "dgoldT" && r[1] === "none" && r[2] ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Encima del juego: al abrir pide la música actual (la página estuvo congelada)', async () => {
    const p = await page({ q: '?overlay=quick', wait: 1500, vp: { width: 712, height: 1080 } });
    await ev(p, () => { window.__qa.media = { app: "Apple Music", title: "Nueva", artist: "X", playing: true, position: 1, duration: 100, now: Date.now() }; window.__emit("quick.open"); });
    await p.waitForTimeout(700);
    const r = await ev(p, () => [(window.__qa.log.filter(x => x.type === "media.state")).length, media && media.title]);
    return r[0] >= 1 && r[1] === "Nueva" ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Internet: redes registradas y encontradas, señal y candado, Y busca de nuevo', async () => {
    const p = await page();
    await ev(p, () => { openView(settingsView()); topView().goto("Internet"); });
    await keys(p, ['a'], 900);
    const r = await ev(p, () => { const v = topView(); return [v.isWifi, $(".vhead .sub", v.el).textContent, $$(".nssec", v.el).map(x => x.textContent), $$(".wrow", v.el).map(b => b.querySelector("b").textContent + (b.querySelector(".wlock") ? "🔒" : "") + b.querySelectorAll(".wbars path[opacity='1']").length)]; });
    const before = (await calls(p, 'wifi.list')).length;
    await keys(p, ['y'], 600);
    const scans = (await calls(p, 'wifi.list')).filter(x => x.scan).length;
    const ok = r[0] && r[1] === "Conectado a Casa_5G" && r[2].join() === "Conexión actual,Redes registradas,Redes encontradas,Más" && r[3].includes("Casa_5G🔒3") && r[3].includes("Vecino🔒2") && r[3].includes("Cafe libre1") && scans >= 1 && before >= 1;
    return ok ? { p } : { p, fail: JSON.stringify({ r, before, scans }) };
  });

  await run('Internet: conectarse con contraseña (símbolos, Mostrar, puntos), mala y luego buena, con la animación', async () => {
    const p = await page();
    await ev(p, () => openView(wifiView())); await p.waitForTimeout(700);
    await ev(p, () => { const v = topView(); v.i = v.rows.findIndex(r => r.net && r.net.ssid === "Vecino"); updateFocus(); });
    await keys(p, ['a'], 500);
    const kb = await ev(p, () => [topView().el.querySelector(".vhead").textContent]);
    for (const ch of "malamala") await ev(p, ch => topView().type(ch), ch);
    const dots = await ev(p, () => $(".kbtext", topView().el).textContent);
    await keys(p, ['y'], 200);
    const shown = await ev(p, () => $(".kbtext", topView().el).textContent);
    await keys(p, ['start'], 300);
    await p.waitForTimeout(900);
    const bad = await ev(p, () => [topView().cls, $(".wmsg", topView().el).textContent, !!$(".wlink.l1.bad", topView().el)]);
    await keys(p, ['a'], 500);
    await ev(p, () => { const v = topView(); v.i = v.rows.findIndex(r => r.net && r.net.ssid === "Vecino"); updateFocus(); });
    await keys(p, ['a'], 500);
    for (const ch of "clave") await ev(p, ch => topView().type(ch), ch);
    // página de símbolos: no se usa para la clave, pero se prueba que cambia y regresa
    await ev(p, () => { const v = topView(); const k = $$(".key", v.el).findIndex(b => b.textContent === "#+="); v.i = k; v.syncFromIndex(); updateFocus(); v.press(); });
    const sym = await ev(p, () => $$(".key", topView().el).some(b => b.textContent === "@") && $$(".key", topView().el).some(b => b.textContent === "abc"));
    await ev(p, () => { const v = topView(); const k = $$(".key", v.el).findIndex(b => b.textContent === "abc"); v.i = k; v.syncFromIndex(); updateFocus(); v.press(); });
    for (const ch of "1234") await ev(p, ch => topView().type(ch), ch);
    await keys(p, ['start'], 300);
    await p.waitForTimeout(1100);
    const good = await ev(p, () => [$(".wmsg", topView().el).textContent, $$(".wnode.ok", topView().el).length, !!$(".wlink.l2.ok", topView().el)]);
    await keys(p, ['a'], 900);
    const sub = await ev(p, () => $(".vhead .sub", topView().el).textContent);
    const ok = /Contraseña/.test(kb[0]) && dots === "••••••••" && shown === "malamala" && /contraseña/i.test(bad[1]) && bad[2] && sym && /Conexión establecida/.test(good[0]) && good[1] === 3 && good[2] && sub === "Conectado a Vecino";
    return ok ? { p } : { p, fail: JSON.stringify({ kb, dots, shown, bad, sym, good, sub }) };
  });

  await run('Internet: estado de la conexión, prueba de conexión y olvidar red', async () => {
    const p = await page();
    await ev(p, () => openView(wifiView())); await p.waitForTimeout(700);
    await ev(p, () => { const v = topView(); v.i = v.rows.findIndex(r => r.key === "status"); updateFocus(); });
    await keys(p, ['a'], 500);
    const st = await ev(p, () => $$(".kv dt", topView().el).map(x => x.textContent).join("|") + " " + $$(".kv dd", topView().el).map(x => x.textContent).join("|"));
    await keys(p, ['b'], 400);
    await ev(p, () => { const v = topView(); v.i = v.rows.findIndex(r => r.key === "test"); updateFocus(); });
    await keys(p, ['a'], 1300);
    const test = await ev(p, () => $(".wmsg", topView().el).textContent);
    await keys(p, ['a'], 600);
    await ev(p, () => { const v = topView(); v.i = v.rows.findIndex(r => r.net && r.net.ssid === "Oficina"); updateFocus(); });
    await keys(p, ['a'], 500);
    await ev(p, () => { const v = topView(); v.i = v.items().findIndex(b => /Olvidar/.test(b.textContent)); updateFocus(); });
    await keys(p, ['a'], 500);
    await ev(p, () => { const v = topView(); v.i = 1; updateFocus(); }); await keys(p, ['a'], 800);
    const gone = await ev(p, () => !topView().rows.some(r => r.net && r.net.ssid === "Oficina"));
    const ok = /Banda/.test(st) && /Canal/.test(st) && /Dirección IP/.test(st) && /Dirección MAC/.test(st) && /192\.168\.0\.20/.test(st) && /exitosa/.test(test) && gone;
    return ok ? { p } : { p, fail: JSON.stringify({ st, test, gone }) };
  });

  await run('Internet: sin permiso de ubicación se explica y se ofrece abrirlo', async () => {
    const p = await page();
    await ev(p, () => { window.__qa.wifiLoc = true; WIFI.data = null; openView(wifiView()); }); await p.waitForTimeout(800);
    const r = await ev(p, () => [$(".wbody", topView().el).textContent.includes("permiso de ubicación"), topView().rows.some(x => x.key === "loc")]);
    return r[0] && r[1] ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Perfil: dos columnas, tu código de amigo e historial de juego con horas', async () => {
    const p = await page();
    await ev(p, () => openView(profileView())); await p.waitForTimeout(700);
    const r = await ev(p, () => { const v = topView(); return [$$(".nscat", v.el).map(b => b.textContent).join("|"), $(".pcode", v.el).textContent, $$(".phist b", v.el).map(b => b.textContent).slice(0, 3).join("|"), $$(".phist .val", v.el).map(b => b.textContent).slice(0, 2).join("|")]; });
    const ok = r[0] === "Perfil|Lista de amigos|Añadir un amigo|Ajustes de usuario" && /3973 4321/.test(r[1]) && /^Stellar Blade\|ELDEN RING/.test(r[2]) && /^43 h\|131 h$/.test(r[3]);
    return ok ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Perfil › Lista de amigos: tarjetas (★ primero), moverse en cuadrícula, Y actualiza', async () => {
    const p = await page();
    await ev(p, () => { settings.bestFriends = ["Luna"]; openView(profileView()); });
    await p.waitForTimeout(500);
    await keys(p, ['down', 'right'], 300);
    const cards = await ev(p, () => $$(".fcardb b", topView().el).map(b => b.textContent));
    await keys(p, ['right'], 250);
    const foc = await ev(p, () => $(".fcardb.focused b", topView().el).textContent);
    await keys(p, ['y'], 600);
    const n = (await calls(p, 'steam.friends.refresh')).length;
    const after = await ev(p, () => $$(".fcardb", topView().el).length);
    await keys(p, ['a'], 900);
    const fp = await ev(p, () => [$(".fcard h2", topView().el).textContent, $(".fbest", topView().el).textContent, $(".frecent", topView().el).textContent]);
    await ev(p, () => $('[data-a="best"]', topView().el).click()); await p.waitForTimeout(200);
    const best = await ev(p, () => [isBest("NeoKaiser"), $(".fbest", topView().el).textContent]);
    const ok = cards[0] === "Luna★" && /NeoKaiser/.test(foc) && n === 1 && after === 3 && fp[0] === "NeoKaiser" && /Mejores amigos/.test(fp[1]) && /ELDEN RING/.test(fp[2]) && /6\.5 h/.test(fp[2]) && best[0] && /★ Mejor amigo/.test(best[1]);
    return ok ? { p } : { p, fail: JSON.stringify({ cards, foc, n, after, fp, best }) };
  });

  await run('Perfil › Añadir un amigo: buscar por código y mandar la solicitud con Steam', async () => {
    const p = await page();
    await ev(p, () => openView(profileView())); await p.waitForTimeout(500);
    await keys(p, ['down', 'down', 'right', 'a'], 300);
    for (const ch of "55555555") await ev(p, ch => topView().type(ch), ch);
    await keys(p, ['start'], 700);
    const dlg = await ev(p, () => topView().el.textContent);
    await ev(p, () => { const v = topView(); v.i = v.items().findIndex(b => /Enviar solicitud/.test(b.textContent)); updateFocus(); });
    await keys(p, ['a'], 600);
    const sent = (await calls(p, 'steam.addfriend')).pop();
    return /Mario/.test(dlg) && sent && sent.steamid === "76561198015820283" ? { p } : { p, fail: JSON.stringify({ dlg, sent }) };
  });

  await browser.close();
  const bad = results.filter(r => !r.ok);
  console.log(`\n${results.length - bad.length}/${results.length} escenarios OK`);
})();
