/* --- grupos de un juego: marcar / desmarcar y crear nuevos --- */
function groupsView(g, onChange) {
  return {
    hints: [["B", "Atrás"], ["A", "Marcar"]],
    render(el) {
      const names = groupNames();
      el.innerHTML = head(I.all, "Grupos", g.name) + `<div class="vbody">${names.map((n, i) => `<button class="lrow fi" data-i="${i}"><span class="meta"><b>${esc(n)}</b><span>${(settings.groups[n] || []).length} en el grupo</span></span><span class="val">${inGroup(g, n) ? "✓" : ""}</span></button>`).join("")}<button class="lrow fi" data-new="1"><span class="meta"><b>Nuevo grupo…</b><span>Crea un grupo y agrega ${esc(g.name)}</span></span><span class="val">＋</span></button></div>`;
      fis(el).forEach(b => b.onclick = () => {
        if (b.dataset.new) return openView(keyboardView("", name => {
          name = name.trim(); if (!name) return;
          settings.groups = settings.groups || {}; settings.groups[name] = [...new Set([...(settings.groups[name] || []), g.appid])];
          saveSettings(); toast(`${g.name} se agregó a ${name}`); this.render(this.el); updateFocus(); onChange && onChange();
        }, null, { title: "Nombre del grupo", max: 24 }));
        const n = groupNames()[+b.dataset.i], list = new Set(settings.groups[n] || []);
        if (list.has(g.appid)) list.delete(g.appid); else list.add(g.appid);
        if (list.size) settings.groups[n] = [...list]; else delete settings.groups[n];
        saveSettings(); play("select"); toast(inGroup(g, n) ? `Se agregó a ${n}` : `Se quitó de ${n}`);
        this.render(this.el); updateFocus(); onChange && onChange();
      });
    },
    items() { return fis(this.el); }
  };
}

