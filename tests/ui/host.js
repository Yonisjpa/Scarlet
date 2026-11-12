// Simulador de la app de Scarlet (anfitrión) para probar la interfaz sin Windows.
(() => {
  const L = []; const Q = window.__qa = { calls: [], log: [], fail: new Set(), slow: {}, running: 0, state: {},
    inst: { size: 70.2 * 2 ** 30, folders: [{ i: 0, name: "Disco local (C:)", free: 120 * 2 ** 30, def: true, ok: true, path: "C:\\Steam" }, { i: 1, name: "Juegos (D:)", free: 800 * 2 ** 30, def: false, ok: true, path: "D:\\SteamLibrary" }], seq: [], go: null, eula: null, enableOk: true, enableMs: 600 } };
  const games = [
    { appid: 3489700, name: "Stellar Blade", source: "steam", kind: "game", installed: true, hours: 42.5, last: "hoy", lastTs: 1761500000, ach: [18, 38], cols: ["Acción"], size: 52.4, pause: true, keepFocus: false },
    { appid: 1245620, name: "ELDEN RING", source: "steam", kind: "game", installed: true, hours: 131.2, last: "ayer", lastTs: 1761400000, ach: [34, 42], cols: [], size: 61.8, pause: true },
    { appid: 1091500, name: "Cyberpunk 2077", source: "steam", kind: "game", installed: false, hours: 71, last: "hace 1 mes", lastTs: 1758000000, ach: [31, 57], cols: [], size: 70 },
    { appid: 1793059040, name: "ZenlessZoneZero", source: "xbox", kind: "game", installed: true, hours: 3, last: "ayer", lastTs: 1761300000, ach: [0, 0], cols: ["Xbox"], size: 40, storeId: "9N4X0WFZ2STM" },
    { appid: 900001, name: "YouTube", source: "app", kind: "app", installed: true, hours: 0, last: "nunca", lastTs: 0, ach: [0, 0], cols: [], size: 0 },
    { appid: 900002, name: "Apple Music", source: "xbox", kind: "app", installed: true, hours: 0, last: "nunca", lastTs: 0, ach: [0, 0], cols: [], size: 0 },
    { appid: 900003, name: "Netflix", source: "xbox", kind: "app", installed: true, hours: 0, last: "nunca", lastTs: 0, ach: [0, 0], cols: [], size: 0 }
  ];
  Q.vpn = { provider: "none", warp: false, wireguard: false, profile: null, status: "off" };
  const vst = () => ({ provider: Q.vpn.provider, warp: Q.vpn.warp, wireguard: Q.vpn.wireguard, profile: Q.vpn.profile, status: Q.vpn.status, internet: true, steam: true });
  const emit = (event, data) => L.forEach(f => f({ data: { event, data } }));
  window.__emit = emit;
  const R = {
    "steam.profile": () => ({ name: "Jhon", level: 31, avatarUrl: "", frameUrl: "" }),
    "steam.games": () => JSON.parse(JSON.stringify(games.concat(Q.ext || []))),
    "fs.list": p => { const T = { "": [{ name: "Escritorio", path: "C:\\D", kind: "place" }], "C:\\D": [{ name: "Emus", path: "C:\\D\\Emus", kind: "dir" }, p.images ? { name: "p.png", path: "C:\\D\\p.png", kind: "image" } : { name: "Hollow Knight.lnk", path: "C:\\D\\Hollow Knight.lnk", kind: "file", size: 900 }], "C:\\D\\Emus": [{ name: "Ryujinx.exe", path: "C:\\D\\Emus\\Ryujinx.exe", kind: "file", size: 4e7 }] };
      return { path: p.path || "", parent: p.path === "C:\\D\\Emus" ? "C:\\D" : p.path ? "" : null, entries: T[p.path || ""] || [] }; },
    "ext.add": p => { Q.ext = Q.ext || []; const name = p.path.split("\\").pop().replace(/\.(exe|lnk|url)$/i, ""); const id = 2000000001 + Q.ext.length; Q.ext.push({ appid: id, name, source: "ext", kind: "game", installed: true, hours: 0, last: "nunca", lastTs: 0, ach: [0, 0], cols: ["Externos"], size: 0, path: p.path, args: "" }); return { ok: true, appid: id, name }; },
    "ext.update": p => { const g = (Q.ext || []).find(x => x.appid === p.appid); if (!g) return { ok: false, error: "no" }; Object.assign(g, p); return { ok: true }; },
    "ext.remove": p => { Q.ext = (Q.ext || []).filter(x => x.appid !== p.appid); return true; },
    "ext.art": p => ({ ok: true, url: p.mode === "clear" ? null : "data:image/svg+xml;charset=utf-8,%3Csvg xmlns='http://www.w3.org/2000/svg' width='64' height='64'%3E%3Crect width='64' height='64' fill='%23f80'/%3E%3C/svg%3E", icon: p.mode === "icon" }),
    "quick.glass": () => true, "quick.tint": () => true, "quick.shape": () => true,
    "steam.friends": () => [{ name: "NeoKaiser", steamid: "1", status: "playing", game: "ELDEN RING", avatar: "" }, { name: "Luna", steamid: "2", status: "offline", last: "hace 3 h" }],
    "steam.friendProfile": () => ({ level: 12, common: [{ appid: 1245620, name: "ELDEN RING", mine: 131, theirs: 80 }], recent: [{ appid: 1245620, name: "ELDEN RING", weeks2: 6.5, total: 80 }], lastSeen: "1 nov 2026, 21:10" }),
    "steam.achievements": () => ({ unlocked: 18, total: 38, list: [{ name: "Primeros pasos", desc: "Termina el tutorial", unlocked: true, date: "12 oct 2026" }, { name: "Explorador", desc: "Descubre 10 zonas", unlocked: false }] }),
    "steam.open": () => true, "steam.join": () => ({ ok: true }),
    "storage.info": () => ({ total: 953, free: 410 }),
    "pads.list": () => [{ key: "ally", name: "ROG Xbox Ally X", type: "Integrado", player: 1, vendor: 0x0B05, layout: "xbox" }, { key: "m1", name: "Xbox Wireless Controller", type: "Inalámbrico", player: 2, vendor: 0x2345, layout: "xbox", battery: .6 }],
    "system.state": () => ({ brightness: 60, brightnessOk: true, volume: 40, power: "perf", wifi: true, airplane: false, autoBriOk: !!Q.sensor, ...(Q.sensor ? { autoBri: !!Q.autoBri } : {}) }),
    "album.list": () => window.__qa.album || [], "sounds.list": () => ({}), "sounds.info": () => ({ pack: null }),
    "app.info": () => ({ startup: false, updated: false, version: "0.4.19", dolby: true }),
    "game.state": () => ({ appid: Q.running, ready: true }),
    "game.launch": p => { setTimeout(() => { if (Q.updateOnLaunch === p.appid) { Q.updateOnLaunch = 0; return emit("game.updating", { appid: p.appid }); } Q.running = p.appid; emit("game.ready", { appid: p.appid }); }, 400); return true; },
    "game.close": () => { Q.running = 0; return true; }, "game.detach": () => { Q.running = 0; return true; },
    "game.focus": () => true, "game.kill": () => { const id = Q.running; Q.running = 0; setTimeout(() => emit("game.exited", { appid: id }), 100); return true; },
    "game.wait": () => true, "game.pausepref": () => true, "game.menufocus": () => true, "game.uninstall": p => { if (Q.uninstallFail) return { ok: false, error: "Cierra el juego antes de desinstalarlo." }; setTimeout(() => emit("game.uninstalled", { appid: p.appid, ok: !Q.uninstallBad, freed: 52.4 * 2 ** 30 }), Q.uninstallMs || 500); return { ok: true, mode: "cdp" }; },
    "app.open": () => true, "ui.show": () => true,
    "quick.live": p => ({ mixer: Q.mixer || [], atmos: true, ...(p && p.full ? { battery: { minutesLeft: 90 }, system: R["system.state"](), media: Q.media || null, wifiName: "Casa_5G" } : { system: { volume: 40, brightness: 60 } }) }),
    "quick.shown": () => true, "quick.close": () => true, "quick.action": () => true,
    "media.state": () => Q.media || null, "media.cmd": () => true,
    "system.brightness": () => true, "system.volume": () => true, "system.power": () => true,
    "system.radio": p => Q.radioDeny ? { error: "Windows no deja a Scarlet cambiar el Wi-Fi: Configuración › Privacidad › Radios › permitir" } : { wifi: p.wifi, airplane: p.airplane },
    "mixer.set": () => true, "audio.reset": () => 2, "audio.mutebehind": () => true, "cursor.hide": () => true,
    "diag": () => ({ "Versión": "alpha 0.4.19", "Juego en curso": "ninguno", "Salida 1: Altavoces (predeterminada)": "volumen 40 % · AppleMusic 100 % ▶ sonando" }),
    "update.check": () => ({ available: true, latest: "0.4.20", current: "0.4.19", notes: "Cambios" }),
    "update.install": () => ({ ok: false, error: "simulado" }),
    "setup.get": () => ({ steamId: "76561198000000000", steamKey: true, sgdbKey: true, autostart: true, gamebar: true }),
    "setup.set": p => ({ steamId: "76561198000000000", steamKey: true, sgdbKey: true, autostart: !!p.value, gamebar: true }),
    "compat.fix": () => ({ message: "Las opciones de Windows ya estaban bien." }), "compat.set": () => true,
    "sounds.import": () => ({ ok: true, name: "Nintendo Switch 2 UI Sounds", count: 26 }), "sounds.reset": () => true,
    // beta 1.6: logros encima del juego y sonidos de logros importados
    "ach.show": () => true,
    "sounds.importAch": () => Q.achImportFail ? { ok: false, error: Q.achImportFail } : { ok: true, name: "logros", count: 2 },
    "album.delete": () => true, "album.trim": () => ({ ok: true, item: { type: "video", url: "", path: "c", game: "x", date: "hoy" } }),
    "album.share": () => ({ url: "http://192.168.1.5:8765/x", qr: "", minutes: 10 }), "album.shareStop": () => true, "album.copy": () => true,
    "pad.player": () => true, "pad.rumble": () => true, "pad.disconnect": () => true, "bt.scan": () => true, "bt.pair": () => true,
    "rumble": () => true, "overlay.notify": () => true, "system.open": () => true, "system.armoury": () => true, "system.shutdown": () => true,
    "system.sleep": () => true, "system.restart": () => true, "system.autobright": p => Q.sensor ? (Q.autoBri = !!p.on, { on: Q.autoBri }) : { error: "Esta consola no tiene sensor de luz" }, "system.hibernate": () => true, "app.icon": () => null, "app.reload": () => true, "app.setup": () => true,
    "clipboard.get": () => "texto", "capture.take": () => false, "audio.output": () => true, "audio.atmos": () => true,
    "net.state": () => ({ internet: Q.net ? Q.net.internet : true, steam: Q.net ? Q.net.steam : true }),
    "vpn.state": () => vst(),
    "vpn.set": p => { const V = Q.vpn; if (V.provider === "none") return { error: "Instala Cloudflare WARP o importa un perfil de WireGuard en Ajustes › VPN." }; if (V.provider === "wireguard" && V.setupNeeded) return { error: "setup" }; if (V.fail) return Object.assign(vst(), { error: V.fail }); V.status = p.on ? "on" : "off"; return vst(); },
    "vpn.setup": () => { if (Q.vpn.setupFail) return { ok: false, error: Q.vpn.setupFail }; Q.vpn.setupNeeded = false; return { ok: true }; },
    "vpn.import": () => { const V = Q.vpn; if (V.importFail) return { ok: false, error: V.importFail }; V.profile = "ProtonVPN-MX-FREE-1"; V.provider = "wireguard"; return { ok: true, name: V.profile }; },
    "vpn.remove": () => { const V = Q.vpn; V.profile = null; V.status = "off"; if (V.provider === "wireguard") V.provider = V.warp ? "warp" : "none"; return vst(); },
    "vpn.provider": p => { Q.vpn.provider = p.provider; return vst(); },
    "web.open": () => true,
    // beta 1.2: Wi-Fi y perfil
    "wifi.list": p => { const W = Q.wifi = Q.wifi || { saved: ["Casa_5G", "Oficina"], cur: "Casa_5G" }; if (Q.wifiErr) return { ok: false, error: Q.wifiErr };
      const near = [{ ssid: "Casa_5G", signal: 90, bars: 3, secure: true, security: "WPA2-Personal", band: "5 GHz" }, { ssid: "Vecino", signal: 50, bars: 2, secure: true, security: "WPA3-Personal", band: "2.4 GHz", sae: true }, { ssid: "Cafe libre", signal: 30, bars: 1, secure: false, security: "Abierta", band: "2.4 GHz" }];
      return { ok: true, adapter: "Wi-Fi 7", saved: [...W.saved], location: !!Q.wifiLoc, networks: Q.wifiLoc ? [] : near.map(n => ({ ...n, saved: W.saved.includes(n.ssid), connected: n.ssid === W.cur, connectable: true })),
        current: W.cur ? { ssid: W.cur, security: "WPA2-Personal", band: "5 GHz", channel: 36, signal: 90, rx: 1200, ip: "192.168.0.20", mask: "255.255.255.0", gateway: "192.168.0.1", dns: "1.1.1.1", mac: "AA:BB:CC:DD:EE:FF", bssid: "11:22:33:44:55:66" } : null }; },
    "wifi.connect": async p => { await new Promise(r => setTimeout(r, 300)); const W = Q.wifi; if (p.password != null && p.password !== "clave1234") return { ok: false, error: "No se pudo conectar. Revisa la contraseña." }; W.cur = p.ssid; if (!W.saved.includes(p.ssid)) W.saved.push(p.ssid); return { ok: true }; },
    "wifi.forget": p => { const W = Q.wifi; W.saved = W.saved.filter(s => s !== p.name); if (W.cur === p.name) W.cur = null; return { ok: true }; },
    "wifi.test": async () => { await new Promise(r => setTimeout(r, 200)); return Q.noNet ? { ok: false, portal: false, steam: false, ms: 0 } : { ok: true, steam: true, ms: 24 }; },
    "steam.me": () => ({ code: 39734321, steamid: "76561197999999049" }),
    "steam.lookup": p => p.code === "55555555" ? { ok: true, steamid: "76561198015820283", name: "Mario", avatar: null, friend: false } : { ok: false, error: "No hay ninguna cuenta con ese código" },
    "steam.addfriend": () => true,
    "steam.friends.refresh": () => { Q.refreshed = (Q.refreshed || 0) + 1; return [{ name: "NeoKaiser", steamid: "1", status: "playing", game: "ELDEN RING", avatar: "" }, { name: "Luna", steamid: "2", status: "online" }, { name: "Zed", steamid: "3", status: "offline", last: "ayer" }]; },
    "download.pause": () => (Q.dlFail ? { ok: false } : { ok: true }),
    "download.resume": () => ({ ok: true }),
    "download.cancel": async p => { if (Q.cancelFail) return { ok: false, error: "Steam no terminó de cancelarla." }; if (Q.cancelSlow) await new Promise(r => setTimeout(r, 800)); setTimeout(() => emit("download.removed", { appid: p.appid }), 50); return Q.cancelUpdate ? { ok: true, update: true } : { ok: true }; },
    "download.now": () => (Q.nowFail ? { ok: false } : { ok: true }),
    "install.prepare": () => { const I = Q.inst; if (I.seq.length) { const x = I.seq.shift(); if (x === "throw") throw new Error("Steam no contestó"); return x; } return { mode: "cdp", size: I.size, folder: I.folder != null ? I.folder : 0, canChange: true, folders: JSON.parse(JSON.stringify(I.folders)) }; },
    "install.folder": p => ({ ok: true, size: Q.inst.size, folder: p.folder }),
    "install.go": () => { const g = Q.inst.go; if (Array.isArray(g)) return g.shift() || { s: "started" }; return g || { s: "started" }; },
    "install.eula": p => { if (!p.accept) return { s: "canceled" }; const e = Q.inst.eula; if (Array.isArray(e)) return e.shift() || { s: "started" }; return e || { s: "started" }; },
    "install.cancel": () => true,
    "install.enable": () => { if (Q.inst.enableRefuse) return { ok: false, error: Q.inst.enableRefuse }; setTimeout(() => { if (Q.inst.enableOk) Q.inst.seq = []; emit("install.enabled", { ok: Q.inst.enableOk, error: Q.inst.enableOk ? null : "Steam no abrió la conexión. Se usará la ventanita de Steam." }); }, Q.inst.enableMs); return { ok: true }; },
    // SteamGridDB y la caché de imágenes: sin red, salvo que la prueba lo pida (beta 1.5: fondos y logos)
    "sgdb": p => { if (Q.sgdb && /^(logos|heroes)\//.test(p.path)) return { data: [1, 2, 3].map(i => ({ url: `data:image/svg+xml,${encodeURIComponent(`<svg xmlns='http://www.w3.org/2000/svg' width='600' height='200'><text x='10' y='120' font-size='90' fill='white'>${p.path.split('/')[0]} ${i}</text></svg>`)}`, width: 600, height: 200 })) }; throw new Error("sin key"); },
    "cache.image": p => { if (Q.cacheOk) return p.url; throw new Error("offline"); },
    "art.file": p => ({ ok: true, url: "https://files.scarlet/f?p=" + encodeURIComponent(p.path) })
  };
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener: (t, f) => L.push(f),
    postMessage: ({ id, type, payload }) => {
      Q.calls.push(type); Q.log.push({ type, payload: payload || {} });
      const send = m => setTimeout(() => L.forEach(f => f({ data: m })), Q.slow[type] || 15);
      if (Q.fail.has(type)) return send({ id, ok: false, error: "falla simulada" });
      const h = R[type];
      if (!h) { Q.unknown = (Q.unknown || []).concat(type); return send({ id, ok: false, error: "Mensaje desconocido: " + type }); }
      try { send({ id, ok: true, data: h(payload || {}) }); } catch (e) { send({ id, ok: false, error: e.message }); }
    }
  };
})();
