// Pruebas de "instalar directo con Steam" y de la animación de descarga.
const { chromium } = require('playwright');
const URL = 'http://localhost:8799/index.html';
const results = [];
let browser;
const CP = 1091500;   // Cyberpunk 2077 (no instalado en el anfitrión simulado)

async function page(opts = {}) {
  const p = await browser.newPage({ viewport: opts.vp || { width: 1280, height: 800 }, hasTouch: true });
  p.errs = [];
  p.on('pageerror', e => p.errs.push('JS: ' + e.message));
  p.on('console', m => { if (m.type() === 'error' && !/favicon|Failed to load resource|ERR_|net::/.test(m.text())) p.errs.push('consola: ' + m.text()); });
  if (opts.init) await p.addInitScript(opts.init);
  await p.goto(URL); await p.waitForTimeout(3000);
  await p.evaluate(() => { if (mode === "boot") dismissBoot(); });
  await p.waitForTimeout(400);
  if (opts.setup) await p.evaluate(opts.setup);
  return p;
}
const ev = (p, f, a) => p.evaluate(f, a);
const keys = async (p, list, ms = 90) => { for (const k of list) { await p.evaluate(k => input(k), k); await p.waitForTimeout(ms); } };
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
// abre el diálogo Instalar de Cyberpunk desde el Inicio (A sobre su mosaico)
async function openInstall(p) {
  await ev(p, id => { home.zone = "games"; home.g = homeGames().findIndex(g => g.appid === id); updateFocus(); }, CP);
  await p.waitForTimeout(150);
  await keys(p, ['a'], 500);
}
const top = p => ev(p, () => { const v = topView(); return v ? { inst: !!v.isInstall, phase: v.phase, cls: v.cls, text: v.el.textContent.replace(/\s+/g, ' ').slice(0, 300), focus: (v.items()[v.i] || {}).textContent } : null; });
const calls = (p, t) => ev(p, t => window.__qa.log.filter(x => x.type === t).map(x => x.payload), t);
const dl = (p, id = CP) => ev(p, id => { const d = state.downloads[id]; const el = document.querySelector(`#track [data-dl="${id}"]`); return d ? { ...d, strip: !!el, cls: el ? el.className : "", w: el ? el.querySelector("i").style.width : "", first: homeGames()[0].appid === id } : null; }, id);

