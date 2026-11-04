/* ================= beta 1.2: Perfil como en la consola =================
   Dos columnas (como Ajustes): Perfil (avatar, lo que juegas, tu código de amigo, historial de juego con horas),
   Lista de amigos (tarjetas, ★ primero, Y para actualizar), Añadir un amigo (por código de amigo) y Ajustes de usuario. */
const ME = { code: 0 };
const fmtHours = h => { const x = +h || 0; return x <= 0 ? "" : x < 1 ? `${Math.max(1, Math.round(x * 60))} min` : `${x >= 10 ? Math.round(x) : x.toFixed(1)} h`; };
const friendCode = n => { const s = String(n || ""); return s ? s.replace(/\B(?=(\d{4})+(?!\d))/g, " ") : "—"; };
function profileView() {
  const PCATS = ["Perfil", "Lista de amigos", "Añadir un amigo", "Ajustes de usuario"];
  const COLS = 3;
  const order = { playing: 0, online: 1, away: 2, offline: 3 };
  return {
    isProfile: true, side: "L", ci: 0, ri: 0, rows: [],
    syncFromIndex() { if (this.side === "R") this.ri = this.i; },
    hints() {
      if (this.side === "L") return this.ci === 1 ? [["B", "Atrás"], ["Y", "Actualizar"], ["A", "Aceptar"]] : [["B", "Atrás"], ["A", "Aceptar"]];
      if (this.ci === 1) return [["B", "Atrás"], ["Y", "Actualizar"], ["A", "Ver perfil"]];
      return [["B", "Atrás"], ["A", "Aceptar"]];
    },
    render(el) {
      el.innerHTML = head(I.friends, "Perfil", profile.name || "") + `<div class="nsb"><div class="nsl">${PCATS.map((c, i) => `<button class="nscat fi" data-c="${i}">${esc(c)}</button>`).join("")}</div><div class="nsr ppane"></div></div>`;
      $$(".nscat", el).forEach(b => b.onclick = () => { const c = +b.dataset.c; if (this.side === "L" && c === this.ci) return this.enter(); this.side = "L"; this.ci = c; this.i = c; this.pane(); updateFocus(); });
      this.pane();
      if (Host.has && !ME.code) Host.call("steam.me").then(m => { if (m && m.code) { ME.code = m.code; if (this.el.isConnected && (this.ci === 0 || this.ci === 2)) this.pane(true); } }).catch(() => {});
      if (!Host.has && !ME.code) ME.code = 123456789;
    },
    /* La columna derecha. keep: no mover el foco (solo se actualizaron datos). */
    pane(keep) {
      const p = $(".ppane", this.el), rows = this.rows = [];
      $$(".nscat", this.el).forEach(b => b.classList.toggle("sel", +b.dataset.c === this.ci));
      const add = (html, act, extra) => { const k = rows.length; rows.push({ act, ...extra }); return html.replace("<button", `<button data-r="${k}"`); };
      let h = "";
      if (this.ci === 0) {
        const real = games.filter(g => g.kind !== "app"), hours = real.reduce((s, g) => s + (+g.hours || 0), 0), ach = real.reduce((s, g) => s + (g.ach ? g.ach[0] : 0), 0);
        const playing = state.running && byId(state.running);
        h += `<div class="pme"><span class="avatar pav">${avatarHTML()}</span><div class="pmeta"><h2>${esc(profile.name || "Jugador")}</h2>
          <span>${playing ? `<span class="dot" style="background:#7bd84a"></span>Jugando ${esc(playing.name)}` : `<span class="dot" style="background:#3ad7ff"></span>En línea`}</span>
          <span>Nivel de Steam ${esc(profile.level ?? "—")}</span><span class="pcode">Código de amigo: <b>${esc(friendCode(ME.code))}</b></span></div></div>
          <div class="stats"><div class="stat"><b>${real.length}</b>Juegos</div><div class="stat"><b>${Math.round(hours)}</b>Horas jugadas</div><div class="stat"><b>${ach}</b>Logros</div><div class="stat"><b>${friends.filter(f => f.status !== "offline").length}</b>Amigos en línea</div></div>`;
        h += add(`<button class="lrow fi"><span class="meta"><b>Logros</b><span>Progreso por juego</span></span><span class="val">›</span></button>`, () => openView(achHubView()));
        const hist = real.filter(g => (g.lastTs || 0) > 0 || (+g.hours || 0) > 0).sort((a, b) => (b.lastTs || 0) - (a.lastTs || 0) || (+b.hours || 0) - (+a.hours || 0)).slice(0, 30);
        h += `<div class="nssec">Historial de juego</div>` + (hist.length ? hist.map(g => add(`<button class="lrow fi phist"><img class="mini" alt="" data-art="${g.appid}"><span class="meta"><b>${esc(g.name)}</b><span>${g.last && g.last !== "nunca" ? "Jugado por última vez: " + esc(g.last) : "Sin jugar todavía"}</span></span><span class="val">${fmtHours(g.hours)}</span></button>`, () => openView(propsView(g)))).join("")
          : `<p class="note">Todavía no hay juegos jugados.</p>`);
      } else if (this.ci === 1) {
        const list = [...friends].sort((a, b) => (isBest(b.name) - isBest(a.name)) || order[a.status] - order[b.status] || a.name.localeCompare(b.name));
        const on = list.filter(f => f.status !== "offline").length;
        h += `<p class="pcount">${on} en línea · ${list.length} en total</p>`;
        h += list.length ? `<div class="fgrid">${list.map(f => {
          const [c, txt] = STATUS[f.status] || STATUS.offline;
          return add(`<button class="fcardb fi"><span class="fav" style="background:hsl(${hueOf(f.name)},55%,48%)">${f.avatar ? `<img alt="" src="${esc(f.avatar)}">` : esc(f.name[0].toUpperCase())}</span><b>${esc(f.name)}${isBest(f.name) ? '<i class="nstar">★</i>' : ""}</b><span><span class="dot" style="background:${c}"></span>${esc(trunc(txt(f.game || ""), 26))}</span></button>`, () => openView(friendProfileView(f)), { grid: true, name: f.name });
        }).join("")}</div>` : `<p class="note">${Host.has ? "Tu lista de amigos está vacía o Steam no la deja ver (revisa tu API key en Cuentas y arranque)." : "Sin amigos."}</p>`;
      } else if (this.ci === 2) {
        h += `<div class="nssec">Buscar</div>` + add(`<button class="lrow fi"><span class="meta"><b>Buscar por código de amigo</b><span>El número de cuenta de Steam de tu amigo (lo ve en su perfil de Scarlet o en Steam › Amigos › Añadir un amigo)</span></span><span class="val">›</span></button>`, () => this.search());
        h += `<div class="nssec">Tu código</div><div class="pcodebig"><span>Tu código de amigo</span><b>${esc(friendCode(ME.code))}</b><span>Compártelo para que te agreguen</span></div>`;
        h += `<div class="nssec">Solicitudes</div>` + add(`<button class="lrow fi"><span class="meta"><b>Solicitudes de amistad y jugadores recientes</b><span>Las solicitudes recibidas y enviadas se ven en Steam (Scarlet todavía no puede leerlas)</span></span><span class="val">Abrir ›</span></button>`, () => openSystemApp({ ...SYS.chat, name: "Solicitudes de amistad", url: "steam://open/friends" }));
      } else {
        h += `<div class="nssec">Tu cuenta</div>`;
        if (Host.has) h += add(`<button class="lrow fi"><span class="meta"><b>Cuentas y arranque</b><span>Claves de Steam y SteamGridDB, inicio con Windows, botón Xbox</span></span><span class="val">›</span></button>`, () => openView(accountsView()));
        h += add(`<button class="lrow fi"><span class="meta"><b>Ver tu perfil en Steam</b><span>Se abre en Steam Big Picture</span></span><span class="val">›</span></button>`, () => openSystemApp({ ...SYS.store, name: "Tu perfil de Steam", sub: "Abierto en Steam Big Picture", url: "steam://url/SteamIDPage/" + (ME.code ? (76561197960265728n + BigInt(ME.code)).toString() : "") }));
        h += `<div class="nssec">Avisos de amigos</div>`;
        h += add(`<button class="lrow fi"><span class="meta"><b>Amigos que se conectan</b><span>Todos, solo tus mejores amigos (★) o nadie</span></span><span class="val pnf1">${esc(optLabel(NF_FRIENDS, settings.nFriendOn))}</span></button>`, () => pickOption("Amigos que se conectan", NF_FRIENDS, "nFriendOn"));
        h += add(`<button class="lrow fi"><span class="meta"><b>Amigos que empiezan a jugar</b><span>Todos, solo tus mejores amigos (★) o nadie</span></span><span class="val pnf2">${esc(optLabel(NF_FRIENDS, settings.nFriendPlay))}</span></button>`, () => pickOption("Amigos que empiezan a jugar", NF_FRIENDS, "nFriendPlay"));
      }
      p.innerHTML = h;
      $$("[data-art]", p).forEach(im => Art.apply(im, byId(im.dataset.art) || { appid: +im.dataset.art, name: "" }));
      $$("[data-r]", p).forEach(b => b.onclick = () => { const r = +b.dataset.r; this.side = "R"; this.ri = r; this.i = r; updateFocus(); this.rows[r].act(); });
      if (keep && this.side === "R") { this.ri = Math.min(this.ri, Math.max(0, rows.length - 1)); this.i = this.ri; }
      if (!keep && this.side === "L") p.scrollTop = 0;
      if (keep) updateFocus();
    },
    async search() {
      openView(keyboardView("", async code => {
        if (!code) return;
        const r = Host.has ? await Host.call("steam.lookup", { code }).catch(e => ({ ok: false, error: e.message })) : (code === "987654321" ? { ok: true, steamid: "76561198947920049", name: "Amiga de prueba", avatar: null, friend: false } : { ok: false, error: "No hay ninguna cuenta con ese código" });
        if (!r || !r.ok) return alertDialog((r && r.error) || "No se pudo buscar");
        if (r.friend) return alertDialog(`${r.name || "Esa cuenta"} ya es tu amigo.`);
        const ok = await confirmDialog(`${r.name ? r.name : "Cuenta " + friendCode(code)}\n¿Enviar una solicitud de amistad?`, "Enviar solicitud");
        if (!ok) return;
        const sent = Host.has ? await Host.call("steam.addfriend", { steamid: r.steamid }).catch(() => false) : true;
        toast(sent ? "Solicitud enviada con Steam" : "No se pudo enviar la solicitud");
      }, null, { title: "Código de amigo", placeholder: "Escribe el código de amigo…", keys: "digits", max: 10 }));
    },
    items() { return this.side === "L" ? $$(".nscat", this.el) : $$("[data-r]", this.el); },
    enter() {
      if (!this.rows.length) return bump(this.items()[this.i], "right");
      this.side = "R"; this.ri = 0; this.i = 0; play("move"); updateFocus();
    },
    nav(dir) {
      if (this.side === "L") {
        if (dir === "right") return this.enter();
        if (dir === "left") return bump(this.items()[this.i], dir);
        const c = this.ci + (dir === "down" ? 1 : -1);
        if (c < 0 || c >= PCATS.length) return bump(this.items()[this.i], dir);
        this.ci = c; this.i = c; this.pane(); play("move"); return updateFocus();
      }
      const grid = this.ci === 1, n = this.rows.length;
      if (dir === "left" && (!grid || this.ri % COLS === 0)) { this.side = "L"; this.i = this.ci; play("move"); return updateFocus(); }
      let j = this.ri;
      if (grid) j += dir === "left" ? -1 : dir === "right" ? 1 : dir === "down" ? COLS : -COLS;
      else { if (dir === "right") return bump(this.items()[this.i], dir); j += dir === "down" ? 1 : -1; }
      if (grid && dir === "down" && j >= n && Math.floor(this.ri / COLS) < Math.floor((n - 1) / COLS)) j = n - 1;   // última fila incompleta
      if (j < 0 || j >= n || (grid && dir === "right" && j % COLS === 0)) return bump(this.items()[this.i], dir);
      this.ri = j; this.i = j; play("move"); updateFocus();
    },
    onB() { if (this.side === "R") { this.side = "L"; this.i = this.ci; play("back"); updateFocus(); return true; } return false; },
    onKey(k) {
      if (k === "y" && this.ci === 1) {
        play("select"); toast("Actualizando la lista de amigos…");
        const done = list => { if (Array.isArray(list)) friends = list; if (this.el.isConnected && this.ci === 1) this.pane(true); };
        if (Host.has) Host.call("steam.friends.refresh").then(done).catch(() => toast("No se pudo actualizar"));
        else setTimeout(() => done(friends), 300);
        return true;
      }
      return false;
    },
    refresh() { if (this.el && this.el.isConnected) this.pane(true); },
    onReturn() { if (this.ci === 1) this.pane(true); },   // ★ cambiado en el perfil de un amigo
    onFocus() { },
  };
}

