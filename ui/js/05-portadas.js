/* ================= portadas (SteamGridDB + respaldo propio) ================= */
const Art = {
  cache: store.get("artCache", {}),
  alt: store.get("artAlt", {}),
  placeholder(g, kind = "grid") {
    const n = this.alt[g.appid] || 0, h = (g.appid * 47 + n * 67) % 360, h2 = (h + 55) % 360;
    const [w, ht] = kind === "hero" ? [1920, 1080] : [512, 512];
    const ini = g.name.split(/\s+/).filter(x => /[\p{L}\p{N}]/u.test(x)).slice(0, 2).map(x => x.match(/[\p{L}\p{N}]/u)[0]).join("").toUpperCase();
    const text = kind === "hero" ? "" :
      `<text x="50%" y="50%" text-anchor="middle" dominant-baseline="middle" font-family="Segoe UI,Arial" font-weight="800" font-size="200" fill="#fff" opacity=".93">${xml(ini)}</text>` +
      `<text x="50%" y="88%" text-anchor="middle" font-family="Segoe UI,Arial" font-weight="700" font-size="34" fill="#fff" opacity=".8">${xml(trunc(g.name, 24))}</text>`;
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${ht}" viewBox="0 0 ${w} ${ht}"><defs><linearGradient id="a" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="hsl(${h},68%,54%)"/><stop offset="1" stop-color="hsl(${h2},72%,26%)"/></linearGradient></defs><rect width="100%" height="100%" fill="url(#a)"/><circle cx="${w * .84}" cy="${ht * .16}" r="${ht * .46}" fill="#fff" opacity=".08"/><circle cx="${w * .08}" cy="${ht * .98}" r="${ht * .38}" fill="#000" opacity=".14"/>${text}</svg>`;
    return "data:image/svg+xml;charset=utf-8," + encodeURIComponent(svg);
  },
  async sgdb(path) {
    if (Host.has) return Host.call("sgdb", { path });
    if (!settings.sgdbKey) throw new Error("sin key");
    const r = await fetch("https://www.steamgriddb.com/api/v2/" + path, { headers: { Authorization: "Bearer " + settings.sgdbKey } });
    if (!r.ok) throw new Error("HTTP " + r.status);
    return r.json();
  },
  async gameId(g) {   // juegos que no son de Steam: se buscan por nombre
    const k = "sgid:" + norm(g.name);
    if (this.cache[k]) return this.cache[k];
    const j = await this.sgdb(`search/autocomplete/${encodeURIComponent(g.name)}`);
    const id = j && j.data && j.data[0] && j.data[0].id;
    if (id) { this.cache[k] = id; store.set("artCache", this.cache); }
    return id;
  },
  pick: store.get("artPick", {}),
  icons: new Set(),
  steamUrl(g, kind = "grid") { return `https://cdn.cloudflare.steamstatic.com/steam/apps/${g.appid}/${kind === "hero" ? "library_hero.jpg" : "library_600x900.jpg"}`; },
  async local(url) { return Host.has ? Host.call("cache.image", { url }) : url; },
  /* todas las portadas de SteamGridDB para elegir: mode "square" | "all" */
  async options(g, mode, page) {
    const steam = (g.source || "steam") === "steam";
    const id = steam ? g.appid : await this.gameId(g); if (!id) return [];
    // sin filtros de "humor" ni "epilepsia" (SteamGridDB los esconde por defecto); los íconos se piden aparte
    const q = ["nsfw=false", "humor=any", "epilepsy=any", "types=static,animated", "page=" + page];
    if (mode === "square") q.push("dimensions=512x512,1024x1024");
    if (mode === "icons") q.push("mimes=image/png");
    const j = await this.sgdb(`${mode === "icons" ? "icons" : "grids"}/${steam ? "steam" : "game"}/${id}?${q.join("&")}`);
    return ((j && j.data) || []).map(x => ({ url: x.url, thumb: x.thumb || x.url, w: x.width, h: x.height, author: x.author && x.author.name, anim: /webm|gif/i.test(x.mime || "") || /animated/i.test(x.style || "") }));
  },
  async choose(g, url) {   // url de SteamGridDB, "steam" (arte oficial) o "none" (colores)
    if (url) this.pick[g.appid] = url; else delete this.pick[g.appid];
    store.set("artPick", this.pick);
  },
  async get(g, kind = "grid") {
    // juegos externos con su propia imagen (el ícono del programa o una imagen tuya)
    if (g.source === "ext" && g.art && !this.pick[g.appid]) { if (g.iconArt) this.icons.add(g.art); return kind === "grid" ? g.art : null; }
    const p = kind === "grid" && this.pick[g.appid];
    if (p === "none") return null;
    if (p === "xbox") return g.art || null;
    // juegos de la Xbox Store: la portada oficial de la tienda (la trae la app), salvo que elijas otra
    if (!p && g.source === "xbox" && (kind === "hero" ? g.hero || g.art : g.art)) return kind === "hero" ? g.hero || g.art : g.art;
    if (p) {
      const key = `pick:${g.appid}:${p}`;
      if (this.cache[key]) return this.cache[key];
      try {
        const url = await this.local(p === "steam" ? this.steamUrl(g) : p);
        if (url) { this.cache[key] = url; store.set("artCache", this.cache); return url; }
      } catch (e) {}
    }
    const n = this.alt[g.appid] || 0, key = `${kind}:${g.appid}:${n}${g.kind === "app" ? ":i" : ""}`;
    // apps web (YouTube…): el ícono oficial que declara el sitio, centrado en una tarjeta blanca
    if (Host.has && kind === "grid" && g.source === "app" && !n) {
      const wk = `web:${g.appid}`;
      if (this.cache[wk]) { this.icons.add(this.cache[wk]); return this.cache[wk]; }
      const wu = await Host.call("app.icon", { appid: g.appid }).catch(() => null);
      if (wu) { this.cache[wk] = wu; store.set("artCache", this.cache); this.icons.add(wu); return wu; }
    }
    if (this.cache[key]) return this.cache[key];
    if (!Host.has && !settings.sgdbKey) return null;
    try {
      const steam = (g.source || "steam") === "steam";
      const id = steam ? g.appid : await this.gameId(g); if (!id) return null;
      if (g.kind === "app" && kind === "grid") {   // YouTube, Apple Music…: el ícono de la app se ve mejor que una portada
        const ic = await this.sgdb(`icons/game/${id}?mimes=image/png`).catch(() => null);
        const il = (ic && ic.data) || [], big = il.filter(x => (x.width || 0) >= 256);
        let iu = (big.length ? big : il).length ? (big.length ? big : il)[n % (big.length ? big : il).length].url : null;
        if (iu && Host.has) iu = await Host.call("cache.image", { url: iu });
        if (iu) { this.cache[key] = iu; store.set("artCache", this.cache); return iu; }
      }
      const base = `${kind === "hero" ? "heroes" : "grids"}/${steam ? "steam" : "game"}/${id}`;
      const j = await this.sgdb(kind === "hero" ? base : base + "?dimensions=512x512,1024x1024");
      const list = (j && j.data) || []; let url = list.length && list[n % list.length].url;
      if (url && Host.has) url = await Host.call("cache.image", { url });
      if (url) { this.cache[key] = url; store.set("artCache", this.cache); return url; }
    } catch (e) { /* sin red, sin key o CORS: se intenta el respaldo */ }
    if (Host.has && (g.source || "steam") === "steam" && g.kind !== "app") {   // respaldo: arte oficial de Steam
      try {
        const url = await Host.call("cache.image", { url: `https://cdn.cloudflare.steamstatic.com/steam/apps/${g.appid}/${kind === "hero" ? "library_hero.jpg" : "library_600x900.jpg"}` });
        if (url) { this.cache[key] = url; store.set("artCache", this.cache); return url; }
      } catch (e) {}
    }
    return null;
  },
  apply(img, g, kind = "grid", retry = true) {
    if (retry) img.src = this.placeholder(g, kind);
    this.get(g, kind).then(u => {
      if (!u) return;
      const im = new Image();
      im.onload = () => { img.src = u; img.classList.toggle("appicon", kind === "grid" && this.icons.has(u)); };
      // si la imagen guardada ya no existe (se borró la caché o cambió de carpeta), se olvida y se vuelve a descargar
      im.onerror = () => { if (!retry) return; this.forget(u); this.apply(img, g, kind, false); };
      im.src = u;
    });
  },
  forget(u) {
    let changed = false;
    for (const k in this.cache) if (this.cache[k] === u) { delete this.cache[k]; changed = true; }
    if (changed) store.set("artCache", this.cache);
  },
  next(g) { this.alt[g.appid] = (this.alt[g.appid] || 0) + 1; store.set("artAlt", this.alt); },
  clear() { this.cache = {}; store.set("artCache", {}); }
};

