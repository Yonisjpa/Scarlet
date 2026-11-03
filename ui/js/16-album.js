/* --- álbum: filtros por tipo y juego, borrar --- */
const ATYPES = ["Todo", "Fotos", "Videos"];
const albumFilter = { type: 0, game: null };
const albumList = () => (albumData || []).filter(c => (albumFilter.type === 0 || (albumFilter.type === 1 ? c.type !== "video" : c.type === "video")) && (!albumFilter.game || c.game === albumFilter.game));
function albumView() {
  return {
    cols: 5, isAlbum: true,
    hints() { return this.sel ? [["B", "Cancelar"], ["A", "Marcar"], ["☰", `Enviar (${this.sel.size})`]] : [["B", "Atrás"], ["Y", "Tipo"], ["X", "Juego"], ["RB", "Actualizar"], ["☰", "Enviar"], ["A", "Ver"]]; },
    render(el) {
      el.innerHTML = head(I.album, "Álbum", "Cargando…") + `<div class="vbody"><div class="agrid"></div></div>`;
      // Se ve al instante lo que ya se sabía y siempre se vuelve a leer la carpeta (antes se leía una vez por sesión).
      if (albumData) this.refresh();
      this.reload();
    },
    reload(manual) {
      if (manual) { const s = $(".vhead .sub", this.el); if (s) s.textContent = "Buscando capturas…"; }
      return Data.album().then(list => {
        const keep = this.list && this.list[this.i] && this.list[this.i].path;
        albumData = list; this.refresh();
        if (keep) { const j = this.list.findIndex(c => c.path === keep); if (j >= 0) { this.i = j; updateFocus(); } }
        if (manual) toast(`Álbum actualizado · ${list.length} capturas`);
      });
    },
    refresh() {
      if (!albumData) return;
      this.list = albumList();
      $(".vhead .sub", this.el).textContent = `${ATYPES[albumFilter.type]}${albumFilter.game ? " · " + albumFilter.game : ""} · ${this.list.length}`;
      const g = $(".agrid", this.el);
      g.classList.toggle("sel", !!this.sel);
      g.innerHTML = this.list.length ? this.list.map((c, i) => `<button class="shot fi${this.sel && this.sel.has(c.path) ? " on" : ""}" style="--k:${i % 15}" data-i="${i}"><span class="chk">✓</span>${c.type === "video" && !c.thumb && !c.url.startsWith("data:") ? `<video data-src="${esc(c.url)}#t=0.5" muted preload="metadata" style="width:100%;height:100%;object-fit:cover;border-radius:inherit;display:block"></video>` : `<img alt="" loading="lazy" decoding="async" src="${esc(c.thumb || c.url)}">`}${c.type === "video" ? '<span class="vid">▶ Video</span>' : ""}<span class="cap">${esc(c.game)} · ${esc(c.date)}</span></button>`).join("") : `<p class="empty" style="grid-column:1/-1">No hay capturas con este filtro.</p>`;
      // Los videos cargan su primer cuadro solo cuando se ven (con cientos de capturas ya no se piden todos de golpe).
      const vids = $$("video[data-src]", g);
      if (vids.length) {
        const io = new IntersectionObserver(es => es.forEach(e => { if (e.isIntersecting) { e.target.src = e.target.dataset.src; e.target.removeAttribute("data-src"); io.unobserve(e.target); } }), { rootMargin: "400px" });
        vids.forEach(v => io.observe(v));
      }
      fis(this.el).forEach(b => b.onclick = () => {
        if (!this.sel) return openView(viewerView(this, +b.dataset.i));
        const c = this.list[+b.dataset.i]; if (!c || c.url.startsWith("data:") && Host.has) return play("bump");
        if (this.sel.has(c.path)) this.sel.delete(c.path); else this.sel.add(c.path);
        b.classList.toggle("on", this.sel.has(c.path)); play("select"); updateFocus();
      });
      this.i = clamp(this.i, 0, Math.max(0, this.list.length - 1)); updateFocus();
    },
    onKey(k) {
      if (k === "start") {   // ☰: elegir varias y mandarlas al celular
        if (!this.list || !this.list.length) return play("bump"), true;
        if (!this.sel) { this.sel = new Set([this.list[this.i].path]); play("select"); toast("Marca con A lo que quieras enviar y presiona ☰"); }
        else if (!this.sel.size) { play("bump"); return true; }
        else { const paths = [...this.sel]; this.sel = null; openView(shareView(paths)); }
        this.refresh(); this.i = clamp(this.i, 0, this.list.length - 1); updateFocus(); return true;
      }
      if (this.sel) return false;
      if (k === "y") { albumFilter.type = (albumFilter.type + 1) % ATYPES.length; play("select"); this.i = 0; this.refresh(); return true; }
      if (k === "rb") { play("select"); this.reload(true); return true; }
      return false;
    },
    onB() { if (!this.sel) return false; this.sel = null; play("back"); const i = this.i; this.refresh(); this.i = i; updateFocus(); return true; },
    onX() {
      const names = [...new Set((albumData || []).map(c => c.game))];
      const opts = [{ label: "Todos los juegos", value: null }, ...names.map(n => ({ label: n, value: n, extra: (albumData || []).filter(c => c.game === n).length + "" }))];
      openView(pickerView("Filtrar por juego", I.album, opts, albumFilter.game, v => { albumFilter.game = v; this.i = 0; this.refresh(); }));
      return true;
    },
    items() { return fis(this.el); }
  };
}
function viewerView(album, idx) {
  return {
    cls: "viewer", idx,
    hints() {
      return this.vid
        ? [["B", "Atrás"], ["☰", "Más"], ["A", this.vid.paused ? "Reproducir" : "Pausa"], ["◀", "−5 s"], ["▶", "+5 s"], ["LB", "Anterior"], ["RB", "Siguiente"]]
        : [["B", "Atrás"], ["☰", "Más"], ["X", "Borrar"], ["◀", "Anterior"], ["▶", "Siguiente"]];
    },
    render(el) { el.innerHTML = `<div class="vwrap"><div class="media" style="display:contents"></div><div class="vcap"></div></div>`; this.show(); },
    show() {
      const list = album.list, c = list[this.idx], m = $(".media", this.el);
      this.stop();
      if (c.type === "video" && !c.url.startsWith("data:")) this.player(m, c.url);
      else m.innerHTML = `<img alt="" src="${esc(c.url)}">`;
      $(".vcap", this.el).textContent = `${c.game} · ${c.date} · ${this.idx + 1} de ${list.length}${c.type === "video" ? " · video" : ""}`;
      updateFocus();
    },
    /* Reproductor propio: la barra se esconde sola mientras corre y vuelve con cualquier botón. */
    player(m, url) {
      const PLAY = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M7 4.5v15l12.5-7.5z"/></svg>', PAUSE = '<svg viewBox="0 0 24 24" fill="currentColor"><rect x="5.5" y="4.5" width="4.5" height="15" rx="1"/><rect x="14" y="4.5" width="4.5" height="15" rx="1"/></svg>';
      m.innerHTML = `<div class="vplayer paused wait"><video src="${esc(url)}" autoplay playsinline preload="auto" disablepictureinpicture controlslist="nodownload nofullscreen noremoteplayback"></video>
        <div class="vbig play">${PLAY}</div><div class="vbig load"><i></i></div>
        <div class="vctl"><span class="pp">${PLAY}</span><span class="t cur">0:00</span><div class="vbar"><i class="buf"></i><i class="fill"></i><i class="knob"></i></div><span class="t r dur">0:00</span></div></div>`;
      const box = $(".vplayer", m), v = $("video", box), bar = $(".vbar", box);
      const fmt = s => !isFinite(s) ? "0:00" : `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, "0")}`;
      const paint = () => {
        const d = v.duration || 0, p = d ? v.currentTime / d * 100 : 0;
        $(".fill", bar).style.width = p + "%"; $(".knob", bar).style.left = p + "%";
        if (d && v.buffered.length) $(".buf", bar).style.width = v.buffered.end(v.buffered.length - 1) / d * 100 + "%";
        $(".cur", box).textContent = fmt(v.currentTime); $(".dur", box).textContent = fmt(d);
      };
      const state = () => { box.classList.toggle("paused", v.paused); $(".pp", box).innerHTML = v.paused ? PLAY : PAUSE; this.wake(); updateFocus(); };
      v.addEventListener("timeupdate", paint); v.addEventListener("progress", paint); v.addEventListener("loadedmetadata", paint);
      v.addEventListener("play", state); v.addEventListener("pause", state); v.addEventListener("ended", state);
      v.addEventListener("waiting", () => box.classList.add("wait"));
      ["playing", "canplay", "seeked"].forEach(e => v.addEventListener(e, () => box.classList.remove("wait")));
      v.addEventListener("error", () => { box.classList.remove("wait"); box.insertAdjacentHTML("beforeend", `<p class="verr">No se pudo reproducir este video.<br><small>Puede que el formato no sea compatible (H.264 / VP9 sí lo son).</small></p>`); });
      v.addEventListener("click", () => this.toggle());
      bar.addEventListener("click", e => { const r = bar.getBoundingClientRect(); if (v.duration) v.currentTime = clamp((e.clientX - r.left) / r.width, 0, 1) * v.duration; this.wake(); });
      box.addEventListener("pointermove", () => this.wake());
      this.vid = v; this.box = box;
      v.play().catch(() => state());
      this.wake();
    },
    wake() {
      if (!this.box) return;
      this.box.classList.remove("idle"); clearTimeout(this.idleT);
      this.idleT = setTimeout(() => { if (this.vid && !this.vid.paused) this.box.classList.add("idle"); }, 2600);
    },
    toggle() { const v = this.vid; if (!v) return; if (v.paused || v.ended) { if (v.ended) v.currentTime = 0; v.play().catch(() => {}); play("select"); } else { v.pause(); play("tick"); } },
    seek(s) { const v = this.vid; if (!v || !v.duration) return play("bump"); v.currentTime = clamp(v.currentTime + s, 0, v.duration - 0.05); play("tick"); this.wake(); },
    stop() { if (this.vid) { try { this.vid.pause(); this.vid.removeAttribute("src"); this.vid.load(); } catch {} } this.vid = this.box = null; clearTimeout(this.idleT); },
    go(d) {
      const n = this.idx + d;
      if (n < 0 || n >= album.list.length) return bump($(".vwrap img, .vwrap .vplayer", this.el), d > 0 ? "right" : "left");
      this.idx = n; play("move"); this.show();
    },
    more() {
      const c = album.list[this.idx]; if (!c) return;
      if (this.vid && !this.vid.paused) this.vid.pause();
      const opts = [{ label: "Enviar al celular (código QR)", value: "share" }, { label: "Copiar (para pegar en Discord, WhatsApp…)", value: "copy" }];
      if (c.type === "video") opts.push({ label: "Recortar video", value: "trim" });
      opts.push({ label: c.type === "video" ? "Borrar grabación" : "Borrar captura", value: "del" });
      openView(pickerView("Opciones", I.album, opts, null, v => {
        if (v === "share") openView(shareView([c.path]));
        else if (v === "copy") Host.has ? Host.call("album.copy", { path: c.path }).then(ok => toast(ok ? "Copiada: pégala con Ctrl+V" : "No se pudo copiar")).catch(() => toast("No se pudo copiar")) : toast("Copiada (demo)");
        else if (v === "trim") { this.stop(); openView(trimView(c, item => { albumData.unshift(item); album.refresh(); this.idx = Math.max(0, album.list.indexOf(c)); this.show(); })); }
        else if (v === "del") this.onX();
      }));
    },
    onKey(k) {
      if (this.vid) this.wake();
      if (k === "start") { this.more(); return true; }
      if (k === "a" && this.vid) { this.toggle(); return true; }
      if (k === "lb" || k === "rb") { this.go(k === "lb" ? -1 : 1); return true; }
      return false;
    },
    nav(d) {
      if (d !== "left" && d !== "right") return false;
      if (this.vid) this.seek(d === "right" ? 5 : -5); else this.go(d === "right" ? 1 : -1);
    },
    onB() { this.stop(); return false; },
    onX() {
      const c = album.list[this.idx];
      confirmDialog(`¿Borrar esta ${c.type === "video" ? "grabación" : "captura"}?\nNo se puede deshacer.`, "Borrar").then(ok => {
        if (!ok) return;
        this.stop();   // suelta el archivo antes de borrarlo
        Host.call("album.delete", { path: c.path }).catch(() => {});
        albumData = albumData.filter(x => x !== c); album.refresh();
        if (!album.list.length) return closeView();
        this.idx = Math.min(this.idx, album.list.length - 1); this.show(); toast("Se borró la captura");
      });
      return true;
    },
    items() { return []; }
  };
}

