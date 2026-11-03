// Pruebas de VPN (WARP / WireGuard) y de "sin internet".
const { chromium } = require('playwright');
const URL = 'http://localhost:8799/index.html';
const results = [];
let browser;
async function page(opts = {}) {
  const p = await browser.newPage({ viewport: opts.vp || { width: 1280, height: 800 }, hasTouch: true });
  p.errs = [];
  p.on('pageerror', e => p.errs.push('JS: ' + e.message));
  p.on('console', m => { if (m.type() === 'error' && !/favicon|Failed to load resource|ERR_|net::/.test(m.text())) p.errs.push('consola: ' + m.text()); });
  // Estas pruebas revisan los cuadritos del menú rápido clásico (el nuevo tiene sus propias pruebas en beta.js).
  await p.addInitScript(() => { try { const s = JSON.parse(localStorage.getItem('scarlet.settings') || '{}'); s.quickClassic = true; localStorage.setItem('scarlet.settings', JSON.stringify(s)); } catch {} });
  if (opts.vpn) await p.addInitScript(v => { const t = setInterval(() => { if (window.__qa) { Object.assign(window.__qa.vpn, v); clearInterval(t); } }, 0); }, opts.vpn);
  await p.goto(URL + (opts.q || '')); await p.waitForTimeout(opts.wait || 3000);
  await p.evaluate(() => { if (mode === "boot") dismissBoot(); });
  await p.waitForTimeout(400);
  if (opts.setup) await p.evaluate(opts.setup);
  return p;
}
const ev = (p, f, a) => p.evaluate(f, a);
const keys = async (p, list, ms = 90) => { for (const k of list) { await p.evaluate(k => { input(k); }, k); await p.waitForTimeout(ms); } };
async function run(name, fn) {
  if (process.env.ONLY && !new RegExp(process.env.ONLY).test(name)) return;
  let p, ok = true, note = '';
  try {
    const r = await Promise.race([fn(), new Promise((_, rej) => setTimeout(() => rej(new Error('tardó más de 60 s')), 60000))]);
    p = r && r.p; note = (r && r.note) || ''; if (r && r.fail) { ok = false; note = r.fail; }
  } catch (e) { ok = false; note = 'excepción de la prueba: ' + e.message.split('\n')[0]; }
  const errs = p ? p.errs : [];
  if (errs.length) ok = false;
  results.push({ name, ok, note, errs: [...new Set(errs)].slice(0, 5) });
  if (p) await p.close().catch(() => {});
  console.log((ok ? 'OK   ' : 'FALLA') + ' ' + name + (note ? ' — ' + note : '') + (errs.length ? '\n        ' + [...new Set(errs)].slice(0, 3).join('\n        ') : ''));
}
const calls = (p, t) => ev(p, t => window.__qa.log.filter(x => x.type === t).map(x => x.payload), t);
const openVpnSettings = async p => { await ev(p, () => { openView(settingsView()); topView().goto('VPN'); }); await keys(p, ['a'], 600); };
const rowVal = (p, label) => ev(p, l => { const b = [...topView().el.querySelectorAll('.lrow')].find(x => x.querySelector('b').textContent === l); return b ? b.querySelector('.val').textContent : null; }, label);
const focusRow = (p, label) => ev(p, l => { const v = topView(); v.i = v.items().findIndex(x => x.querySelector('b').textContent === l); updateFocus(); }, label);
const qmTiles = p => ev(p, () => [...document.querySelectorAll('#quick .qpair .qtile')].map(t => t.querySelector('b').textContent + ':' + t.querySelector('.qtl span').textContent + (t.classList.contains('on') ? '*' : '')));
const selQ = (p, k) => ev(p, k => { QM.i = qmRows().findIndex(r => r.k === k); renderQuick(); }, k);

