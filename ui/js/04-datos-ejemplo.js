/* ================= datos de ejemplo ================= */
const SOURCES = { steam: "Steam", epic: "Epic Games", xbox: "Xbox / Game Pass", emu: "Emulador", app: "Aplicación", ext: "Agregado por ti" };
const MOCK = {
  profile: { name: "Jhon", level: 27, avatarUrl: "" },
  games: [
    { appid: 3489700, name: "Stellar Blade", hours: 42.5, last: "hoy", ach: [18, 38], cols: ["Favoritos", "Acción"] },
    { appid: 1245620, name: "ELDEN RING", hours: 131.2, last: "ayer", ach: [34, 42], cols: ["Favoritos", "RPG", "Acción"] },
    { appid: 1145350, name: "Hades II", hours: 27.8, last: "hace 3 días", ach: [21, 49], cols: ["Indies", "Acción"] },
    { appid: 900104, source: "app", kind: "app", name: "YouTube", hours: 61.0, last: "hace 3 días", ach: [0, 0], exec: "msedge --app=https://www.youtube.com/tv --start-fullscreen" },
    { appid: 2379780, name: "Balatro", hours: 64.0, last: "hace 4 días", ach: [26, 30], cols: ["Favoritos", "Indies"] },
    { appid: 900101, source: "epic", name: "Fortnite", hours: 15.2, last: "hace 5 días", ach: [0, 0], cols: ["Acción"] },
    { appid: 1086940, name: "Baldur's Gate 3", hours: 88.3, last: "la semana pasada", ach: [22, 54], cols: ["RPG"] },
    { appid: 1091500, name: "Cyberpunk 2077", hours: 71.4, last: "la semana pasada", ach: [31, 57], cols: ["RPG", "Acción"], installed: false },
    { appid: 367520, name: "Hollow Knight", hours: 39.9, last: "hace 2 semanas", ach: [40, 63], cols: ["Indies"] },
    { appid: 900102, source: "xbox", name: "Avowed", hours: 12.4, last: "hace 2 semanas", ach: [0, 0], cols: ["RPG"] },
    { appid: 2246340, name: "Monster Hunter Wilds", hours: 55.1, last: "hace 3 semanas", ach: [28, 50], cols: ["Acción", "RPG"] },
    { appid: 900105, source: "app", kind: "app", name: "Discord", hours: 40.0, last: "hace 3 semanas", ach: [0, 0] },
    { appid: 900106, source: "app", kind: "app", name: "Microsoft Edge", hours: 9.3, last: "hace 3 semanas", ach: [0, 0], exec: "msedge --start-fullscreen" },
    { appid: 504230, name: "Celeste", hours: 19.4, last: "el mes pasado", ach: [19, 32], cols: ["Indies"] },
    { appid: 1551360, name: "Forza Horizon 5", hours: 46.7, last: "el mes pasado", ach: [77, 210], cols: [], installed: false },
    { appid: 900103, source: "emu", kind: "app", name: "Dolphin", hours: 6.1, last: "el mes pasado", ach: [0, 0] },
    { appid: 1794680, name: "Vampire Survivors", hours: 33.0, last: "el mes pasado", ach: [140, 225], cols: ["Indies"] },
    { appid: 413150, name: "Stardew Valley", hours: 120.6, last: "hace 2 meses", ach: [30, 49], cols: ["Favoritos", "Indies"] },
    { appid: 1817070, name: "Marvel's Spider-Man Remastered", hours: 24.2, last: "hace 3 meses", ach: [44, 51], cols: ["Acción"], installed: false },
    { appid: 2050650, name: "Resident Evil 4", hours: 21.7, last: "hace 4 meses", ach: [20, 44], cols: ["Acción"] }
  ],
  friends: [
    { name: "NeoKaiser", status: "playing", game: "ELDEN RING" },
    { name: "LunaPixel", status: "playing", game: "Balatro" },
    { name: "ElTacoMaster", status: "online" },
    { name: "Rorro_92", status: "away" },
    { name: "Gatito.exe", status: "online" },
    { name: "SirBalatro", status: "offline" },
    { name: "Mich_Speedrun", status: "offline" },
    { name: "PixelPanda", status: "offline" }
  ]
};
const ACH_BASE = [["Primeros pasos", "Completa el tutorial"], ["Explorador", "Descubre 10 zonas"], ["Sin un rasguño", "Vence a un jefe sin recibir daño"], ["Coleccionista", "Consigue 50 objetos"], ["Imparable", "Encadena un combo de 100"], ["Noctámbulo", "Juega después de medianoche"], ["Maestría", "Termina el juego en difícil"], ["Leyenda", "Desbloquea todos los logros"]];
function mockAch(g) {
  const [u, t] = g.ach, n = Math.min(t, 24), list = [];
  for (let i = 0; i < n; i++) {
    const b = ACH_BASE[i % ACH_BASE.length], lap = Math.floor(i / ACH_BASE.length);
    list.push({ name: b[0] + (lap ? " " + (lap + 1) : ""), desc: b[1], unlocked: i < Math.round(u * n / t), date: "12 oct 2026" });
  }
  return { unlocked: u, total: t, list };
}
function shotSvg(g, i) {
  const h = (g.appid * 47 + i * 23) % 360;
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="720" viewBox="0 0 1280 720"><defs><linearGradient id="s" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="hsl(${h},60%,62%)"/><stop offset="1" stop-color="hsl(${(h + 40) % 360},55%,30%)"/></linearGradient></defs><rect width="1280" height="720" fill="url(#s)"/><circle cx="${260 + (i * 97) % 760}" cy="${150 + (i * 31) % 120}" r="90" fill="#fff" opacity=".25"/><path d="M0 520 L${160 + (i * 41) % 200} 330 L420 500 L${600 + (i * 29) % 120} 290 L900 520 L1100 380 L1280 470 V720 H0Z" fill="hsl(${(h + 180) % 360},30%,18%)" opacity=".85"/><path d="M0 620 L300 480 L560 600 L820 470 L1280 640 V720 H0Z" fill="#000" opacity=".35"/><text x="40" y="680" font-family="Segoe UI,Arial" font-size="30" font-weight="700" fill="#fff" opacity=".85">${xml(g.name)}</text></svg>`;
  return "data:image/svg+xml;charset=utf-8," + encodeURIComponent(svg);
}
function mockAlbum() {
  const pool = MOCK.games.filter(g => g.kind !== "app"), out = [];
  for (let i = 0; i < 22; i++) {
    const g = pool[(i * 5) % pool.length], day = 25 - Math.floor(i / 3), hh = 22 - (i % 3) * 3, mm = (i * 17) % 60;
    out.push({ url: shotSvg(g, i), path: `captura_${i}.png`, game: g.name, date: `${day} oct 2026 · ${hh}:${String(mm).padStart(2, "0")}`, type: i % 6 === 4 ? "video" : "image" });
  }
  return out;
}
const nat = (real, demo) => Host.has ? real : demo;
const Data = {
  profile: () => Host.call("steam.profile").catch(() => nat({ name: "Jugador" }, { ...MOCK.profile })),
  games: () => Host.call("steam.games").catch(() => nat([], MOCK.games.map(g => ({ source: "steam", kind: "game", installed: true, cols: [], ...g })))),
  friends: () => Host.call("steam.friends").catch(() => nat([], MOCK.friends.map(f => ({ ...f })))),
  achievements: g => Host.call("steam.achievements", { appid: g.appid }).catch(() => nat({ unlocked: g.ach[0], total: g.ach[1], list: [] }, mockAch(g))),
  album: () => Host.call("album.list").catch(() => nat([], mockAlbum())),
  launch: g => Host.call("game.launch", { appid: g.appid, source: g.source }).catch(() => {}),
  open: (url, label) => Host.call("steam.open", { url }).catch(() => toast("Prototipo: aquí se abriría " + label))
};