/* Enviar al celular: código QR con una página en tu red Wi-Fi (como la Switch 2). */
function shareView(paths) {
  return {
    hints: [["B", "Terminar"]],
    render(el) {
      el.innerHTML = head(I.album, "Enviar al celular", `${paths.length} archivo${paths.length === 1 ? "" : "s"}`) + `<div class="qrbox"><div class="qr"></div><div><ol>
        <li>Conecta tu iPhone a la misma red Wi-Fi<span class="ssid"></span>.</li><li>Abre la cámara y apunta al código.</li><li>Toca el aviso que aparece: se abre una página con tus capturas.</li><li>En fotos, mantén presionada la imagen › «Guardar en Fotos». En videos, «Descargar».</li></ol>
        <p class="url">Preparando…</p><p class="left"></p></div></div>`;
      if (!Host.has) { $(".url", el).textContent = "En la consola aquí aparece el código QR."; return; }
      Host.call("album.share", { paths }).then(r => {
        if (!r || r.error) { $(".url", el).textContent = (r && r.error) || "No se pudo compartir."; return; }
        $(".qr", el).innerHTML = `<img alt="Código QR" src="${r.qr}">`;
        $(".url", el).textContent = r.url;
        if (r.ssid) $(".ssid", el).textContent = ` (${r.ssid})`;
        const until = Date.now() + r.minutes * 6e4;
        const t = () => { const s = Math.max(0, Math.round((until - Date.now()) / 1000)); const l = $(".left", el); if (l) l.textContent = s ? `El enlace caduca en ${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}` : "El enlace caducó."; };
        t(); this.timer = setInterval(t, 1000);
        play("popup");
      }).catch(() => { $(".url", el).textContent = "No se pudo compartir."; });
    },
    onB() { clearInterval(this.timer); if (Host.has) Host.call("album.shareStop").catch(() => {}); return false; },
    items() { return []; }
  };
}

