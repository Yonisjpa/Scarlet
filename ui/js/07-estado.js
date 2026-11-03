/* ================= estado ================= */
let games = [], friends = [], profile = MOCK.profile, albumData = null;
let mode = "boot";                     // boot | ui | launching | game | sleep | lock | unlocking
const state = { running: null, side: null, sleptFromGame: false, downloads: {}, updates: {}, cloud: {}, overlay: null,
  pads: [{ key: "ally", name: "ROG Xbox Ally X", type: "Integrado", player: 1 }] };
const HOME_MAX = 12;
/* Juegos ocultos (no salen en el Inicio ni en "Todos") y grupos propios, como en la Switch 2. */
const isHidden = g => (settings.hidden || []).includes(g.appid);
/* Orden del Inicio: lo más reciente primero. Un juego que mandas a instalar pasa al primer lugar mientras se descarga
   y se queda ahí al terminar, como lo más nuevo, hasta que juegues otro. */
const homeKey = g => Math.max((g.lastTs || 0) * 1000, (settings.installedAt && settings.installedAt[g.appid]) || 0, state.downloads[g.appid] && settings.installedAt && settings.installedAt[g.appid] ? Date.now() : 0);
const homeGames = () => games.filter(g => !isHidden(g) || g.appid === state.running)
  .map((g, i) => [g, i]).sort((a, b) => (homeKey(b[0]) - homeKey(a[0])) || (a[1] - b[1])).map(x => x[0]).slice(0, HOME_MAX);
const groupNames = () => Object.keys(settings.groups || {});
const inGroup = (g, name) => ((settings.groups || {})[name] || []).includes(g.appid);
const home = { zone: "games", g: 0, a: 0, t: 0, offset: 0 };
const views = [];
const topView = () => views[views.length - 1];
const runningGame = () => games.find(x => x.appid === state.running);
const byId = id => games.find(x => x.appid == id);
const hasAch = g => g.ach && g.ach[1] > 0;

