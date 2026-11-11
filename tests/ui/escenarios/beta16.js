// Pruebas de la beta 1.6: logros estilo Xbox (normal, raro y 100 %), notificaciones en cápsula, sonidos de logros y
// el tamaño y la posición del logo de la pantalla del juego.
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
// Un juego de Steam con logros (para los avisos)
const SB = 3489700;
// Sonidos de logros de mentira (buffers de silencio) y un espía de Snd.sample
const spySound = p => ev(p, () => {
  const c = Snd.ensure(); window.__snd = [];
  for (const k of ["achieve", "achrare", "ach100", "xachrare"]) Snd.custom[k] = c.createBuffer(1, 4800, 48000);
  const orig = Snd.sample.bind(Snd); Snd.sample = (n, r, v, d) => { window.__snd.push([n, v, !!d]); return orig(n, r, v, d); };
});

(async () => {
  browser = await chromium.launch();

  await run('Logro normal (menú): disco con el botón Xbox → trofeo → barra; dura 2.8 s y suena al 25 %', async () => {
    const p = await page(); await spySound(p);
    await ev(p, a => window.__emit("achievement.unlocked", { appid: a, name: "Primer paso", desc: "x", percent: 48.3, unlocked: 12, total: 40 }), SB);
    await p.waitForTimeout(150);
    const k = await ev(p, () => [Ach.cur && Ach.cur.kind, !!document.querySelector('.ach.k-normal .axb')]);
    const f0 = await ev(p, () => { Ach.seek(.2); return getComputedStyle($('.ach .apill')).visibility; });
    const f1 = await ev(p, () => { const f = Ach.seek(1.4); return [f.w, f.tx[0], $('.ach .atx').textContent, $('.ach .aflip').style.transform]; });
    const snd = await ev(p, () => window.__snd);
    await ev(p, () => { Ach.cur.manual = false; Ach.cur.start = performance.now() - 2700; requestAnimationFrame(function s() { if (Ach.cur) { const t = (performance.now() - Ach.cur.start) / 1000; if (t >= 2.8) Ach.done(Ach.cur); else requestAnimationFrame(s); } }); });
    await p.waitForTimeout(500);
    const gone = await ev(p, () => [!document.querySelector('.ach'), !Ach.cur]);
    const ok = k[0] === 'normal' && k[1] && f0 === 'hidden' && f1[0] === 1 && f1[1] === 1 && /Logro desbloqueado/.test(f1[2]) && /Primer paso · Stellar Blade/.test(f1[2]) && /180/.test(f1[3])
      && snd.length === 1 && snd[0][0] === 'achieve' && snd[0][1] === .25 && snd[0][2] && gone[0] && gone[1] && await ev(p, () => ACH_MS.normal) === 2800;
    return ok ? { p } : { p, fail: JSON.stringify({ k, f0, f1, snd, gone }) };
  });

  await run('Logro raro (< 10 %): diamante que gira, barra azul con destellos, «4.2 % lo tiene»; 6.0 s al 50 %', async () => {
    const p = await page(); await spySound(p);
    await ev(p, a => window.__emit("achievement.unlocked", { appid: a, name: "Sin un rasguño", desc: "x", percent: 4.2, unlocked: 13, total: 40 }), SB);
    await p.waitForTimeout(150);
    const r = await ev(p, () => {
      const kind = Ach.cur.kind;
      Ach.seek(.5); const front = $('.ach .aflip').style.transform;
      Ach.seek(2.6); const gem1 = $('.ach .agem').innerHTML; const n = $$('.ach .agem polygon').length;
      Ach.seek(3.2); const gem2 = $('.ach .agem').innerHTML; const shine = $('.ach .ashine').style.display;
      const txt = $('.ach .atx').textContent;
      Ach.seek(5.9); const end = $('.ach .adisc').style.transform;
      return { kind, front, n, spins: gem1 !== gem2, shine, txt, end, ms: ACH_MS.rare };
    });
    const snd = await ev(p, () => window.__snd);
    const ok = r.kind === 'rare' && /rotateY\(0/.test(r.front) && r.n >= 10 && r.spins && r.shine === '' && /Logro raro desbloqueado/.test(r.txt) && /4\.2 % lo tiene/.test(r.txt)
      && /scale\(0\.[0-3]/.test(r.end) && r.ms === 6000 && snd[0][0] === 'achrare' && snd[0][1] === .5;
    return ok ? { p } : { p, fail: JSON.stringify({ r, snd }) };
  });

  await run('Juego al 100 %: un solo aviso con el último logro, luego «Juego completado» y «Desbloqueaste los N logros»; 7.8 s al 75 %', async () => {
    const p = await page(); await spySound(p);
    await ev(p, a => window.__emit("achievement.unlocked", { appid: a, name: "Protocolo EVE", desc: "x", percent: 3.1, unlocked: 40, total: 40 }), SB);
    await p.waitForTimeout(150);
    const r = await ev(p, () => {
      const vis = () => $$('.ach .atx').filter(x => +x.style.opacity > .9).map(x => x.textContent.trim()).join('|');
      const kind = Ach.cur.kind, q = Ach.q.length;
      Ach.seek(1.2); const shards = $$('.ach .ashards g').length;
      Ach.seek(2.6); const a = vis(), icon = +$('.ach .aicon').style.opacity, halo = +$('.ach .ahalo').style.opacity;
      Ach.seek(4.3); const b = vis(), emb = +$('.ach .aemb').style.opacity;
      Ach.seek(5.8); const c = vis();
      return { kind, q, shards, a, icon, halo, b, emb, c, n: $$('.ach').length };
    });
    const snd = await ev(p, () => window.__snd);
    const ok = r.kind === 'full' && r.q === 0 && r.shards === 11 && /Protocolo EVE/.test(r.a) && /3\.1 % lo tiene/.test(r.a) && r.icon === 1 && r.halo > 0
      && /Juego completado al 100 %/.test(r.b) && /Stellar Blade/.test(r.b) && r.emb === 1 && /Desbloqueaste los 40 logros/.test(r.c) && r.n === 1
      && snd.length === 1 && snd[0][0] === 'ach100' && snd[0][1] === .75;
    return ok ? { p } : { p, fail: JSON.stringify({ r, snd }) };
  });

  await run('Jugando: el logro lo dibuja la app encima del juego (ach.show) con su tipo y posición; los juegos de Xbox no se tocan', async () => {
    const p = await page();
    await ev(p, () => launchGame(games.find(g => g.name === "Stellar Blade"))); await p.waitForTimeout(2600);
    const m = await ev(p, () => mode);
    await ev(p, a => { settings.achPos = "tr"; window.__emit("achievement.unlocked", { appid: a, name: "Sin un rasguño", desc: "x", percent: 4.2, unlocked: 13, total: 40, iconPath: "C:/c/ach.png" }); }, SB);
    await p.waitForTimeout(300);
    const shown = await calls(p, 'ach.show'), dom = await ev(p, () => !!document.querySelector('.ach'));
    await ev(p, () => { Ach.done(Ach.cur); const gx = games.find(g => g.source === "xbox") || { appid: 1, name: "X", source: "xbox" }; notifyAchievement(gx, "Logro de Xbox", { percent: 2 }); });
    await p.waitForTimeout(300);
    const after = await calls(p, 'ach.show'), cur = await ev(p, () => !!Ach.cur);
    const s = shown[0] || {};
    const ok = m === 'game' && shown.length === 1 && s.kind === 'rare' && s.name === 'Sin un rasguño' && s.pos === 'tr' && s.percent === 4.2 && s.image === 'C:/c/ach.png' && !dom && after.length === 1 && !cur;
    return ok ? { p, note: 'jugando: ' + JSON.stringify(s) } : { p, fail: JSON.stringify({ m, shown, dom, after, cur }) };
  });

  await run('Posición de los logros (Ajustes): abajo a la derecha, y la barra crece hacia la izquierda', async () => {
    const p = await page({ vp: { width: 1920, height: 1080 } });
    await ev(p, () => { openView(settingsView()); topView().goto("Posición de los logros"); });
    const v0 = await ev(p, () => $('.nsrow.focused .nv').textContent);
    await keys(p, ['a'], 500);
    await ev(p, () => { const v = topView(); v.i = 5; updateFocus(); }); await keys(p, ['a'], 400);
    const v1 = await ev(p, () => [settings.achPos, $('.nsrow.focused .nv').textContent]);
    await ev(p, () => { closeAllViews(); demoAchievement("rare"); }); await p.waitForTimeout(100);
    const r = await ev(p, () => {
      Ach.seek(1); const a = $('.ach .adisc').getBoundingClientRect();
      Ach.seek(3); const b = $('.ach .adisc').getBoundingClientRect(), pill = $('.ach .apill').getBoundingClientRect();
      return { a: [a.right, a.bottom], b: [b.left, b.right], pill: pill.right, W: innerWidth, H: innerHeight, m: 2.4 * rem() };
    });
    const ok = v0 === 'Arriba a la izquierda' && v1[0] === 'br' && v1[1] === 'Abajo a la derecha' && Math.abs(r.a[0] - (r.W - r.m)) < 3 && Math.abs(r.a[1] - (r.H - r.m)) < 3
      && r.b[0] < r.a[0] - 300 && Math.abs(r.pill - (r.W - r.m)) < 3;
    return ok ? { p } : { p, fail: JSON.stringify({ v0, v1, r }) };
  });

  await run('Mientras se ve un logro, las notificaciones esperan; después salen en cápsula con su etiqueta y color', async () => {
    const p = await page();
    await ev(p, a => { window.__emit("achievement.unlocked", { appid: a, name: "Primer paso", desc: "x", percent: 50, unlocked: 12, total: 40 }); window.__emit("friend.online", { name: "Neo", game: "ELDEN RING" }); }, SB);
    await p.waitForTimeout(400);
    const during = await ev(p, () => [!!Ach.cur, $$('#notifs .notif').length]);
    await p.waitForTimeout(3200);
    const after = await ev(p, () => { const n = $('#notifs .notif'); return n ? [n.querySelector('.ntag') && n.querySelector('.ntag').textContent, n.style.getPropertyValue('--nac'), getComputedStyle(n.querySelector('.ic')).borderRadius] : null; });
    const ok = during[0] && during[1] === 0 && after && after[0] === 'Jugando' && after[1] === '#3ddc84' && after[2] === '50%';
    return ok ? { p } : { p, fail: JSON.stringify({ during, after }) };
  });

  await run('Notificaciones apiladas: las que esperan se asoman detrás con +N', async () => {
    const p = await page();
    await ev(p, () => { window.__emit("friend.online", { name: "Neo", game: "" }); notify({ kind: "dl", icon: I.pad, title: "Hades II", text: "Listo" }); notifyBattery(.15); });
    await p.waitForTimeout(500);
    const r = await ev(p, () => [$$('#notifs .nghost').length, ($('#notifs .nmore') || {}).textContent, $$('#notifs .notif').length]);
    return r[0] === 2 && r[1] === '+2' && r[2] === 1 ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Sonidos de logros: Scarlet o Personalizados (importados), volumen por tipo e importar', async () => {
    const p = await page(); await spySound(p);
    const r = await ev(p, async () => {
      const out = {};
      achSound("rare"); out.def = window.__snd.pop();
      settings.achSounds = "custom"; achSound("rare"); out.custom = window.__snd.pop();
      achSound("full"); out.fallback = window.__snd.pop();                       // sin xach100 importado: el de Scarlet
      settings["achVol_rare"] = 0; achSound("rare"); out.off = window.__snd.length;
      settings.achSounds = "scarlet"; delete settings["achVol_rare"];
      return out;
    });
    await ev(p, () => { openView(settingsView()); topView().goto("Importar sonidos de logros"); });
    await keys(p, ['a'], 700);
    const imp = await ev(p, () => [settings.achSounds, window.__qa.calls.includes("sounds.importAch")]);
    await ev(p, () => topView().goto("Sonidos de logros"));
    const val = await ev(p, () => $('.nsrow.focused .nv').textContent);
    const rows = await ev(p, () => ["Volumen: logro", "Volumen: logro raro", "Volumen: juego al 100 %", "Probar: logro raro"].map(l => $$('.nsrow .nl').some(b => b.textContent === l)));
    const ok = r.def[0] === 'achrare' && r.def[1] === .5 && r.custom[0] === 'xachrare' && r.fallback[0] === 'ach100' && r.off === 0 && imp[0] === 'custom' && imp[1] && val === 'Personalizados' && rows.every(Boolean);
    return ok ? { p } : { p, fail: JSON.stringify({ r, imp, val, rows }) };
  });

  await run('Logo de la pantalla del juego: ◀ ▶ tamaño (50–150 %), Y posición; la vista previa es la pantalla real escalada', async () => {
    const p = await page({ vp: { width: 1920, height: 1080 } });
    await ev(p, a => { openView(propsView(byId(a))); const v = topView(); v.i = v.s.findIndex(s => s.k === "logo"); updateFocus(); }, SB);
    await p.waitForTimeout(500);
    const hints = await ev(p, () => $('#hints') ? $('#hints').textContent : '');
    await keys(p, ['right', 'right'], 200);
    await keys(p, ['y'], 200);
    const r = await ev(p, a => {
      const st = $('.gprev .gpstage'), gp = $('.gprev');
      return { look: settings.logoLook[a], lsz: st.style.getPropertyValue('--lsz'), lpos: st.dataset.lpos, scale: +st.style.transform.match(/scale\(([\d.]+)/)[1], want: gp.clientWidth / innerWidth,
        txt: $('.glook').textContent };
    }, SB);
    for (let i = 0; i < 10; i++) await keys(p, ['left'], 60);
    const min = await ev(p, a => settings.logoLook[a].s, SB);
    await ev(p, a => { closeAllViews(); settings.logoSize = 80; showGame(byId(a)); }, SB); await p.waitForTimeout(400);
    const game = await ev(p, () => [$('#game').dataset.lpos, $('#game').style.getPropertyValue('--lsz')]);
    await ev(p, () => { $('#game').className = ''; openView(settingsView()); topView().goto("Tamaño de los logos"); });
    const gv = await ev(p, () => $('.nsrow.focused .nv').textContent);
    const ok = /Tamaño/.test(hints) && /Posición/.test(hints) && r.look.s === 120 && r.look.p === 'bc' && r.lsz === '1.200' && r.lpos === 'bc' && Math.abs(r.scale - r.want) < .002
      && /120 %/.test(r.txt) && /Abajo al centro/.test(r.txt) && min === 50 && game[0] === 'bc' && game[1] === '0.400' && gv === '80 %';
    return ok ? { p } : { p, fail: JSON.stringify({ hints, r, min, game, gv }) };
  });

  // ───── 1.6.1 ─────
  await run('1.6.1: volver a un juego que ya se cerró no se queda esperando: se avisa y se quita el «Jugando»', async () => {
    const p = await page();
    await ev(p, () => launchGame(games.find(g => g.name === "Stellar Blade"))); await p.waitForTimeout(2600);
    await ev(p, () => goHome()); await p.waitForTimeout(800);
    await ev(p, () => { window.__qa.running = 0; });            // el juego se cerró y la interfaz no se enteró
    const before = await ev(p, () => !!state.running);
    await ev(p, () => resumeGame()); await p.waitForTimeout(600);
    const r = await ev(p, () => [mode, state.running, ($('#toast') || {}).textContent || '', !$('#game').classList.contains('show')]);
    const ok = before && r[0] === 'ui' && !r[1] && /ya se había cerrado/.test(r[2]) && r[3];
    return ok ? { p } : { p, fail: JSON.stringify({ before, r }) };
  });

  await run('1.6.1: si Big Picture se reabre solo, se vuelve a explicar cómo quitarlo aunque ya se hubiera dicho', async () => {
    const p = await page();
    await ev(p, () => { store.set("guidebpTold", 1); window.__emit("steam.guidebp", { changed: false }); });
    await p.waitForTimeout(300);
    const quiet = await ev(p, () => $$('#notifs .notif').length);
    await ev(p, () => window.__emit("steam.guidebp", { changed: false, loop: true })); await p.waitForTimeout(600);
    const t = await ev(p, () => ($('#notifs .notif') || {}).textContent || '');
    return quiet === 0 && /Guía enfoca Steam/.test(t) ? { p } : { p, fail: JSON.stringify({ quiet, t }) };
  });

  await browser.close();
  const bad = results.filter(r => !r.ok);
  console.log(`\n${results.length - bad.length}/${results.length} escenarios OK`);
})();