/* --- elegir portada entre todas las de SteamGridDB --- */
const ART_MODES = [["square", "Cuadradas"], ["icons", "Íconos"], ["all", "Todas"]];
function artPickerView(g, onChosen) {
  const steam = (g.source || "steam") === "steam" && g.kind !== "app";
  const media = (u, cls = "") => /\.(webm|mp4)(\?|$)/i.test(u) ? `<video class="${cls}" src="${esc(u)}" autoplay loop muted playsinline></video>` : `<img class="${cls}" alt="" loading="lazy" src="${esc(u)}">`;
  return {
    cls: "apick", mode: 0, page: 0, list: [], done: false, loading: false, err: "",
    hints() { return [["B", "Atrás"], ["Y", ART_MODES[(this.mode + 1) % ART_MODES.length][1]], ["A", "Usar esta"]]; },
    render(el) {
      el.innerHTML = head(I.album, "Elegir portada", g.name) + `<div class="tabs"><span class="chip">Y</span>${ART_MODES.map(([, l], i) => `<span class="tab${i === this.mode ? " on" : ""}">${l}</span>`).join("")}</div><div class="vbody"><div class="grid"></div></div>`;
      this.reload();
    },
    reload() {
      this.page = 0; this.list = []; this.done = false; this.err = ""; this.i = 0; this.loading = false; this.gen = (this.gen || 0) + 1;
      $$(".tab", this.el).forEach((t, i) => t.classList.toggle("on", i === this.mode));
      this.draw(); this.more();
    },
    fixed() {
      const f = [];
      if (steam) f.push({ url: "steam", thumb: Art.steamUrl(g), label: "Oficial de Steam", vert: true });
      if (g.source === "xbox" && g.art) f.push({ url: "xbox", thumb: g.art, label: "Oficial de Xbox Store" });
      f.push({ url: "none", thumb: Art.placeholder(g), label: "Colores (sin imagen)" });
      return f;
    },
    tile(o, i, cur) {
      return `<button class="tile fi${o.vert || (o.w && o.h && o.h > o.w * 1.15) ? " vert" : ""}" data-i="${i}" style="--k:${i % 10}">${media(o.thumb)}${o.url === cur ? `<span class="ck">✓</span>` : ""}${o.anim ? `<span class="tag">Animada</span>` : o.label ? "" : o.w ? `<span class="tag">${o.w}×${o.h}</span>` : ""}<span class="cap">${esc(o.label || (o.author ? "por " + o.author : "SteamGridDB"))}</span></button>`;
    },
    status() {
      return this.loading ? "Buscando portadas…" : this.err ? this.err : this.done ? (this.list.length ? `${this.list.length} ${this.list.length === 1 ? "imagen" : "imágenes"} de SteamGridDB` : "SteamGridDB no tiene portadas de este tipo para este juego. Prueba con Y.") : "";
    },
    /* dibuja todo (o solo agrega las nuevas, para que no parpadeen las que ya están) */
    draw(append) {
      const grid = $(".grid", this.el), cur = Art.pick[g.appid] || "";
      const all = [...this.fixed(), ...this.list], from = append ? (this.all || []).length : 0;
      this.all = all;
      if (!append) grid.innerHTML = `<p class="more"></p>`;
      const more = $(".more", grid);
      more.insertAdjacentHTML("beforebegin", all.slice(from).map((o, k) => this.tile(o, from + k, cur)).join(""));
      more.textContent = this.status();
      fis(grid).slice(from).forEach(b => b.onclick = () => this.choose(this.all[+b.dataset.i]));
      // el arte oficial de Steam lo resuelve la app (los juegos nuevos ya no usan la dirección clásica)
      if (!append && Host.has && steam) Host.call("cache.image", { url: Art.steamUrl(g) }).then(u => { const im = $(".tile img", grid); if (u && im) im.src = u; }).catch(() => {});
      if (topView() === this) updateFocus();
    },
    async more() {
      if (this.loading || this.done) return;
      this.loading = true; $(".more", this.el).textContent = this.status();
      const gen = this.gen;
      try {
        const items = await Art.options(g, ART_MODES[this.mode][0], this.page);
        if (gen !== this.gen) return;
        const seen = new Set(this.list.map(x => x.url));
        const fresh = items.filter(x => x.url && !seen.has(x.url));
        this.list.push(...fresh); this.page++;
        if (!fresh.length) this.done = true;
      } catch (e) {
        if (gen !== this.gen) return;
        this.done = true;
        this.err = /key/i.test(String(e && e.message)) ? "Falta la API key de SteamGridDB (Ajustes › Cuentas y arranque)." : "No se pudo consultar SteamGridDB. Revisa tu conexión.";
      }
      this.loading = false;
      if (views.includes(this)) this.draw(true);
    },
    onFocus(i) { if (i >= (this.all || []).length - 10) this.more(); },
    async choose(o) {
      if (!o) return;
      play("select");
      await Art.choose(g, o.url);
      toast("Portada actualizada");
      closeView(); renderHome();
      const lv = views.find(v => v.refresh && v.list && v.cols === 6); if (lv) lv.refresh(true);
      if (onChosen) onChosen();
    },
    cols: 5,
    onKey(k) {
      if (k === "y") { this.mode = (this.mode + 1) % ART_MODES.length; play("select"); this.reload(); return true; }
      if (k === "lb" || k === "rb") {
        const n = this.items().length; if (!n) return true;
        const i = clamp(this.i + (k === "lb" ? -10 : 10), 0, n - 1);
        if (i === this.i) bump(this.items()[this.i], k === "lb" ? "up" : "down"); else { this.i = i; play("tab"); updateFocus(); }
        return true;
      }
      return false;
    },
    items() { return fis(this.el); }
  };
}