(async () => {
  browser = await chromium.launch();

  await run('Sin ninguna VPN: no hay cuadro VPN en el menú rápido y Ajustes › VPN dice qué instalar', async () => {
    const p = await page();
    await ev(p, () => openQuick()); await p.waitForTimeout(500);
    const t = await qmTiles(p);
    await ev(p, () => closeQuick(true)); await p.waitForTimeout(300);
    await openVpnSettings(p);
    const r = { vpn: await rowVal(p, 'VPN'), warp: await rowVal(p, 'Cloudflare WARP'), wg: await rowVal(p, 'WireGuard') };
    await focusRow(p, 'Cloudflare WARP'); await keys(p, ['a'], 300);
    const w = await calls(p, 'web.open');
    await focusRow(p, 'VPN'); await keys(p, ['a'], 300);
    const set = (await calls(p, 'vpn.set')).length, toast = await ev(p, () => $("#toast").textContent);
    if (t.some(x => /VPN/.test(x))) return { p, fail: 'apareció el cuadro VPN: ' + t };
    if (r.vpn !== 'Sin configurar' || r.warp !== 'Descargar ›' || r.wg !== 'Descargar ›') return { p, fail: JSON.stringify(r) };
    if (!w.length || w[0].url !== 'https://one.one.one.one/') return { p, fail: 'web.open ' + JSON.stringify(w) };
    return !set && /Primero instala/.test(toast) ? { p, note: t.join(' | ') } : { p, fail: JSON.stringify({ set, toast }) };
  });

  await run('WARP instalada: cuadro VPN junto a Wi-Fi y Modo avión; A conecta, chip "VPN" arriba; A desconecta', async () => {
    const p = await page({ vpn: { provider: "warp", warp: true } });
    await ev(p, () => openQuick()); await p.waitForTimeout(600);
    const t0 = await qmTiles(p), cols = await ev(p, () => getComputedStyle($('#quick .qpair')).gridTemplateColumns.split(' ').length);
    await selQ(p, 'vpn'); await keys(p, ['a'], 400);
    const t1 = await qmTiles(p), chip = await ev(p, () => !$(".vpnchip").hidden), set1 = await calls(p, 'vpn.set');
    await keys(p, ['a'], 400);
    const t2 = await qmTiles(p), chip2 = await ev(p, () => !$(".vpnchip").hidden);
    if (t0.join() !== 'Wi-Fi:Conectado*,Modo avión:Desactivado,VPN:Desconectada' || cols !== 3) return { p, fail: JSON.stringify({ t0, cols }) };
    if (!t1.includes('VPN:Conectada*') || !chip || set1[0].on !== true) return { p, fail: JSON.stringify({ t1, chip, set1 }) };
    return t2.includes('VPN:Desconectada') && !chip2 ? { p } : { p, fail: JSON.stringify({ t2, chip2 }) };
  });

  await run('Menú rápido: izquierda/derecha recorren los tres cuadros y arriba/abajo no se pierden', async () => {
    const p = await page({ vpn: { provider: "warp", warp: true } });
    await ev(p, () => openQuick()); await p.waitForTimeout(500);
    await selQ(p, 'wifi');
    const path = [];
    for (const k of ['right', 'right', 'right', 'left', 'down', 'up']) { await keys(p, [k], 80); path.push(await ev(p, () => qmRows()[QM.i].k)); }
    // beta 1.3: los cuadros son lo último del menú (ya no hay Apagar/Ajustes abajo): abajo no se mueve, arriba va al volumen
    return path.join() === 'airplane,vpn,vpn,airplane,airplane,volume' ? { p, note: path.join(' → ') } : { p, fail: path.join(' → ') };
  });

  await run('VPN lenta (2 s): dice "Conectando…" y apretar otra vez no manda otra orden', async () => {
    const p = await page({ vpn: { provider: "warp", warp: true }, setup: () => { window.__qa.slow["vpn.set"] = 2000; } });
    await ev(p, () => openQuick()); await p.waitForTimeout(500);
    await selQ(p, 'vpn'); await keys(p, ['a'], 200);
    const mid = await qmTiles(p);
    await keys(p, ['a', 'a'], 150);
    await p.waitForTimeout(2300);
    const end = await qmTiles(p), n = (await calls(p, 'vpn.set')).length;
    return mid.includes('VPN:Conectando…') && end.includes('VPN:Conectada*') && n === 1 ? { p } : { p, fail: JSON.stringify({ mid, end, n }) };
  });

  await run('La VPN falla (la red también la bloquea): avisa y el cuadro queda apagado', async () => {
    const p = await page({ vpn: { provider: "warp", warp: true, fail: "La VPN no terminó de conectar. Puede que esta red también la bloquee." } });
    await ev(p, () => openQuick()); await p.waitForTimeout(500);
    await selQ(p, 'vpn'); await keys(p, ['a'], 500);
    const t = await qmTiles(p), toast = await ev(p, () => $("#toast").textContent);
    return t.includes('VPN:Desconectada') && /también la bloquee/.test(toast) ? { p } : { p, fail: JSON.stringify({ t, toast }) };
  });

  await run('WireGuard: importar perfil desde Ajustes, prepararlo (permiso una vez) y conectar', async () => {
    const p = await page({ vpn: { provider: "none", wireguard: true, setupNeeded: true } });
    await openVpnSettings(p);
    const v0 = await rowVal(p, 'WireGuard');
    await focusRow(p, 'WireGuard'); await keys(p, ['a'], 600);
    const v1 = await rowVal(p, 'WireGuard'), use = await rowVal(p, 'Usar');
    await focusRow(p, 'VPN'); await keys(p, ['a'], 600);
    const dlg = await ev(p, () => topView().el.textContent);
    await ev(p, () => document.querySelectorAll('.dialog .dbtns button')[1].click()); await p.waitForTimeout(900);
    const setup = (await calls(p, 'vpn.setup')).length, sets = await calls(p, 'vpn.set'), val = await rowVal(p, 'VPN');
    if (v0 !== 'Importar perfil ›' || v1 !== 'ProtonVPN-MX-FREE-1' || !/WireGuard · ProtonVPN/.test(use)) return { p, fail: JSON.stringify({ v0, v1, use }) };
    if (!/UNA sola vez/.test(dlg)) return { p, fail: 'no explicó el permiso: ' + dlg.slice(0, 80) };
    return setup === 1 && sets.length === 2 && val === 'Conectada' ? { p, note: 'importó, pidió permiso una vez y conectó' } : { p, fail: JSON.stringify({ setup, sets: sets.length, val }) };
  });

  await run('WireGuard: no encontró perfil → aviso claro', async () => {
    const p = await page({ vpn: { provider: "none", wireguard: true, importFail: "No encontré un perfil de WireGuard (.conf) en Descargas, Escritorio o Documentos." } });
    await openVpnSettings(p); await focusRow(p, 'WireGuard'); await keys(p, ['a'], 600);
    const t = await ev(p, () => topView().el.textContent);
    return /No encontré un perfil/.test(t) ? { p } : { p, fail: t.slice(0, 120) };
  });

  await run('WireGuard: cancelar el permiso de Windows → no conecta y avisa', async () => {
    const p = await page({ vpn: { provider: "wireguard", wireguard: true, profile: "Perfil", setupNeeded: true, setupFail: "Cancelaste el permiso de Windows." } });
    await openVpnSettings(p); await focusRow(p, 'VPN'); await keys(p, ['a'], 500);
    await ev(p, () => document.querySelectorAll('.dialog .dbtns button')[1].click()); await p.waitForTimeout(700);
    const val = await rowVal(p, 'VPN'), toast = await ev(p, () => $("#toast").textContent);
    return val === 'Desconectada' && /Cancelaste/.test(toast) ? { p } : { p, fail: JSON.stringify({ val, toast }) };
  });

  await run('WireGuard sin preparar desde el menú rápido: cierra el menú y pide el permiso en la pantalla', async () => {
    const p = await page({ vpn: { provider: "wireguard", wireguard: true, profile: "Perfil", setupNeeded: true } });
    await ev(p, () => openQuick()); await p.waitForTimeout(500);
    await selQ(p, 'vpn'); await keys(p, ['a'], 700);
    const r = await ev(p, () => [QM.open, topView() && topView().cls, topView() && topView().el.textContent.slice(0, 160)]);
    return !r[0] && r[1] === 'dialog' && /Windows te pedirá permiso/.test(r[2]) ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Encima del juego (overlay): el cuadro VPN funciona; si falta el permiso, avisa que vayas a Ajustes', async () => {
    const p = await page({ q: '?overlay=quick', wait: 1500, vp: { width: 712, height: 1080 }, vpn: { provider: "warp", warp: true } });
    await ev(p, () => window.__emit("quick.open")); await p.waitForTimeout(800);
    await selQ(p, 'vpn'); await ev(p, () => window.__emit("pad.key", "a")); await p.waitForTimeout(500);
    const t = await qmTiles(p);
    await ev(p, () => { Object.assign(window.__qa.vpn, { provider: "wireguard", wireguard: true, profile: "P", setupNeeded: true, status: "off" }); Object.assign(VPN, { provider: "wireguard", status: "off" }); renderQuick(); });
    await selQ(p, 'vpn'); await ev(p, () => window.__emit("pad.key", "a")); await p.waitForTimeout(500);
    const toast = await ev(p, () => $("#toast").textContent);
    return t.includes('VPN:Conectada*') && /Ajustes › VPN/.test(toast) ? { p } : { p, fail: JSON.stringify({ t, toast }) };
  });

  await run('Cambiar entre WARP y WireGuard ("Usar"); con la VPN prendida pide apagarla antes', async () => {
    const p = await page({ vpn: { provider: "warp", warp: true, wireguard: true, profile: "Proton" } });
    await openVpnSettings(p);
    await focusRow(p, 'Usar'); await keys(p, ['a'], 400);
    const a = await rowVal(p, 'Usar');
    await focusRow(p, 'VPN'); await keys(p, ['a'], 400);
    await focusRow(p, 'Usar'); await keys(p, ['a'], 400);
    const b = await rowVal(p, 'Usar'), toast = await ev(p, () => $("#toast").textContent);
    return /WireGuard · Proton/.test(a) && b === a && /Apaga la VPN/.test(toast) ? { p } : { p, fail: JSON.stringify({ a, b, toast }) };
  });

  await run('Quitar el perfil de WireGuard regresa a WARP', async () => {
    const p = await page({ vpn: { provider: "wireguard", warp: true, wireguard: true, profile: "Proton" } });
    await openVpnSettings(p); await focusRow(p, 'WireGuard'); await keys(p, ['a'], 500);
    await ev(p, () => document.querySelectorAll('.dialog .dbtns button')[1].click()); await p.waitForTimeout(600);
    const r = { wg: await rowVal(p, 'WireGuard'), use: await rowVal(p, 'Usar') };
    return r.wg === 'Importar perfil ›' && r.use === 'Cloudflare WARP' ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Se va el internet: aviso, Wi-Fi atenuado y "Sin internet" en el menú; regresa: aviso', async () => {
    const p = await page();
    await ev(p, () => window.__emit("net.state", { internet: false, steam: true })); await p.waitForTimeout(500);
    const a = await ev(p, () => [$('#notifs').textContent, getComputedStyle($('.status .wifi')).opacity]);
    await ev(p, () => openQuick()); await p.waitForTimeout(400);
    const t = await qmTiles(p);
    await ev(p, () => closeQuick(true));
    await ev(p, () => window.__emit("net.state", { internet: true, steam: true })); await p.waitForTimeout(500);
    const b = await ev(p, () => [$('#notifs').textContent, getComputedStyle($('.status .wifi')).opacity]);
    if (!/Sin conexión a internet/.test(a[0]) || +a[1] > .5) return { p, fail: JSON.stringify(a) };
    if (!t.includes('Wi-Fi:Sin internet*')) return { p, fail: JSON.stringify(t) };
    return /Conectado a internet/.test(b[0]) && +b[1] === 1 ? { p } : { p, fail: JSON.stringify(b) };
  });

  await run('Red que bloquea Steam: sugiere la VPN (o instalar una si no hay)', async () => {
    const p1 = await page({ vpn: { provider: "warp", warp: true } });
    await ev(p1, () => window.__emit("net.state", { internet: true, steam: false })); await p1.waitForTimeout(500);
    const a = await ev(p1, () => $('#notifs').textContent);
    const e1 = p1.errs.slice(); await p1.close();
    const p = await page();
    await ev(p, () => window.__emit("net.state", { internet: true, steam: false })); await p.waitForTimeout(500);
    const b = await ev(p, () => $('#notifs').textContent);
    p.errs.push(...e1);
    return /bloquea Steam/.test(a) && /menú rápido/.test(a) && /Ajustes › VPN/.test(b) ? { p } : { p, fail: JSON.stringify({ a, b }) };
  });

  await run('Steam arrancó sin internet: aviso de qué tocar', async () => {
    const p = await page();
    await ev(p, () => window.__emit("steam.offline", { title: "Steam" })); await p.waitForTimeout(500);
    const n = await ev(p, () => $('#notifs').textContent);
    return /Iniciar en modo sin conexión/.test(n) ? { p } : { p, fail: n };
  });

  await run('Steam se reinicia para reconectarse: avisos de "Reconectando", "en línea" y "al cerrar el juego"', async () => {
    const p = await page();
    const seen = [];
    for (const st of ["restarting", "done", "later", "failed"]) {
      await ev(p, st => window.__emit("steam.reconnect", { state: st }), st); await p.waitForTimeout(350);
      seen.push(await ev(p, () => $('#notifs').textContent));
    }
    const ok = /Reconectando Steam/.test(seen[0]) && /Steam en línea/.test(seen[1]) && /cierres el juego/.test(seen[2]) && /no terminó/.test(seen[3]);
    return ok ? { p } : { p, fail: JSON.stringify(seen) };
  });

  await run('Pantalla de TV 1280×720, modo claro, música y 3 apps: el menú con VPN cabe', async () => {
    const p = await page({ vp: { width: 1280, height: 720 }, vpn: { provider: "warp", warp: true, status: "on" } });
    await ev(p, () => { settings.theme = "light"; applyTheme(); window.__qa.media = { app: "Apple Music", title: "Canción", artist: "A", playing: true, position: 1, duration: 100, now: Date.now() }; window.__qa.mixer = [{ key: "game", exe: "SB", name: "Stellar Blade", volume: 80, appid: 3489700 }, { key: "music", exe: "AppleMusic", name: "Apple Music", volume: 60, appid: 900002 }, { key: "app", exe: "msedge", name: "YouTube", volume: 40, appid: 900001 }]; openQuick(); });
    await p.waitForTimeout(900);
    const r = await ev(p, () => { const q = document.querySelector('#quick .qrows'); const tiles = [...document.querySelectorAll('#quick .qpair .qtile')].map(t => t.scrollWidth <= t.clientWidth + 1); return [q.scrollHeight <= q.clientHeight + 2, q.getBoundingClientRect().bottom <= innerHeight + 1, tiles]; });
    await p.screenshot({ path: __dirname + '/vpn_tv_claro.png' });
    return r[0] && r[1] && r[2].every(Boolean) ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Estrés: 300 botones al azar en el menú rápido con la VPN', async () => {
    const p = await page({ vpn: { provider: "warp", warp: true } });
    const r = await ev(p, async () => {
      const ks = ["up", "down", "left", "right", "a", "b", "lb", "rb", "guide"]; let seed = 5, errs = [];
      const rnd = () => (seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648;
      for (let n = 0; n < 300; n++) {
        try {
          if (views.some(v => v.isPower) || mode !== "ui") { closeAllViews(); mode = "ui"; }
          if (!QM.open && n % 20 === 0) openQuick();
          const k = ks[Math.floor(rnd() * ks.length)];
          if (QM.open) qmInput(k === "guide" ? "b" : k); else input(k === "guide" ? "b" : k);
        } catch (e) { errs.push(e.message); }
        await new Promise(r => setTimeout(r, 25));
      }
      await new Promise(r => setTimeout(r, 800));
      return { errs, busy: VPN.busy };
    });
    return r.errs.length ? { p, fail: r.errs.slice(0, 3).join(' | ') } : !r.busy ? { p } : { p, fail: 'VPN quedó ocupada' };
  });

  await browser.close();
  const bad = results.filter(r => !r.ok);
  console.log(`\n${results.length - bad.length}/${results.length} escenarios de VPN y red OK`);
})();
