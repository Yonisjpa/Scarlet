// Steam simulado: el asistente de instalación (EInstallMgrState) y las descargas.
const S = require('./scripts.json');
function makeSteam(opt = {}) {
  const st = { s: 0, apps: [], folder: 0, eulaOk: false, shortcuts: [true, true], log: [] };
  const later = (ms, f) => setTimeout(f, ms);
  const I = {
    async GetInstallManagerInfo() { const NM = ['None','Setup','WaitLicense','FreeLicense','ShowCDKey','WaitAppInfo','ShowPassword','ShowConfig','ShowEULAs','CreateApps','ReadFromMedia','ShowChangeMedia','WaitLegacyCDKeys','ShowSignup','Complete','Failed','Canceled']; return { [opt.oldName ? 'rgAppIDs' : 'rgApps']: opt.appsNum ? st.apps.slice() : st.apps.map(a => ({ nAppID: a, lDiskSpaceRequiredBytes: 1 })), eInstallState: opt.names ? NM[st.s] : (opt.shift && st.s === 7 ? 21 : st.s), nDiskSpaceRequired: st.s >= 7 ? (opt.size ?? 5e10) : 0, nDiskSpaceAvailable: 1e11, iInstallFolder: st.folder, bCanChangeInstallFolder: true, currentAppID: st.apps[0] || 0, eAppError: st.err || 0, errorDetail: st.detail || '', bDesktopShortcut: st.shortcuts[0], bSystemMenuShortcut: st.shortcuts[1] }; },
    OpenInstallWizard(ids, client, fromUrl) {
      if (opt.apiIgnored && !fromUrl) { st.log.push('api ignorada'); return; }   // como la Steam de escritorio real (log-7)
      if (opt.needClient && client !== '0' && !fromUrl) { st.log.push('sin dispositivo'); return; }
      st.log.push('open'); if (st.s > 0 && st.s < 14) throw new Error('wizard ocupado');
      st.apps = ids; st.s = 1;
      later(150, () => { st.s = 5; });
      later(400, () => { if (opt.fail) { st.s = 15; st.err = opt.fail; } else if (opt.need) st.s = opt.need; else st.s = 7; });
    },
    SetInstallFolder(i) { st.log.push('folder ' + i); st.folder = i; },
    SetCreateShortcuts(a, b) { st.shortcuts = [a, b]; st.log.push(`shortcuts ${a} ${b}`); },
    ContinueInstall() {
      st.log.push('continue@' + st.s);
      if (st.s === 7) { if (opt.eula && !st.eulaOk) { st.s = 9; later(200, () => { st.s = 8; }); } else { st.s = 9; later(300, () => { st.s = 14; later(400, () => { st.s = 0; }); }); } }
      else if (st.s === 8) { if (st.eulaOk || opt.eulaAuto) { st.s = 9; later(300, () => { st.s = 14; }); } }
    },
    OpenUninstallWizard(ids, client, quiet) { st.log.push('desinstala ' + ids[0] + ' ' + client + ' ' + quiet); },
    CancelInstall() { st.log.push('cancel'); st.s = 16; later(200, () => { st.s = 0; st.apps = []; }); }
  };
  const g = {
    SteamClient: {
      URL: opt.noUrl ? undefined : { ExecuteSteamURL(u) { st.log.push('url ' + u); const m = /^steam:\/\/install\/(\d+)$/.exec(u); if (m) I.OpenInstallWizard([+m[1]], '0', true); } },
      Installs: I,
      InstallFolder: { async GetInstallFolders() { return [{ nFolderIndex: 0, strFolderPath: 'C:\\Program Files (x86)\\Steam', strUserLabel: '', strDriveName: 'C:', strFreeSpace: '82000000000', bIsDefaultFolder: true, bIsMounted: true }, { nFolderIndex: 1, strFolderPath: 'D:\\SteamLibrary', strUserLabel: 'Juegos', strDriveName: 'D:', strFreeSpace: '900000000000', bIsDefaultFolder: false, bIsMounted: true }]; } },
      Apps: { async LoadEula(id) { if (opt.eulaThrow) throw new Error('no eula'); return [{ id: 'e1', url: 'https://store.steampowered.com/eula/' + id + '_eula_0', version: 3 }]; }, MarkEulaAccepted(app, id, v) { st.log.push(`eula ok ${app} ${id} ${v}`); if (id === 'e1' && v === 3) st.eulaOk = true; } },
      Downloads: { RegisterForDownloadItems(cb) { st.itemsCb = cb; return { unregister() {} }; }, RegisterForDownloadOverview(cb) { st.ovCb = cb; return { unregister() {} }; },
        EnableAllDownloads(on, c) { st.log.push('todas ' + on + ' ' + c); }, PauseAppUpdate(a, c) { st.log.push('pausa ' + a + ' ' + c); }, ResumeAppUpdate(a, c) { st.log.push('reanuda ' + a + ' ' + c); },
        RemoveFromDownloadList(a, c) { st.log.push('quita ' + a + ' ' + c); }, QueueAppUpdate(a, c) { st.log.push('cola ' + a + ' ' + c); }, SetQueueIndex(a, i, c) { st.log.push('primera ' + a + ' ' + i + ' ' + c); } }
    }
  };
  return { st, run: (name, rep = {}) => { let code = S[name]; for (const [k, v] of Object.entries(rep)) code = code.split(k).join(v); return new Function('SteamClient', 'window', 'return ' + code)(g.SteamClient, g); }, g };
}
const out = []; const check = (name, ok, info) => { out.push(ok); console.log((ok ? 'OK   ' : 'FALLA') + ' ' + name + (info ? ' — ' + info : '')); };
(async () => {
  { const m = makeSteam(); const r = await m.run('prepare', { __APPID__: '292030' });
    check('Preparar: llega a "elegir disco" con tamaño y discos', r.s === 7 && r.mine && r.need === 5e10 && r.folders.length === 2 && r.folders[1].label === 'Juegos' && r.folders[0].free === '82000000000', JSON.stringify({ s: r.s, need: r.need, f: r.folders.map(f => f.drive) }));
    const f = await m.run('folder', { __APPID__: '292030', __FOLDER__: '1' });
    check('Cambiar disco: se aplica y conserva tus accesos directos', f.folder === 1 && m.st.log.includes('shortcuts true true'), m.st.log.join(','));
    const g = await m.run('go', { __APPID__: '292030', __FOLDER__: '1' });
    check('Instalar sin contrato: termina en "completo"', g.s === 14 || g.s === 0, 'estado ' + g.s); }
  { const m = makeSteam({ eula: true }); await m.run('prepare', { __APPID__: '292030' });
    const g = await m.run('go', { __APPID__: '292030', __FOLDER__: '0' });
    check('Con contrato: se detiene en el contrato y lo trae', g.s === 8 && g.eulas && g.eulas[0].id === 'e1' && g.eulas[0].version === 3, JSON.stringify(g));
    const y = await m.run('eulaYes', { __APPID__: '292030' });
    check('Aceptar contrato: lo marca aceptado y sigue hasta completo', (y.s === 14 || y.s === 0) && m.st.log.some(x => x === 'eula ok 292030 e1 3'), 'estado ' + y.s + ' · ' + m.st.log.join(',')); }
  { const m = makeSteam({ eula: true }); await m.run('prepare', { __APPID__: '5' }); await m.run('go', { __APPID__: '5', __FOLDER__: '0' });
    const n = await m.run('eulaNo', { __APPID__: '5' });
    check('Rechazar contrato: cancela el asistente', n.s === 16, 'estado ' + n.s); }
  { const m = makeSteam({ eula: true }); await m.run('prepare', { __APPID__: '7' }); await m.run('go', { __APPID__: '7', __FOLDER__: '0' });
    const code = m.run('eulaYes', { __APPID__: '7', 'MarkEulaAccepted(app, "e1", 3)': 'MarkEulaAccepted(app, "otro", 1)' });
    const y = await code;
    check('Contrato que no avanza: se reporta "atorado" (Scarlet usa la ventanita de Steam)', y.s === 8 && y.stuck === true, JSON.stringify({ s: y.s, stuck: y.stuck })); }
  { const m = makeSteam({ fail: 5 }); const r = await m.run('prepare', { __APPID__: '9' });
    check('Juego que no es tuyo: falla con el código de Steam', r.s === 15 && r.err === 5, JSON.stringify({ s: r.s, err: r.err })); }
  { const m = makeSteam({ need: 4 }); const r = await m.run('prepare', { __APPID__: '9' });
    check('Pide clave de producto: se detecta para usar la ventanita', r.s === 4, 'estado ' + r.s); }
  { const m = makeSteam(); m.st.s = 7; m.st.apps = [111]; const r = await m.run('prepare', { __APPID__: '222' });
    check('Había otro asistente abierto (escondido): se cancela y se abre el nuevo', r.s === 7 && r.mine && m.st.log.indexOf('cancel') < m.st.log.indexOf('open'), m.st.log.join(',')); }
  { const m = makeSteam(); await m.run('prepare', { __APPID__: '1' }); m.st.s = 16;
    const g = await m.run('go', { __APPID__: '1', __FOLDER__: '0' });
    check('El asistente ya no estaba (Steam lo cerró): avisa "stale" y no continúa', g.stale === true && !m.st.log.some(x => x.startsWith('continue')), JSON.stringify(g)); }
  { const m = makeSteam(); await m.run('prepare', { __APPID__: '1' }); await m.run('cancel');
    check('Cancelar: cierra el asistente', m.st.s === 16 || m.st.s === 0, m.st.log.join(',')); }
  { const m = makeSteam({ eula: true, eulaThrow: true }); await m.run('prepare', { __APPID__: '3' }); const g = await m.run('go', { __APPID__: '3', __FOLDER__: '0' });
    check('Steam no da el texto del contrato: igual se muestra el paso (sin texto)', g.s === 8 && Array.isArray(g.eulas) && g.eulas.length === 0); }
  { const m = makeSteam({ appsNum: true, names: true }); const r = await m.run('prepare', { __APPID__: '1091500' });
    check('Steam manda la lista como números y el estado con nombre: igual llega al disco', r.s === 7 && r.mine && r.need > 0, JSON.stringify({ s: r.s, mine: r.mine }));
    const g = await m.run('go', { __APPID__: '1091500', __FOLDER__: '0' });
    check('… y al instalar termina bien', g.s === 14 || g.s === 0, 'estado ' + g.s); }
  { const m = makeSteam({ shift: true }); const t0 = Date.now(); const r = await m.run('prepare', { __APPID__: '5' });
    check('Paso con número desconocido que ya trae el tamaño: se toma como la pantalla del disco', r.s === 21 && r.need > 0 && Date.now() - t0 < 4000, `estado ${r.s} en ${Date.now() - t0} ms`); }
  { const m = makeSteam(); await m.run('prepare', { __APPID__: '77' });
    m.g.SteamClient.Installs.ContinueInstall = () => { m.st.log.push('continue-raro'); setTimeout(() => m.st.itemsCb && m.st.itemsCb(true, [{ appid: 77, active: false, completed: false, paused: false, queue_index: 0, downloaded_bytes: 0, total_bytes: 1, buildid: 0 }]), 300); };
    m.run('downloads');
    const g = await m.run('go', { __APPID__: '77', __FOLDER__: '0' });
    check('El asistente no cambia de paso pero el juego ya está en las descargas: se toma como iniciado', g.started === true, JSON.stringify({ s: g.s, started: g.started })); }
  { const m = makeSteam(); await m.run('prepare', { __APPID__: '1' });
    const g = await m.run('prepare', { __APPID__: '1' });
    check('Pedir otra vez con el asistente abierto (escondido): lo reinicia y sigue', g.s === 7 && m.st.log.filter(x => x === 'cancel').length === 1, m.st.log.join(',')); }
  { const m = makeSteam(); await m.run('prepare', { __APPID__: '1' }); await m.run('cancel');
    check('Cancelar (versión nueva): cierra el asistente', m.st.log.includes('cancel'), m.st.log.join(',')); }
  { const m = makeSteam(); const r = await m.run('prepare', { __APPID__: '1091500' });
    check('Como lo manda Steam de verdad (rgApps, del log-7): reconoce su asistente', r.s === 7 && r.mine === true, JSON.stringify({ s: r.s, mine: r.mine })); }
  { const m = makeSteam({ oldName: true }); const r = await m.run('prepare', { __APPID__: '1091500' });
    check('Y con el nombre viejo (rgAppIDs) también', r.s === 7 && r.mine === true); }
  { const m = makeSteam(); const real = m.g.SteamClient.Installs.OpenInstallWizard; let n = 0;
    m.g.SteamClient.Installs.OpenInstallWizard = ids => { n++; if (n === 1) return; real(ids); };   // Steam recién abierto ignora la primera orden
    const r = await m.run('prepare', { __APPID__: '42' });
    check('Steam ignora la primera orden (recién reiniciado): se repite y llega al disco', r.s === 7 && n >= 2, `estado ${r.s}, órdenes ${n}`); }
  { const m = makeSteam(); m.st.s = 16; m.st.apps = [1091500];   // lo que dejó un intento anterior del mismo juego (foto de Jhon)
    const r = await m.run('prepare', { __APPID__: '1091500' });
    check('Quedaba "cancelado" de un intento anterior del mismo juego: no lo toma como respuesta y llega al disco', r.s === 7 && r.fresh === true, JSON.stringify({ s: r.s, fresh: r.fresh })); }
  { const m = makeSteam(); m.st.s = 14; m.st.apps = [1091500];
    const r = await m.run('prepare', { __APPID__: '1091500' });
    check('Quedaba "completo" de antes: tampoco cuenta', r.s === 7 && r.fresh === true, JSON.stringify({ s: r.s })); }
  { const m = makeSteam(); m.g.SteamClient.Installs.OpenInstallWizard = () => {};   // Steam sin conexión: no hace caso
    m.st.s = 16; m.st.apps = [5];
    const t0 = Date.now(); const r = await m.run('prepare', { __APPID__: '5' });
    check('Steam no hace caso (sin conexión): se reporta "no fresco" (Scarlet reinicia Steam en vez de decir "canceló")', r.fresh === false, `${Date.now() - t0} ms`); }
  { const m = makeSteam({ apiIgnored: true }); const r = await m.run('prepare', { __APPID__: '1091500' });
    check('Steam de escritorio real: la función no abre nada, el enlace steam://install sí → llega al disco', r.s === 7 && r.fresh && /url/.test(r.how), JSON.stringify({ s: r.s, how: r.how, log: m.st.log.slice(0, 4) })); }
  { const m = makeSteam({ noUrl: true }); const r = await m.run('prepare', { __APPID__: '1091500' });
    check('Steam sin el enlace interno (Deck/otra versión): usa la función', r.s === 7 && /api/.test(r.how), JSON.stringify({ s: r.s, how: r.how })); }
  // ───── descargas: escenarios (scen.js) por el código real de Scarlet; se guardan para la prueba en C# de la compilación
  { const SC = require('./scen.js'), fx = {};
    for (const [name, sc] of Object.entries(SC)) {
      const m = makeSteam(); m.run('downloads');
      for (const gs of sc.items) m.st.itemsCb(true, gs);
      for (const o of sc.ovs) m.st.ovCb(o);
      fx[name] = m.run('downloads');
    }
    require('fs').writeFileSync(process.env.FIXTURES || 'fixtures.json', JSON.stringify(fx, null, 1));
    const a = fx.lan_dos_dispositivos;
    check('Dos dispositivos: se usa el avance de ESTA consola aunque el de la laptop llegue después', a.ov.cur === 1091500 && a.ov.pct === 83 && a.ov.ph[2].done === 65.6e9 && a.ov.lan === 'YONIS' && !a.ov.upload && a.others === 1, JSON.stringify({ pct: a.ov.pct, dl: a.ov.ph[2], lan: a.ov.lan }));
    check('… el juego: instalación nueva, contenido pendiente, bytes de la fase de descarga', a.items[0].fresh && a.items[0].game && !a.items[0].extra && a.items[0].b === 65.6e9 && a.items[0].t === 78.8e9, JSON.stringify(a.items[0]));
    check('Una lista con solo la otra PC no borra la de esta consola', fx.lista_solo_remota.items.length === 1 && fx.lista_solo_remota.items[0].a === 1091500);
    const s = fx.sombreadores;
    check('Sombreadores después de instalar: contenido listo, trabajo extra', s.ov.shader && !s.items[0].game && s.items[0].extra && s.items[0].inst, JSON.stringify(s.items[0]));
    const u = fx.actualizacion_y_cola;
    check('Actualización y cola: no es instalación nueva; la segunda en cola 1', !u.items[0].fresh && u.items[0].inst && u.items[1].q === 1 && !u.items[1].on && u.ov.pct === 40, JSON.stringify(u.items.map(i => [i.a, i.q, i.fresh])));
    const p = fx.pendientes;
    check('Pendientes: sin cola, programada (deferred) y terminada se distinguen', p.items.length === 4 && p.items[0].q === -1 && p.items[1].def > 0 && p.items[2].fresh && p.items[3].done, JSON.stringify(p.items.map(i => [i.a, i.q, i.def, i.fresh, i.done])));
    const e = fx.error_y_pausa;
    check('Error de Steam (código y texto) y pausa general', e.items[0].err === 12 && e.items[0].errText === 'Disk write failure' && e.ov.paused, JSON.stringify(e.items[0]));
    check('Solo subida: se marca como subida', fx.solo_subida.ov.upload && fx.solo_subida.items.length === 0);
    check('Reservando espacio: estado y fases', fx.reservando.ov.state === 'Preallocating' && fx.reservando.ov.ph[1].total === 89e9);
    const v = fx.formato_viejo;
    check('Formato viejo (sin grupos ni tipos): se lee igual', v.items.length === 1 && v.items[0].a === 292030 && v.items[0].b === 3e9 && v.items[0].game && v.ov.dl === 3e9 && v.ov.dlTotal === 1e10, JSON.stringify(v.items[0])); }
  { const m = makeSteam(); m.run('downloads'); m.st.itemsCb(true, [{ remote_client_id: "0", item_data: [{ appid: 1091500, active: true, completed: false, queue_index: 0 }] }]);
    const r = await m.run('prepare', { __APPID__: '1091500' });
    check('Darle Instalar a un juego que ya está bajando: no abre el asistente otra vez', r.s === 14 && r.how === 'ya en descargas' && !m.st.log.includes('open'), JSON.stringify({ s: r.s, how: r.how })); }
  { const m = makeSteam({ needClient: true, noUrl: true }); const r = await m.run('prepare', { __APPID__: '1091500' });
    check('Abrir el asistente por la función: lleva el dispositivo "0" (sin él Steam no hace caso)', r.s === 7 && !m.st.log.includes('sin dispositivo'), m.st.log.slice(0, 3).join()); }
  { const m = makeSteam(); const a = m.run('pause', { __APPID__: '1091500' }); const b = m.run('resume', { __APPID__: '1091500' });
    check('Pausar y reanudar con el dispositivo', a === 'todas' && m.st.log.join() === 'todas false 0,todas true 0,reanuda 1091500 0', m.st.log.join()); }
  { const m = makeSteam(); m.run('downloads'); const r = await m.run('cancelDl', { __APPID__: '1091500', __UNINST__: 'true' });
    check('Cancelar instalación: la quita y desinstala SIN preguntar (dispositivo "0", true)', r === 'lista desinstalar' && m.st.log.join() === 'quita 1091500 0,desinstala 1091500 0 true', m.st.log.join()); }
  { const m = makeSteam(); m.run('downloads'); const r = await m.run('cancelDl', { __APPID__: '570', __UNINST__: 'false' });
    check('Cancelar actualización: solo la quita de la lista, nunca desinstala', r === 'lista' && m.st.log.join() === 'quita 570 0', m.st.log.join()); }
  { const m = makeSteam(); m.run('downloads');
    m.st.itemsCb(true, [{ remote_client_id: '0', item_data: [{ appid: 570, active: true, queue_index: 0 }, { appid: 440, queue_index: 1 }] }]); m.st.ovCb({ remote_client_id: '0', paused: true, update_appid: 570 });
    await m.run('cancelDl', { __APPID__: '570', __UNINST__: 'false' });
    check('Cancelar la primera con todo en pausa: la siguiente sigue (como Steam)', m.st.log.join() === 'quita 570 0,todas true 0', m.st.log.join()); }
  { const m = makeSteam(); m.run('now', { __APPID__: '570' });
    check('Actualizar ya: a la cola, primera y arranca', m.st.log.join() === 'cola 570 0,primera 570 0 0,reanuda 570 0,todas true 0', m.st.log.join()); }
  { const m = makeSteam(); m.run('uninstall', { __APPID__: '1091500' });
    check('Desinstalar sin la ventanita: la misma función que su botón, sin preguntar', m.st.log.join() === 'desinstala 1091500 0 true', m.st.log.join()); }
  console.log(`\n${out.filter(Boolean).length}/${out.length} pruebas del lado de Steam OK`);
  process.exitCode = out.every(Boolean) ? 0 : 1;
})();