/* Recortar video: dos marcas (inicio y fin) sobre la línea de tiempo; se guarda como un video nuevo. */
function trimView(c, onSaved) {
  return {
    hints() { return [["B", "Cancelar"], ["A", this.h ? "Mover inicio" : "Mover fin"], ["◀▶", "±1 s"], ["LB RB", "±5 s"], ["Y", "Ver recorte"], ["☰", "Guardar"]]; },
    render(el) {
      el.innerHTML = head(I.album, "Recortar video", c.game) + `<div class="trim"><video src="${esc(c.url)}" playsinline preload="auto"></video>
        <div class="tline"><i class="sel"></i><i class="ph"></i><i class="h a on"></i><i class="h b"></i></div>
        <div class="tinfo"><span>Inicio <b class="ta">0:00</b></span><span>Duración <b class="td">0:00</b></span><span>Fin <b class="tb">0:00</b></span></div></div>`;
      const v = $("video", el);
      this.v = v; this.a = 0; this.b = 0; this.h = 0;
      v.addEventListener("loadedmetadata", () => { this.b = v.duration || 0; this.paint(); });
      v.addEventListener("timeupdate", () => { if (this.preview && v.currentTime >= this.b) { v.pause(); this.preview = false; } this.paint(); });
    },
    paint() {
      const d = this.v.duration || 1, el = this.el, pa = this.a / d * 100, pb = this.b / d * 100;
      $(".sel", el).style.cssText = `left:${pa}%;width:${pb - pa}%`;
      $(".h.a", el).style.left = pa + "%"; $(".h.b", el).style.left = pb + "%";
      $(".h.a", el).classList.toggle("on", this.h === 0); $(".h.b", el).classList.toggle("on", this.h === 1);
      $(".ph", el).style.left = this.v.currentTime / d * 100 + "%";
      $(".ta", el).textContent = mmss(this.a); $(".tb", el).textContent = mmss(this.b); $(".td", el).textContent = mmss(this.b - this.a);
    },
    move(s) {
      const d = this.v.duration || 0; if (!d) return play("bump");
      if (this.h === 0) this.a = clamp(this.a + s, 0, Math.max(0, this.b - 1)); else this.b = clamp(this.b + s, Math.min(d, this.a + 1), d);
      this.v.pause(); this.preview = false; this.v.currentTime = this.h === 0 ? this.a : this.b;
      play("tick"); this.paint();
    },
    nav(d) { if (d === "left" || d === "right") { this.move(d === "right" ? 1 : -1); return; } return false; },
    onKey(k) {
      if (k === "a") { this.h = 1 - this.h; play("move"); this.v.currentTime = this.h ? this.b : this.a; this.paint(); updateFocus(); return true; }
      if (k === "lb" || k === "rb") { this.move(k === "rb" ? 5 : -5); return true; }
      if (k === "y") { this.v.currentTime = this.a; this.preview = true; this.v.play().catch(() => {}); play("select"); return true; }
      if (k === "start") { this.save(); return true; }
      return false;
    },
    async save() {
      if (this.saving) return;
      if (!Host.has) { toast("En la consola aquí se guarda el recorte"); return; }
      this.saving = true; this.v.pause(); toast("Guardando recorte…");
      const r = await Host.call("album.trim", { path: c.path, start: this.a, end: this.b }).catch(() => null);
      this.saving = false;
      if (!r || !r.ok) { play("bump"); return toast((r && r.error) || "No se pudo recortar"); }
      play("select"); toast("Recorte guardado en el Álbum"); closeView(); onSaved && onSaved(r.item);
    },
    onB() { this.v.pause(); return false; },
    items() { return []; }
  };
}