/* --- biblioteca: colecciones, orden y búsqueda --- */
const SORTS = ["Recientes", "A–Z", "Más jugados", "Solo instalados"];
const lib = { col: 0, sort: 0, query: "" };
function collections() {
  const names = [];
  games.forEach(g => (g.cols || []).forEach(c => { if (!names.includes(c)) names.push(c); }));
  const order = ["Favoritos", "Acción", "RPG", "Indies"];
  names.sort((a, b) => (order.indexOf(a) + 1 || 99) - (order.indexOf(b) + 1 || 99));
  const out = ["Todos", ...groupNames().filter(n => !names.includes(n)), ...names];
  if (games.some(g => g.kind === "app")) out.push("Aplicaciones");
  if (games.some(isHidden)) out.push("Ocultos");
  return out;
}
function libraryList(query = lib.query) {
  const cols = collections(), col = cols[clamp(lib.col, 0, cols.length - 1)], q = norm(query.trim());
  let list = games.filter(g => col === "Ocultos" ? isHidden(g) : isHidden(g) ? false : col === "Todos" ? true : col === "Aplicaciones" ? g.kind === "app" : (g.cols || []).includes(col) || inGroup(g, col));
  if (q) list = list.filter(g => norm(g.name).includes(q));
  if (lib.sort === 1) list = [...list].sort((a, b) => a.name.localeCompare(b.name, "es"));
  else if (lib.sort === 2) list = [...list].sort((a, b) => (b.hours || 0) - (a.hours || 0));
  else if (lib.sort === 3) list = list.filter(g => g.installed);
  return list;
}
function libraryView() {
  return {
    cols: 6,
    hints() { return [["B", lib.query ? "Quitar búsqueda" : "Atrás"], ["Y", "Ordenar"], ["☰", "Buscar"], ["X", "Opciones"], ["A", "Iniciar"]]; },
    render(el) {
      el.innerHTML = head(I.all, "Biblioteca") + `<div class="tabs"></div><div class="vbody"><div class="grid"></div></div>`;
      this.refresh();
    },
    refresh(keepI) {
      const cols = collections(); lib.col = clamp(lib.col, 0, cols.length - 1);
      $(".tabs", this.el).innerHTML = `<span class="chip" data-key="lt">LT</span>` + cols.map((c, i) => `<span class="tab${i === lib.col ? " on" : ""}" data-col="${i}">${esc(c)}</span>`).join("") + `<span class="chip" data-key="rt">RT</span>`;
      $$(".tab", this.el).forEach(t => t.onclick = () => { if (+t.dataset.col === lib.col) return; lib.col = +t.dataset.col; play("select"); this.refresh(); });
      this.list = libraryList();
      $(".vhead .sub", this.el).textContent = `${lib.query ? `“${lib.query}” · ` : ""}${this.list.length} · ${SORTS[lib.sort]}`;
      const grid = $(".grid", this.el); grid.innerHTML = "";
      if (!this.list.length) grid.outerHTML = `<div class="grid"><p class="empty" style="grid-column:1/-1">No hay nada por aquí.</p></div>`;
      else this.list.forEach((g, i) => {
        const b = buildTile(g, "tile fi"); b.style.setProperty("--k", i % 12);
        b.insertAdjacentHTML("beforeend", `<span class="cap">${esc(g.name)}</span>`);
        b.onclick = () => {   // táctil: primer toque elige, segundo abre (A abre directo)
          if (!b.classList.contains("focused")) { this.i = i; play("move"); return updateFocus(); }
          launchGame(g, b);
        };
        grid.append(b);
      });
      if (!keepI) this.i = 0;
      updateFocus();
    },
    onKey(k) {
      if (k === "lt" || k === "rt") {
        const n = collections().length, c = clamp(lib.col + (k === "lt" ? -1 : 1), 0, n - 1);
        if (c === lib.col) { bump($(".tabs .on", this.el), k === "lt" ? "left" : "right"); return true; }
        lib.col = c; play("tab"); this.refresh(); return true;
      }
      if (k === "y") { lib.sort = (lib.sort + 1) % SORTS.length; play("select"); this.refresh(); toast("Orden: " + SORTS[lib.sort]); return true; }
      if (k === "start") { openView(keyboardView(lib.query, txt => { lib.query = txt.trim(); this.refresh(); }, txt => libraryList(txt).length)); return true; }
      if (k === "lb" || k === "rb") {
        const n = this.list.length; if (!n) return true;
        const i = clamp(this.i + (k === "lb" ? -12 : 12), 0, n - 1);
        if (i === this.i) bump(this.items()[this.i], k === "lb" ? "up" : "down"); else { this.i = i; play("tab"); updateFocus(); }
        return true;
      }
      return false;
    },
    onB() { if (!lib.query) return false; lib.query = ""; play("back"); this.refresh(); return true; },
    onX() { const g = this.list[this.i]; if (!g) play("bump"); else if (g.appid === state.running) askCloseRunning(); else openView(propsView(g)); return true; },
    items() { return fis(this.el); }
  };
}