/* ================= amigos y logros: estados, colores, logros por juego ================= */
const STATUS = { playing: ["#7bd84a", g => "Jugando " + g], online: ["#3ad7ff", () => "En línea"], away: ["#f2c230", () => "Ausente"], offline: ["#77777d", () => "Desconectado"] };
const hueOf = s => [...s].reduce((a, ch) => a + ch.charCodeAt(0), 0) * 29 % 360;
function achView(g) {
  return {
    hints: [["B", "Atrás"]],
    render(el) {
      el.innerHTML = head(I.trophy, "Logros · " + g.name, "Cargando…") + `<div class="vbody"></div>`;
      Data.achievements(g).then(a => {
        $(".vhead .sub", el).textContent = `${a.unlocked} de ${a.total}`;
        $(".vbody", el).innerHTML = a.list.map(x => `<button class="lrow fi" style="${x.unlocked ? "" : "opacity:.55"}">${x.icon ? `<span class="fav achi">${achImg(x)}</span>` : `<span class="fav" style="background:${x.unlocked ? "#f2c230" : "var(--line)"};color:${x.unlocked ? "#3a2c00" : "var(--fg2)"}">${x.unlocked ? I.trophy : I.lock}</span>`}<span class="meta"><b>${esc(x.name)}</b><span>${esc(x.desc)}</span></span><span class="val">${x.unlocked ? esc(x.date || "Desbloqueado") : "Bloqueado"}</span></button>`).join("");
        updateFocus();
      });
    },
    items() { return fis(this.el); }
  };
}

function achHubView() {
  return {
    hints: [["B", "Atrás"], ["A", "Ver logros"]],
    render(el) {
      const list = games.filter(g => hasAch(g) && !isHidden(g)), tot = list.reduce((s, g) => s + g.ach[0], 0);
      el.innerHTML = head(I.trophy, "Logros", `${tot} desbloqueados`) + `<div class="vbody"></div>`;
      const body = $(".vbody", el);
      list.forEach(g => {
        const b = document.createElement("button"); b.className = "lrow fi";
        const img = document.createElement("img"); img.className = "mini"; img.alt = ""; Art.apply(img, g);
        const pct = Math.round(100 * g.ach[0] / g.ach[1]);
        b.append(img);
        b.insertAdjacentHTML("beforeend", `<span class="meta"><b>${esc(g.name)}</b><span>${g.ach[0]} de ${g.ach[1]}</span></span><div class="bar"><i style="width:${pct}%"></i></div><span class="val" style="width:5rem;text-align:right">${pct}%</span>`);
        b.onclick = () => openView(achView(g)); body.append(b);
      });
    },
    items() { return fis(this.el); }
  };
}

/* ================= v2.9.8: perfil de amigo ================= */
const seeded = (s, n) => [...s].reduce((a, c) => (a * 31 + c.charCodeAt(0)) >>> 0, 7) % n;
function friendProfileView(f) {
  const owned = f.game && games.find(g => g.name === f.game);
  const common = Host.has ? [] : games.filter(hasAch).filter((g, i) => (seeded(f.name, 5) + i) % 3 === 0).slice(0, 4);
  const commonRows = list => list.map(g => {
    const mine = hasAch(g) ? Math.round(100 * g.ach[0] / g.ach[1]) : 0, their = g.theirPct != null ? g.theirPct : seeded(f.name + g.name, 90) + 5;
    const theirH = g.theirHours != null ? Math.round(g.theirHours) : 5 + seeded(f.name + g.appid, 140);
    const bars = Host.has ? "" : `<span class="cmp"><span>Tú</span><span class="bar"><i style="width:${mine}%"></i></span><span>${esc(trunc(f.name, 10))}</span><span class="bar them"><i style="width:${their}%"></i></span></span>`;
    return `<button class="lrow fi" data-g="${g.appid}"><img class="mini" alt="" data-art="${g.appid}"><span class="meta"><b>${esc(g.name)}</b><span>${Math.round(+g.hours || 0)} h tú · ${theirH} h ${esc(f.name)}</span></span>${bars}</button>`;
  }).join("");
  return {
    hints: [["B", "Atrás"], ["A", "Aceptar"]],
    render(el) {
      const [c, txt] = STATUS[f.status] || STATUS.offline;
      const last = f.status === "offline" ? (f.last || (Host.has ? "Desconectado" : `Última conexión: hace ${1 + seeded(f.name, 9)} h`)) : txt(f.game || "");
      const level = Host.has ? (f.level != null ? `Nivel de Steam ${f.level}` : "") : `Nivel de Steam ${8 + seeded(f.name, 60)}`;
      el.innerHTML = head(I.friends, f.name, "Perfil") + `<div class="vbody"><div class="fprof">
        <div class="fcard"><span class="fbig" style="background:hsl(${hueOf(f.name)},55%,48%)">${f.avatar ? `<img alt="" src="${esc(f.avatar)}" style="width:100%;height:100%;border-radius:50%;object-fit:cover">` : esc(f.name[0].toUpperCase())}</span><h2>${esc(f.name)}</h2>
          <span class="fst"><span class="dot" style="background:${c}"></span>${esc(last)}</span><span class="flv">${esc(level)}</span></div>
        <div>
          ${f.status === "playing" ? `<div class="fnow"><img alt=""><div><span>Jugando ahora</span><b>${esc(f.game)}</b><span>${owned ? "Lo tienes en tu biblioteca" : "No está en tu biblioteca"}</span></div></div>` : ""}
          <div class="fbtns">
            ${f.status === "playing" ? `<button class="btn primary fi" data-a="join">Unirse a la partida</button>` : ""}
            <button class="btn fi" data-a="msg">Enviar mensaje</button><button class="btn fi fbest${isBest(f.name) ? " on" : ""}" data-a="best">${isBest(f.name) ? "★ Mejor amigo" : "☆ Mejores amigos"}</button><button class="btn fi" data-a="opts">Opciones</button>
          </div>
          <div class="sect">Historial de juego</div>
          <div class="frecent">${Host.has ? `<p class="note">Cargando…</p>` : (owned ? `<button class="lrow fi"><img class="mini" alt="" data-art="${owned.appid}"><span class="meta"><b>${esc(owned.name)}</b><span>${5 + seeded(f.name, 20)} h en las últimas 2 semanas</span></span></button>` : `<p class="note">No ha jugado nada en las últimas 2 semanas.</p>`)}</div>
          <div class="sect">Juegos en común</div>
          <div class="fcommon">${Host.has ? `<p class="note">Cargando…</p>` : (commonRows(common) || `<p class="note">No tienen juegos en común con logros.</p>`)}</div>
        </div></div></div>`;
      if (owned || f.status === "playing") { const im = $(".fnow img", el); if (im) Art.apply(im, owned || { appid: seeded(f.game, 99999), name: f.game }); }
      const wire = () => { $$("[data-art]", el).forEach(im => Art.apply(im, byId(im.dataset.art) || { appid: +im.dataset.art, name: "" })); $$("[data-g]", el).forEach(b => b.onclick = () => { const g = byId(b.dataset.g); if (g && hasAch(g)) openView(achView(g)); else bump(b); }); };
      wire();
      if (Host.has) Host.call("steam.friendProfile", { steamid: f.steamid }).then(d => {
        if (!el.isConnected) return;
        if (d.level != null) $(".flv", el).textContent = `Nivel de Steam ${d.level}`;
        if (f.status === "offline" && d.lastSeen) $(".fst", el).lastChild.textContent = "Última conexión: " + d.lastSeen;
        const rec = d.recent || [];
        $(".frecent", el).innerHTML = rec.length ? rec.map(x => `<button class="lrow fi" data-g="${x.appid}"><img class="mini" alt="" data-art="${x.appid}"><span class="meta"><b>${esc(x.name)}</b><span>${x.weeks2} h en las últimas 2 semanas · ${Math.round(x.total)} h en total</span></span></button>`).join("")
          : `<p class="note">${d.private ? "Su historial de juego es privado." : "No ha jugado nada en las últimas 2 semanas."}</p>`;
        const list = (d.common || []).map(x => ({ ...(byId(x.appid) || { appid: x.appid, name: x.name, ach: [0, 0] }), hours: x.mine, theirHours: x.theirs }));
        $(".fcommon", el).innerHTML = commonRows(list) || `<p class="note">${d.private ? "Su biblioteca es privada." : "No tienen juegos en común."}</p>`;
        wire(); updateFocus();
      }).catch(() => { if (el.isConnected) $(".fcommon", el).innerHTML = `<p class="note">No se pudo cargar.</p>`; });
      const j = $('[data-a="join"]', el);
      if (j) j.onclick = async () => {
        if (!owned) { bump(j); return toast(`Necesitas ${f.game} para unirte`); }
        if (!owned.installed) { bump(j); return toast(`Primero instala ${f.game}`); }
        if (!Host.has) { toast(`Uniéndote a ${f.name}…`); return launchGame(owned, j); }
        const r = await Host.call("steam.join", { steamid: f.steamid, appid: owned.appid }).catch(() => null);
        if (!r || !r.ok) { bump(j); return toast(`La partida de ${f.name} no admite unirse ahora`); }
        toast(`Uniéndote a ${f.name}…`); launchGame(owned, j, { joined: true });
      };
      $('[data-a="msg"]', el).onclick = () => openSystemApp({ ...SYS.chat, name: "Chat con " + f.name, url: f.steamid ? `steam://friends/message/${f.steamid}` : SYS.chat.url });
      const steamPage = () => openSystemApp({ ...SYS.store, name: "Perfil de " + f.name, sub: "Abierto en Steam Big Picture", url: f.steamid ? `steam://url/SteamIDPage/${f.steamid}` : "steam://friends/players" });
      // ★ Mejores amigos (beta 1.2): sus avisos siguen la opción «solo mejores amigos».
      $('[data-a="best"]', el).onclick = () => {
        const s = new Set(settings.bestFriends || []), b = $('[data-a="best"]', el);
        if (s.has(f.name)) s.delete(f.name); else s.add(f.name);
        settings.bestFriends = [...s]; saveSettings(); play(s.has(f.name) ? "on" : "off");
        b.textContent = s.has(f.name) ? "★ Mejor amigo" : "☆ Mejores amigos"; b.classList.toggle("on", s.has(f.name));
        const nm = $(".fcard h2", el); if (nm) nm.textContent = f.name;
        toast(s.has(f.name) ? `${f.name} es de tus mejores amigos` : `${f.name} ya no está en mejores amigos`);
      };
      $('[data-a="opts"]', el).onclick = async () => {
        const v = await choiceDialog(f.name, [{ label: "Atrás", value: null }, { label: "Ver perfil en Steam", value: "steam" }, { label: "Enviar mensaje", value: "msg" }], 1);
        if (v === "steam") steamPage();
        if (v === "msg") $('[data-a="msg"]', el).click();
      };
    },
    items() { return fis(this.el); }
  };
}

