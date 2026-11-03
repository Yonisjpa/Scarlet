// Nube de Steam (beta 1.2/1.3): el gancho que Scarlet pone en Steam distingue «esperando la nube» de «sincronizando»
// y solo cuenta como pregunta lo que de verdad lo es. Corre el CloudJs real (scripts.json) con un Steam simulado.
const S = require('./scripts.json');
const out = [];
const check = (name, ok, info) => { out.push(ok); console.log((ok ? 'OK   ' : 'FALLA') + ' ' + name + (ok ? '' : ' — ' + info)); };
function steam(status) {
  const cbs = {};
  const g = {
    window: {},
    SteamClient: { Apps: new Proxy({}, { get: (o, k) => f => { cbs[k] = f; } }) },
    appStore: { GetAppOverviewByAppID: id => ({ per_client_data: [{ display_status: status[id] ?? 4, status_percentage: 100 }] }) }
  };
  const run = ids => new Function('window', 'SteamClient', 'appStore', 'return ' + S.cloud.replace('__IDS__', JSON.stringify(ids.map(String))))(g.window, g.SteamClient, g.appStore);
  run([]);
  return { cbs, run };
}
{ // Stellar Blade abre mientras Cyberpunk sube su partida
  const s = steam({ 1091500: 8, 3489700: 1 });
  s.cbs.RegisterForGameActionTaskChange(5, '3489700', 'LaunchApp', 'SynchronizingCloud', '');
  s.cbs.RegisterForGameActionUserRequest(5, '3489700', 'LaunchApp', 'ShowInterstitials', '');
  const r = s.run([3489700, 1091500]).apps;
  const sb = r.find(x => x.id === 3489700), cp = r.find(x => x.id === 1091500);
  check('El que abre espera la nube (no «sincroniza») y el que se cerró es el que sincroniza', sb.wait && sb.st === 1 && cp.st === 8 && !cp.wait, JSON.stringify(r));
  check('ShowInterstitials no cuenta como pregunta', !sb.ask, JSON.stringify(sb));
  s.cbs.RegisterForGameActionTaskChange(5, '3489700', 'LaunchApp', 'CreatingProcess', '');
  s.cbs.RegisterForGameActionUserRequest(5, '3489700', 'LaunchApp', 'CreatingProcess', '');
  const r2 = s.run([3489700]).apps[0];
  check('Al seguir abriendo ya no espera, y CreatingProcess tampoco es pregunta', !r2.wait && !r2.ask, JSON.stringify(r2));
  s.cbs.RegisterForGameActionUserRequest(5, '3489700', 'LaunchApp', 'CloudConflict', '');
  check('Un paso raro (conflicto de nube) sí es pregunta', s.run([3489700]).apps[0].ask, '');
}
{ // el id del juego viene en el segundo dato aunque el número de acción sea grande
  const s = steam({});
  s.cbs.RegisterForGameActionTaskChange(12345, '1245620', 'LaunchApp', 'SynchronizingCloud', '');
  const r = s.run([1245620, 12345]).apps;
  check('Se toma el id del juego, no el número de acción', r[0].wait && !r[1].wait, JSON.stringify(r));
}
console.log(`\n${out.filter(Boolean).length}/${out.length} pruebas de la nube OK`);
process.exitCode = out.every(Boolean) ? 0 : 1;