(async () => {
  browser = await chromium.launch();

  await run('Instalar: A sobre un juego no instalado abre el diálogo con tamaño, discos y espacio libre', async () => {
    const p = await page(); await openInstall(p);
    const t = await top(p);
    if (!t || !t.inst) return { p, fail: 'no abrió el diálogo: ' + JSON.stringify(t) };
    const r = await ev(p, () => ({ size: $(".isz", topView().el).textContent, drives: $$(".idrv", topView().el).map(d => d.textContent), sel: $(".idrv.sel", topView().el)?.textContent, star: $$(".idrv .fr svg", topView().el).length, title: $(".dh", topView().el).textContent }));
    if (r.title !== 'Instalar' || r.size !== '70.20 GB' || r.drives.length !== 2 || !/Disco local \(C:\)/.test(r.sel) || r.star !== 1) return { p, fail: JSON.stringify(r) };
    if (t.focus !== 'Cancelar') return { p, fail: 'el foco empieza en ' + t.focus };
    return { p, note: r.drives.join(' | ') };
  });

  await run('Instalar: aceptar → franja de descarga, primer lugar, título con ícono y avance en vivo hasta terminar', async () => {
    const p = await page(); await openInstall(p);
    await keys(p, ['right', 'a'], 300); await p.waitForTimeout(500);
    const a = await dl(p);
    if (!a || a.st !== 'pending' || !a.strip || !a.first) return { p, fail: 'tras aceptar: ' + JSON.stringify(a) };
    const go = await calls(p, 'install.go');
    if (!go.length || go[0].folder !== 0) return { p, fail: 'install.go ' + JSON.stringify(go) };
    await ev(p, id => window.__emit("download.progress", { appid: id, progress: .3, kind: "Descargando", state: "active" }), CP); await p.waitForTimeout(700);
    const b = await dl(p);
    const title = await ev(p, () => ({ text: $("#gameTitle").textContent.trim(), ic: $("#gameTitle").dataset.ic, focusFirst: home.g === 0 }));
    if (b.w !== '30%' || title.text !== 'Cyberpunk 2077' || title.ic !== 'dl') return { p, fail: JSON.stringify({ b, title }) };   // el nombre: solo nombre e ícono
    const anim = await ev(p, id => getComputedStyle(document.querySelector(`#track [data-dl="${id}"]`)).animationName, CP);
    await ev(p, id => window.__emit("download.progress", { appid: id, progress: .3, kind: "Descargando", state: "queued" }), CP); await p.waitForTimeout(300);
    const q = await ev(p, id => document.querySelector(`#track [data-dl="${id}"]`).classList.contains('queued') ? 'En cola' : '', CP);
    await ev(p, id => window.__emit("download.progress", { appid: id, progress: .5, kind: "Descargando", state: "paused" }), CP); await p.waitForTimeout(300);
    const ps = await ev(p, id => [dlTitle(state.downloads[id]) === 'Descarga en pausa' ? 'En pausa' : '', getComputedStyle(document.querySelector(`#track [data-dl="${id}"]`)).animationPlayState], CP);
    await ev(p, id => window.__emit("download.done", { appid: id }), CP); await p.waitForTimeout(8000);   // el aviso espera unos segundos por si Steam sigue con el mismo juego
    const c = await ev(p, id => ({ d: !!state.downloads[id], inst: byId(id).installed, strip: !!document.querySelector(`[data-dl="${id}"]`), ic: $("#gameTitle").dataset.ic, first: homeGames()[0].appid === id, notif: document.querySelector('#notifs')?.textContent || '' }), CP);
    if (anim !== 'dlStripes' || q !== 'En cola' || ps[0] !== 'En pausa' || ps[1] !== 'paused') return { p, fail: JSON.stringify({ anim, q, ps }) };
    if (c.d || !c.inst || c.strip || c.ic !== 'card' || !c.first || !/Descarga completada/.test(c.notif)) return { p, fail: 'al terminar: ' + JSON.stringify(c) };
    return { p, note: 'Preparando → 30 % → En cola → En pausa → instalado, sigue primero' };
  });

  await run('Instalar: cancelar con B le dice a Steam que cancele y no deja rastro', async () => {
    const p = await page(); await openInstall(p);
    await keys(p, ['b'], 900);
    const r = await ev(p, id => ({ v: views.length, d: !!state.downloads[id], pinned: !!(settings.installedAt && settings.installedAt[id]), inst: installing }), CP);
    const c = await calls(p, 'install.cancel');
    return !r.v && !r.d && !r.pinned && !r.inst && c.length === 1 ? { p } : { p, fail: JSON.stringify({ r, cancel: c.length }) };
  });

  await run('Instalar: elegir el otro disco (arriba / abajo / A) y se instala ahí', async () => {
    const p = await page(); await openInstall(p);
    await keys(p, ['up'], 150);
    const f1 = (await top(p)).focus;
    await keys(p, ['down', 'a'], 300);
    const sel = await ev(p, () => $(".idrv.sel", topView().el).textContent);
    const fc = await calls(p, 'install.folder');
    await keys(p, ['down', 'a'], 400); await p.waitForTimeout(400);
    const go = await calls(p, 'install.go');
    if (!/C:/.test(f1)) return { p, fail: 'arriba desde los botones fue a ' + f1 };
    if (!/D:/.test(sel) || !fc.length || fc[0].folder !== 1) return { p, fail: JSON.stringify({ sel, fc }) };
    return go.length && go[0].folder === 1 ? { p, note: 'instaló en D:' } : { p, fail: 'install.go ' + JSON.stringify(go) };
  });

  await run('Sin espacio: avisa cuánto falta, no deja instalar y al elegir otro disco ya se puede', async () => {
    const p = await page({ setup: () => { window.__qa.inst.size = 150 * 2 ** 30; } });
    await openInstall(p);
    const w = await ev(p, () => [$(".iwarn", topView().el)?.textContent || '', $('[data-b="go"]', topView().el).className]);
    await keys(p, ['right', 'a'], 400);
    const go1 = (await calls(p, 'install.go')).length, still = (await top(p)) !== null;
    await keys(p, ['up', 'down', 'a'], 300);
    const w2 = await ev(p, () => $(".iwarn", topView().el)?.textContent || '');
    await keys(p, ['down', 'a'], 400); await p.waitForTimeout(300);
    const go2 = await calls(p, 'install.go');
    if (!/faltan 30\.00 GB/.test(w[0]) || !/off/.test(w[1])) return { p, fail: 'aviso: ' + JSON.stringify(w) };
    if (go1 || !still) return { p, fail: 'dejó instalar sin espacio' };
    return !w2 && go2.length === 1 && go2[0].folder === 1 ? { p, note: w[0] } : { p, fail: JSON.stringify({ w2, go2 }) };
  });

  await run('Sin espacio con un solo disco: no se puede elegir otro y no instala', async () => {
    const p = await page({ setup: () => { window.__qa.inst.size = 150 * 2 ** 30; window.__qa.inst.folders = [window.__qa.inst.folders[0]]; } });
    await openInstall(p);
    const r = await ev(p, () => ({ drivesFocusable: $$(".idrv.fi", topView().el).length, w: $(".iwarn", topView().el)?.textContent }));
    await keys(p, ['up', 'right', 'a'], 300);
    const go = (await calls(p, 'install.go')).length;
    return r.drivesFocusable === 0 && /faltan/.test(r.w) && !/Elige otro/.test(r.w) && !go ? { p } : { p, fail: JSON.stringify({ r, go }) };
  });

  const EULA_HTML = `<html><head><title>EULA</title><style>.x{}</style><script>window.HACK=1</script></head><body>
    <div id="global_header"><a>Tienda</a><a>Comunidad</a><a>Acerca de</a><a>Soporte</a></div>
    <div class="page_content"><div id="eula_content"><h1>ACUERDO DE USUARIO CD PROJEKT RED</h1><p>Última actualización: 17 JULIO 2025</p><p>En vigor desde el: 16 AGOSTO 2025</p>
    <h2>Lo primero es lo primero</h2><ol><li>¡Hola! ¡Me alegro de verte por aquí!</li><li>Lo que ves aquí son las normas que regulan el uso de nuestros servicios.</li></ol>
    ${'<p>Texto largo del contrato para probar que se puede desplazar con el mando. '.repeat(3) + '</p>'.repeat(1)}
    ${Array.from({ length: 40 }, (_, i) => `<p>Cláusula ${i + 1}: el juego se licencia, no se vende; <b>no</b> puedes revenderlo.</p>`).join('')}
    <img src="x" onerror="window.HACK=2"></div></div><div id="footer">© Valve Corporation. Todos los derechos reservados.</div></body></html>`;

  await run('Contrato de licencia: texto limpio (sin menús de Steam ni código), se lee con el mando y se acepta', async () => {
    const p = await page({ setup: new Function(`window.__qa.inst.go = { s: "eula", eulas: [{ id: "1091500_eula_0", version: 3, url: "https://store.steampowered.com/eula/1091500_eula_0", html: ${JSON.stringify(EULA_HTML)} }] };`) });
    await openInstall(p);
    await keys(p, ['right', 'a'], 300); await p.waitForTimeout(700);
    const t = await ev(p, () => { const v = topView(), e = $(".etext", v.el); return { title: $(".dh", v.el).textContent, text: e.textContent, h4: $$(".etext h4", v.el).map(x => x.textContent), top: e.scrollTop, focus: v.items()[v.i].textContent, hack: window.HACK }; });
    if (t.title !== 'Contrato de licencia') return { p, fail: 'no salió el contrato: ' + t.title };
    if (/Tienda|Comunidad|Valve Corporation/.test(t.text)) return { p, fail: 'se coló el menú/pie de Steam' };
    if (t.hack) return { p, fail: 'se ejecutó código de la página del contrato' };
    if (!/1\. ¡Hola! ¡Me alegro de verte/.test(t.text) || !/2\. Lo que ves/.test(t.text) || !/Cláusula 40/.test(t.text) || !t.h4.includes('ACUERDO DE USUARIO CD PROJEKT RED')) return { p, fail: 'texto incompleto: ' + t.text.slice(0, 200) };
    await keys(p, ['down', 'down', 'rb'], 450);
    const sc = await ev(p, () => $(".etext", topView().el).scrollTop);
    await keys(p, ['start', 'x', 'y'], 150);
    const still = await ev(p, () => topView() && !topView().isSettings && $(".dh", topView().el).textContent);
    await keys(p, ['a'], 600); await p.waitForTimeout(300);
    const e = await calls(p, 'install.eula');
    const d = await dl(p);
    if (!(sc > 200)) return { p, fail: 'no se desplazó: ' + sc };
    if (still !== 'Contrato de licencia') return { p, fail: 'Start/X/Y abrieron algo encima: ' + still };
    if (!e.length || !e[0].accept || e[0].eulas[0].id !== '1091500_eula_0' || e[0].eulas[0].version !== 3) return { p, fail: 'install.eula ' + JSON.stringify(e) };
    return d && d.strip ? { p, note: `foco inicial «${t.focus}», desplazó ${Math.round(sc)} px` } : { p, fail: 'no empezó la descarga' };
  });

  await run('Contrato: rechazar (B) cancela en Steam y no descarga', async () => {
    const p = await page({ setup: () => { window.__qa.inst.go = { s: "eula", eulas: [{ id: "e", version: 1, url: "", html: "<p>" + "Texto del contrato. ".repeat(20) + "</p>" }] }; } });
    await openInstall(p); await keys(p, ['right', 'a'], 300); await p.waitForTimeout(600);
    await keys(p, ['b'], 600);
    const e = await calls(p, 'install.eula'), d = await dl(p), v = await ev(p, () => views.length);
    return e.length === 1 && e[0].accept === false && !d && !v ? { p } : { p, fail: JSON.stringify({ e, d, v }) };
  });

  await run('Contrato sin texto (Steam no lo mandó): avisa y aún se puede aceptar', async () => {
    const p = await page({ setup: () => { window.__qa.inst.go = { s: "eula", eulas: [{ id: "e", version: 1, url: "https://store.steampowered.com/eula/x", html: "" }] }; } });
    await openInstall(p); await keys(p, ['right', 'a'], 300); await p.waitForTimeout(600);
    const t = await ev(p, () => $(".etext", topView().el).textContent);
    await keys(p, ['a'], 600);
    const d = await dl(p);
    return /No se pudo cargar el texto/.test(t) && /store\.steampowered\.com/.test(t) && d ? { p } : { p, fail: JSON.stringify({ t, d }) };
  });

  await run('Dos contratos seguidos (juego + extra): se muestran uno tras otro', async () => {
    const p = await page({ setup: () => {
      window.__qa.inst.go = { s: "eula", eulas: [{ id: "a", version: 1, url: "", html: "<p>" + "Primer contrato. ".repeat(10) + "</p>" }, { id: "b", version: 1, url: "", html: "<p>" + "Segundo contrato. ".repeat(10) + "</p>" }] };
      window.__qa.inst.eula = [{ s: "eula", eulas: [{ id: "c", version: 2, url: "", html: "<p>" + "Contrato de la expansión. ".repeat(10) + "</p>" }] }, { s: "started" }];
    } });
    await openInstall(p); await keys(p, ['right', 'a'], 300); await p.waitForTimeout(600);
    const h = await ev(p, () => $$(".etext h4", topView().el).map(x => x.textContent));
    await keys(p, ['a'], 900);
    const t2 = await ev(p, () => topView() && $(".etext", topView().el).textContent);
    await keys(p, ['a'], 700);
    const e = await calls(p, 'install.eula'), d = await dl(p);
    if (h.join() !== 'Contrato 1 de 2,Contrato 2 de 2') return { p, fail: 'encabezados ' + h };
    if (!/expansión/.test(t2 || '')) return { p, fail: 'no salió el segundo contrato' };
    return e.length === 2 && e[1].eulas[0].id === 'c' && d ? { p } : { p, fail: JSON.stringify({ e, d }) };
  });

  await run('Steam pide algo que Scarlet no maneja → PREGUNTA antes de abrir la ventana de Steam (Abrir Steam)', async () => {
    const p = await page({ setup: () => { window.__qa.inst.seq = [{ mode: "steam", why: "clave de producto" }]; } });
    await openInstall(p); await p.waitForTimeout(400);
    const t = await top(p), o0 = (await calls(p, 'steam.open')).length;
    await keys(p, ['right', 'a'], 400);
    const o = await calls(p, 'steam.open'), r = await ev(p, id => ({ v: views.length, first: homeGames()[0].appid === id }), CP);
    if (o0) return { p, fail: 'abrió Steam sin preguntar' };
    if (!t || !/ventana de Steam/.test(t.text) || t.focus !== 'Cancelar') return { p, fail: 'no preguntó: ' + JSON.stringify(t) };
    return o.length && /steam:\/\/install\/1091500/.test(o[0].url) && !r.v && r.first ? { p } : { p, fail: JSON.stringify({ o, r }) };
  });

  await run('Steam pide algo que Scarlet no maneja → Cancelar: Steam no se abre', async () => {
    const p = await page({ setup: () => { window.__qa.inst.seq = [{ mode: "steam", why: "x" }]; } });
    await openInstall(p); await p.waitForTimeout(400);
    await keys(p, ['a'], 400);
    const o = (await calls(p, 'steam.open')).length, r = await ev(p, id => [views.length, !!(settings.installedAt && settings.installedAt[id])], CP);
    return !o && !r[0] && !r[1] ? { p } : { p, fail: JSON.stringify({ o, r }) };
  });

  await run('Cancelar mientras dice "Preparando con Steam…": cierra al instante y cancela en Steam', async () => {
    const p = await page({ setup: () => { window.__qa.slow["install.prepare"] = 4000; } });
    await openInstall(p);
    const t0 = await top(p);
    await keys(p, ['b'], 250);
    const a = await ev(p, () => [views.length, installing]);
    await p.waitForTimeout(4200);
    const c = (await calls(p, 'install.cancel')).length, v = await ev(p, () => views.length);
    const p2ok = t0 && t0.phase === 'load';
    if (!p2ok) return { p, fail: 'no estaba preparando: ' + JSON.stringify(t0) };
    return a[0] === 0 && a[1] === 0 && c === 2 && v === 0 ? { p, note: 'se cerró en < 250 ms; Steam canceló al contestar' } : { p, fail: JSON.stringify({ a, c, v }) };
  });

  await run('Botón Cancelar (A) mientras prepara también cierra al instante', async () => {
    const p = await page({ setup: () => { window.__qa.slow["install.prepare"] = 4000; } });
    await openInstall(p);
    await keys(p, ['a'], 250);
    const a = await ev(p, () => views.length);
    await keys(p, ['right', 'a'], 150);   // ya no hay diálogo: no debe pasar nada raro
    return a === 0 ? { p } : { p, fail: 'sigue abierto' };
  });

  await run('El anfitrión falla al preparar → pregunta si abrir Steam (no lo abre solo)', async () => {
    const p = await page({ setup: () => { window.__qa.inst.seq = ["throw"]; } });
    await openInstall(p); await p.waitForTimeout(400);
    const o = (await calls(p, 'steam.open')).length, t = await top(p);
    return !o && t && /ventana de Steam/.test(t.text) ? { p } : { p, fail: JSON.stringify({ o, t }) };
  });

  await run('Error de Steam (por ejemplo, región) → aviso claro y nada más', async () => {
    const p = await page({ setup: () => { window.__qa.inst.seq = [{ mode: "error", error: "Este juego no está disponible en tu región." }]; } });
    await openInstall(p); await p.waitForTimeout(300);
    const t = await top(p);
    await keys(p, ['a'], 400);
    const r = await ev(p, id => ({ v: views.length, d: !!state.downloads[id], inst: installing }), CP);
    return t && /región/.test(t.text) && !r.v && !r.d && !r.inst ? { p } : { p, fail: JSON.stringify({ t, r }) };
  });

  await run('Resultados al instalar: falla, cancelado y "necesita Steam"', async () => {
    const out = [];
    for (const [go, want] of [[{ s: "error", error: "No hay espacio suficiente en ese disco." }, 'dialog'], [{ s: "canceled" }, 'toast'], [{ s: "steam" }, 'steam']]) {
      const p = await page({ setup: new Function(`window.__qa.inst.go = ${JSON.stringify(go)};`) });
      await openInstall(p); await keys(p, ['right', 'a'], 500);
      const r = await ev(p, id => ({ dlg: topView() ? topView().el.textContent.replace(/\s+/g, ' ') : '', toast: $("#toast").textContent, open: window.__qa.calls.includes("steam.open"), d: !!state.downloads[id] }), CP);
      const ok = want === 'dialog' ? /No hay espacio/.test(r.dlg) && !r.d : want === 'toast' ? /cancel/.test(r.toast) && !r.d && !r.dlg : /ventana de Steam/.test(r.dlg) && !r.open;
      out.push(want + ':' + (ok ? 'ok' : JSON.stringify(r)));
      if (p.errs.length) out.push('errores ' + p.errs.join());
      await p.close();
    }
    return out.every(x => /:ok$/.test(x)) ? { note: out.join(' · ') } : { fail: out.join(' · ') };
  });

  await run('Primera vez: conectar con Steam (reinicio), esperar y seguir con el diálogo Instalar', async () => {
    const p = await page({ setup: () => { window.__qa.inst.seq = [{ mode: "setup", gameOpen: false, millennium: false }]; } });
    await openInstall(p); await p.waitForTimeout(300);
    const t = await top(p);
    const btns = await ev(p, () => $$(".dbtns button", topView().el).map(b => b.textContent));
    await keys(p, ['a'], 300);
    const w = await top(p);
    await p.waitForTimeout(1400);
    const t2 = await top(p);
    const en = (await calls(p, 'install.enable')).length;
    if (btns.join() !== 'Cancelar,Usar Steam,Conectar' || t.focus !== 'Conectar') return { p, fail: JSON.stringify({ btns, f: t.focus }) };
    if (!/Conectando con Steam/.test(w.text)) return { p, fail: 'espera: ' + w.text };
    return en === 1 && t2 && t2.inst && t2.phase === 'config' ? { p, note: 'conectó y abrió Instalar' } : { p, fail: JSON.stringify({ en, t2 }) };
  });

  await run('Primera vez con un juego abierto: no reinicia Steam, ofrece la ventanita', async () => {
    const p = await page({ setup: () => { window.__qa.inst.seq = [{ mode: "setup", gameOpen: true }]; } });
    await openInstall(p); await p.waitForTimeout(300);
    const btns = await ev(p, () => $$(".dbtns button", topView().el).map(b => b.textContent));
    await keys(p, ['a'], 400);
    const en = (await calls(p, 'install.enable')).length, o = (await calls(p, 'steam.open')).length;   // aquí sí: elegiste "Usar Steam"
    return btns.join() === 'Cancelar,Usar Steam' && !en && o === 1 ? { p } : { p, fail: JSON.stringify({ btns, en, o }) };
  });

  await run('Conectar falla (por ejemplo, Millennium borró el archivo) → ventanita de Steam', async () => {
    const p = await page({ setup: () => { window.__qa.inst.seq = [{ mode: "setup", millennium: true }]; window.__qa.inst.enableOk = false; } });
    await openInstall(p); await p.waitForTimeout(300);
    const txt = (await top(p)).text;
    await keys(p, ['a'], 1500);
    const o = (await calls(p, 'steam.open')).length, v = await ev(p, () => views.length);
    return /Millennium/.test(txt) && o === 1 && !v ? { p } : { p, fail: JSON.stringify({ txt, o, v }) };
  });

  await run('Conectar: B deja la conexión en segundo plano y avisa al terminar', async () => {
    const p = await page({ setup: () => { window.__qa.inst.seq = [{ mode: "setup" }]; window.__qa.inst.enableMs = 1500; } });
    await openInstall(p); await p.waitForTimeout(300);
    await keys(p, ['a'], 300); await keys(p, ['a', 'start', 'b'], 200);
    const v = await ev(p, () => [views.length, installing]);
    await p.waitForTimeout(1600);
    const t = await ev(p, () => $("#toast").textContent);
    return v[0] === 0 && v[1] === 0 && /conectado/.test(t) ? { p } : { p, fail: JSON.stringify({ v, t }) };
  });

  await run('Steam arrancando: "Esperando a Steam…" y luego el diálogo', async () => {
    const p = await page({ setup: () => { window.__qa.inst.seq = [{ mode: "wait" }]; } });
    await openInstall(p);
    const a = await ev(p, () => topView() && $(".iload", topView().el)?.textContent);
    await p.waitForTimeout(3400);
    const b = await top(p);
    return /Esperando a Steam/.test(a || '') && b && b.phase === 'config' ? { p } : { p, fail: JSON.stringify({ a, b }) };
  });

  await run('Mientras Steam trabaja ("Instalando…") los botones no hacen nada raro', async () => {
    const p = await page({ setup: () => { window.__qa.slow["install.go"] = 1800; } });
    await openInstall(p); await keys(p, ['right', 'a'], 200);
    const busy = await ev(p, () => [topView().phase, $('[data-b="go"]', topView().el).textContent]);
    await keys(p, ['b', 'start', 'x', 'left', 'a', 'up'], 100);
    const mid = await ev(p, () => [views.length, topView() && topView().phase, window.__qa.calls.filter(c => c === "install.cancel").length]);
    await p.waitForTimeout(1800);
    const d = await dl(p);
    if (busy[0] !== 'busy' || !/Instalando/.test(busy[1])) return { p, fail: 'no se vio ocupado ' + busy };
    if (mid[0] !== 1 || mid[1] !== 'busy' || mid[2]) return { p, fail: 'se alteró: ' + mid };
    return d ? { p } : { p, fail: 'no empezó' };
  });

  await run('Diálogo cerrado por fuera (suspender): Steam cancela y se puede volver a instalar', async () => {
    const p = await page(); await openInstall(p);
    await ev(p, () => closeAllViews()); await p.waitForTimeout(1600);
    const r = await ev(p, () => [installing, window.__qa.calls.filter(c => c === "install.cancel").length]);
    await openInstall(p);
    const t = await top(p);
    return r[0] === 0 && r[1] === 1 && t && t.inst ? { p } : { p, fail: JSON.stringify({ r, t }) };
  });

  await run('Solo un asistente a la vez', async () => {
    const p = await page({ setup: () => { window.__qa.slow["install.prepare"] = 1500; } });
    await openInstall(p);
    await ev(p, () => startInstall(games.find(g => g.name === "Cyberpunk 2077")));
    await p.waitForTimeout(200);
    const r = await ev(p, () => [views.filter(v => v.isInstall).length, $("#toast").textContent, window.__qa.calls.filter(c => c === "install.prepare").length]);
    return r[0] === 1 && /Espera/.test(r[1]) && r[2] === 1 ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Descarga cancelada en Steam (download.removed): se quita la franja y deja de estar primero', async () => {
    const p = await page(); await openInstall(p); await keys(p, ['right', 'a'], 500);
    await ev(p, id => window.__emit("download.progress", { appid: id, progress: .1, kind: "Descargando", state: "active" }), CP); await p.waitForTimeout(300);
    await ev(p, id => window.__emit("download.removed", { appid: id }), CP); await p.waitForTimeout(500);
    const r = await ev(p, id => ({ d: !!state.downloads[id], strip: !!document.querySelector(`[data-dl="${id}"]`), pinned: !!(settings.installedAt && settings.installedAt[id]), uninst: document.querySelector(`#track .tile[aria-label="Cyberpunk 2077"]`)?.classList.contains('uninst') }), CP);
    return !r.d && !r.strip && !r.pinned && r.uninst ? { p } : { p, fail: JSON.stringify(r) };
  });

  await run('Actualización de un juego instalado (app vieja, sin "state"): franja y "Actualizando"', async () => {
    const p = await page();
    await ev(p, () => window.__emit("download.progress", { appid: 3489700, progress: .42, kind: "Actualizando" })); await p.waitForTimeout(400);
    const r = await dl(p, 3489700);
    await ev(p, () => { home.zone = "games"; home.g = homeGames().findIndex(g => g.appid === 3489700); updateFocus(); });
    await keys(p, ['a'], 500);
    const t = await ev(p, () => [topView() && topView().isDownload ? $(".dlh", topView().el).textContent + ' ' + $(".pc", topView().el).textContent : '', mode]);
    return r && r.st === 'active' && r.w === '42%' && t[0] === 'Descargando actualización… 42 %' && t[1] === 'ui' ? { p } : { p, fail: JSON.stringify({ r, t }) };
  });

  await run('Juego en cola o en pausa: A abre su menú de descarga con el estado correcto', async () => {
    const p = await page();
    await ev(p, () => window.__emit("download.progress", { appid: 1245620, progress: .2, kind: "Actualizando", state: "queued" })); await p.waitForTimeout(300);
    await ev(p, () => { home.zone = "games"; home.g = homeGames().findIndex(g => g.appid === 1245620); updateFocus(); });
    await keys(p, ['a'], 500);
    const a = await ev(p, () => $(".dlh", topView().el).textContent);
    await ev(p, () => window.__emit("download.progress", { appid: 1245620, progress: .2, kind: "Actualizando", state: "paused" })); await p.waitForTimeout(300);
    const b = await ev(p, () => [$(".dlh", topView().el).textContent, $(".dle", topView().el).textContent]);
    return a === 'En la cola de descargas' && b[0] === 'Descarga en pausa' && b[1] === 'En pausa' ? { p } : { p, fail: JSON.stringify([a, b]) };
  });

  await run('Biblioteca: el juego descargando también lleva su franja', async () => {
    const p = await page();
    await ev(p, id => window.__emit("download.progress", { appid: id, progress: .6, kind: "Descargando", state: "active" }), CP); await p.waitForTimeout(300);
    await ev(p, () => openView(libraryView())); await p.waitForTimeout(800);
    const r = await ev(p, id => { const el = topView().el.querySelector(`[data-dl="${id}"]`); return el ? [el.querySelector("i").style.width, getComputedStyle(el).animationName, el.closest('.tile').classList.contains('uninst')] : null; }, CP);
    await ev(p, id => window.__emit("download.progress", { appid: id, progress: .8, kind: "Descargando", state: "active" }), CP); await p.waitForTimeout(300);
    const w = await ev(p, id => topView().el.querySelector(`[data-dl="${id}"] i`).style.width, CP);
    return r && r[0] === '60%' && r[1] === 'dlStripes' && !r[2] && w === '80%' ? { p } : { p, fail: JSON.stringify({ r, w }) };
  });

  await run('Opciones del juego (X) → Instalar abre el mismo diálogo', async () => {
    const p = await page();
    await ev(p, id => { home.zone = "games"; home.g = homeGames().findIndex(g => g.appid === id); updateFocus(); }, CP);
    await keys(p, ['x'], 500);
    await ev(p, () => { const v = topView(); v.i = v.s.findIndex(x => x.k === "install"); if (v.syncFromIndex) v.syncFromIndex(); updateFocus(); });
    await keys(p, ['a'], 400); await p.waitForTimeout(300);
    let t = await top(p);
    if (t && !t.inst) { await keys(p, ['a'], 500); t = await top(p); }
    return t && t.inst ? { p } : { p, fail: JSON.stringify(t) };
  });

  await run('Tocar la pantalla: elegir disco e Instalar con el dedo', async () => {
    const p = await page(); await openInstall(p);
    await p.locator('.view.dialog .idrv[data-f="1"]').tap(); await p.waitForTimeout(400);
    await p.locator('.view.dialog [data-b="go"]').tap(); await p.waitForTimeout(500);
    const go = await calls(p, 'install.go');
    return go.length && go[0].folder === 1 ? { p } : { p, fail: JSON.stringify(go) };
  });

  await run('Pantalla de TV 1280×720 y modo claro: Instalar y Contrato caben completos', async () => {
    const p = await page({ vp: { width: 1280, height: 720 }, setup: new Function(`settings.theme = "light"; applyTheme(); window.__qa.inst.go = { s: "eula", eulas: [{ id: "e", version: 1, url: "", html: ${JSON.stringify(EULA_HTML)} }] };`) });
    await openInstall(p);
    const a = await ev(p, () => { const r = $(".dbox", topView().el).getBoundingClientRect(); return [r.top >= 0, r.bottom <= innerHeight]; });
    await p.screenshot({ path: __dirname + '/inst_claro.png' });
    await keys(p, ['right', 'a'], 300); await p.waitForTimeout(600);
    const b = await ev(p, () => { const r = $(".dbox", topView().el).getBoundingClientRect(); return [r.top >= 0, r.bottom <= innerHeight, $(".etext", topView().el).clientHeight]; });
    await p.screenshot({ path: __dirname + '/eula_claro.png' });
    return a[0] && a[1] && b[0] && b[1] && b[2] > 80 ? { p } : { p, fail: JSON.stringify({ a, b }) };
  });

  await run('Estrés: 300 botones al azar con el diálogo Instalar y el contrato', async () => {
    const p = await page({ setup: () => { window.__qa.inst.go = [{ s: "eula", eulas: [{ id: "e", version: 1, url: "", html: "<p>" + "Texto. ".repeat(300) + "</p>" }] }]; } });
    const r = await ev(p, async () => {
      const ks = ["up", "down", "left", "right", "a", "b", "x", "y", "lb", "rb", "start"];
      let seed = 11; const rnd = () => (seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648;
      const errs = [];
      for (let n = 0; n < 300; n++) {
        try {
          if (views.some(v => v.isPower) || (topView() && /Salir al escritorio/.test(topView().el.textContent))) closeAllViews();
          if (!topView() && mode === "ui" && n % 25 === 0) { home.zone = "games"; home.g = homeGames().findIndex(g => g.appid === 1091500); if (home.g < 0) home.g = 0; updateFocus(); }
          if (mode === "game") { mode = "ui"; }
          input(ks[Math.floor(rnd() * ks.length)]);
        } catch (e) { errs.push(e.message); }
        await new Promise(r => setTimeout(r, 30));
      }
      await new Promise(r => setTimeout(r, 1800));
      return { errs, installing, views: views.length };
    });
    if (r.errs.length) return { p, fail: r.errs.slice(0, 3).join(' | ') };
    return { p, note: `al final: ${r.views} pantallas abiertas, asistente ${r.installing ? 'ocupado' : 'libre'}` };
  });

  await browser.close();
  const bad = results.filter(r => !r.ok);
  console.log(`\n${results.length - bad.length}/${results.length} escenarios de instalación OK`);
  require('fs').writeFileSync(__dirname + '/resultados_instalar.json', JSON.stringify(results, null, 1));
})();
