/* --- álbum: filtros por tipo y juego, borrar --- */
const ATYPES = ["Todo", "Fotos", "Videos"];
const albumFilter = { type: 0, game: null };
const albumList = () => (albumData || []).filter(c => (albumFilter.type === 0 || (albumFilter.type === 1 ? c.type !== "video" : c.type === "video")) && (!albumFilter.game || c.game === albumFilter.game));
const albumPreviews = new Map();
let previewsInFlight = 0;
const previewQueue = [];
function pumpAlbumPreviews() {
  while (previewsInFlight < 3 && previewQueue.length) {
    const { tile, load } = previewQueue.pop();
    if (!tile.isConnected) continue;
    previewsInFlight++;
    load().finally(() => { previewsInFlight--; pumpAlbumPreviews(); });
  }
}
async function albumPreview(c) {
  if (!Host.has || !c.path || c.url.startsWith("data:") || (c.thumb && c.type !== "video")) return;
  let entry = albumPreviews.get(c.url);
  if (!entry) {
    entry = Host.call("album.preview", { path: c.path }).catch(() => null);
    albumPreviews.set(c.url, entry);
    if (albumPreviews.size > 2500) albumPreviews.delete(albumPreviews.keys().next().value);
  }
  const p = await entry;
  if (p) { c.thumb ||= p.Thumb; c.duration ||= p.Duration; c.fps ||= p.Fps; }
}
function albumView() {
  return {
    cols: 5, isAlbum: true,
    hints() { return this.sel ? [["B", "Cancelar"], ["A", "Marcar"], ["☰", `Enviar (${this.sel.size})`]] : [["B", "Atrás"], ["Y", "Tipo"], ["X", "Juego"], ["RB", "Actualizar"], ["☰", "Enviar"], ["A", "Ver"]]; },
    render(el) {
      el.innerHTML = head(I.album, "Álbum", "Cargando…") + `<div class="vbody"><div class="agrid"></div></div>`;
      this.grid = $(".agrid", el);
      this.scroller = this.grid.parentElement;
      this.grid.onclick = e => {
        const b = e.target.closest(".shot"); if (!b) return;
        const i = Number(b.dataset.i), c = this.list[i];
        if (!this.sel) return openView(viewerView(this, i));
        if (this.sel.has(c.path)) this.sel.delete(c.path); else this.sel.add(c.path);
        b.classList.toggle("on", this.sel.has(c.path)); play("select"); updateFocus();
      };
      this.scroller.addEventListener("scroll", () => this.windowRows(), { passive: true });
      this.resize = new ResizeObserver(() => this.windowRows(true));
      this.resize.observe(this.scroller);
      // Se ve al instante lo que ya se sabía y siempre se vuelve a leer la carpeta (antes se leía una vez por sesión).
      if (albumData) this.refresh();
      this.reload();
    },
    reload(manual) {
      if (manual) { const s = $(".vhead .sub", this.el); if (s) s.textContent = "Buscando capturas…"; }
      return Data.album().then(list => {
        const keep = this.list && this.list[this.i] && this.list[this.i].path;
        if (!views.includes(this)) return;
        albumData = list; this.refresh(keep);
        if (manual) toast(`Álbum actualizado · ${list.length} capturas`);
      });
    },
    refresh(keep) {
      if (!albumData) return;
      this.list = albumList();
      const index = keep ? this.list.findIndex(c => c.path === keep) : -1;
      this.i = index >= 0 ? index : clamp(this.i, 0, Math.max(0, this.list.length - 1));
      $(".vhead .sub", this.el).textContent = `${ATYPES[albumFilter.type]}${albumFilter.game ? " · " + albumFilter.game : ""} · ${this.list.length}`;
      this.grid.classList.toggle("sel", !!this.sel);
      this.windowRows(true, this.i);
      if (topView() === this) updateFocus();
    },
    windowRows(force = false, focusIndex) {
      if (!this.list) return;
      const g = this.grid, sc = this.scroller, css = getComputedStyle(g);
      const gap = parseFloat(css.rowGap), colGap = parseFloat(css.columnGap);
      if (!g.clientWidth) return;
      const stride = (g.clientWidth - colGap * (this.cols - 1)) / this.cols * 9 / 16 + gap;
      const rows = Math.ceil(this.list.length / this.cols);
      if (focusIndex != null) {
        const y = Math.floor(focusIndex / this.cols) * stride;
        if (y < sc.scrollTop) sc.scrollTop = y;
        else if (y + stride > sc.scrollTop + sc.clientHeight) sc.scrollTop = y + stride - sc.clientHeight;
      }
      const first = clamp(Math.floor(sc.scrollTop / stride) - 2, 0, Math.max(0, rows - 1));
      const end = Math.min(rows, first + Math.ceil(sc.clientHeight / stride) + 5);
      if (!force && first === this.first && end === this.end) return;
      this.first = first; this.end = end;
      g.style.paddingTop = first * stride + "px";
      g.style.paddingBottom = Math.max(0, rows - end) * stride + "px";
      if (!rows) { g.innerHTML = '<p class="empty">No hay capturas con este filtro.</p>'; return; }
      const group = document.createDocumentFragment();
      for (let i = first * this.cols; i < Math.min(this.list.length, end * this.cols); i++) {
        const c = this.list[i], video = c.type === "video";
        const b = document.createElement("button");
        b.className = "shot fi"; b.dataset.i = i; b.classList.toggle("on", !!this.sel?.has(c.path));
        b.classList.toggle("focused", topView() === this && this.i === i);
        b.innerHTML = `<span class="chk">✓</span><img alt="" decoding="async">${video ? '<span class="vid"></span>' : ""}<span class="cap">${esc(c.game)} · ${esc(c.date)}</span>`;
        const img = b.querySelector("img"), badge = b.querySelector(".vid");
        const paint = () => {
          const src = c.thumb || (!video || c.url.startsWith("data:") ? c.url : "");
          if (src) img.src = src;
          if (badge) badge.textContent = "▶ " + (c.duration ? mmss(c.duration) : "Video");
        };
        if (c.thumb || !Host.has || c.url.startsWith("data:")) paint();
        else if (badge) badge.textContent = "▶ Video";
        group.append(b);
        previewQueue.push({ tile: b, load: async () => { await albumPreview(c); if (b.isConnected) paint(); } });
      }
      g.replaceChildren(group);
      pumpAlbumPreviews();
    },
    focusItem() {
      if (!this.list?.length) return null;
      this.windowRows(false, this.i);
      return this.grid.querySelector(`[data-i="${this.i}"]`);
    },
    itemIndex(b) { return Number(b.dataset.i); },
    nav(dir) {
      if (!this.list?.length) return;
      let n = this.i;
      if (dir === "left" && n % this.cols) n--;
      if (dir === "right" && n % this.cols < this.cols - 1) n++;
      if (dir === "up" && n >= this.cols) n -= this.cols;
      if (dir === "down" && Math.floor(n / this.cols) < Math.floor((this.list.length-1) / this.cols)) n += this.cols;
      this.i = Math.min(n, this.list.length - 1); play("move"); updateFocus();
    },
    onReturn() { this.refresh(this.list?.[this.i]?.path); },
    dispose() { this.resize.disconnect(); },
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
  let playbackList = album.list;
  return {
    cls: "viewer", idx, zoom: 1, panX: 0, panY: 0,
    hints() {
      return this.vid
        ? [["B", "Atrás"], ["☰", "Más"], ["A", this.vid.paused ? "Reproducir" : "Pausa"], ["◀▶", this.vid.paused ? "Fotograma" : "±5 s"], ...(this.vid.paused ? [["Y", "Guardar fotograma"]] : []), ["LB", "Anterior"], ["RB", "Siguiente"]]
        : [["B", "Atrás"], ["☰", "Más"], ["LT RT", "Zoom"], ["A", "Encajar"], ["◀▶", this.zoom > 1 ? "Mover" : "Cambiar foto"]];
    },
    render(el) {
      el.innerHTML = `<div class="vwrap" style="touch-action:pan-y"><div class="media" style="display:contents"></div><div class="vcap"></div></div>`; this.show();
      el.addEventListener("pointermove", () => this.wake());
      el.addEventListener("pointerdown", () => this.wake());
      // (1.8) con el dedo: deslizar a los lados cambia de captura
      let sx = null, sy = 0;
      el.addEventListener("pointerdown", e => { if (e.pointerType !== "mouse" && !e.target.closest(".vctl") && this.zoom === 1) { sx = e.clientX; sy = e.clientY; } });
      el.addEventListener("pointerup", e => {
        if (sx == null) return; const dx = e.clientX - sx, dy = e.clientY - sy; sx = null;
        if (Math.abs(dx) > 80 && Math.abs(dx) > Math.abs(dy) * 1.5) { touchSwallowClick = Date.now() + 350; const d = dx < 0 ? 1 : -1; setTimeout(() => this.go(d), 30); }   // después del touchend: si no, el dedo se queda «colgado» de la imagen que se quitó
      });
    },
    show() {
      const list = playbackList, c = list[this.idx], m = $(".media", this.el);
      this.stop();
      this.zoom = 1; this.panX = this.panY = 0;
      album.i = this.idx;
      this.el.classList.remove("photo-idle");
      this.el.classList.toggle("photo-view", c.type !== "video");
      if (c.type === "video" && !c.url.startsWith("data:image/")) { albumPreview(c); this.player(m, c.url); }
      else {
        m.innerHTML = '<div class="photostage"><img alt="" crossorigin="anonymous"></div>';
        const img = m.querySelector("img");
        if (c.thumb) img.src = c.thumb;
        const full = new Image(); full.crossOrigin = "anonymous";
        full.onload = () => { img.src = full.src; };
        full.src = c.url;
        this.photo = full; this.wake();
      }
      $(".vcap", this.el).textContent = `${c.game} · ${c.date} · ${this.idx + 1} de ${list.length}${c.type === "video" ? " · video" : ""}`;
      updateFocus();
    },
    /* Reproductor propio: la barra se esconde sola mientras corre y vuelve con cualquier botón. */
    player(m, url) {
      const PLAY = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M7 4.5v15l12.5-7.5z"/></svg>', PAUSE = '<svg viewBox="0 0 24 24" fill="currentColor"><rect x="5.5" y="4.5" width="4.5" height="15" rx="1"/><rect x="14" y="4.5" width="4.5" height="15" rx="1"/></svg>';
      m.innerHTML = `<div class="vplayer paused wait"><video crossorigin="anonymous" src="${esc(url)}" autoplay playsinline preload="auto" disablepictureinpicture controlslist="nodownload nofullscreen noremoteplayback"></video>
        <div class="vbig play">${PLAY}</div><div class="vbig load"><i></i></div>
        <div class="vctl"><button class="frame-save">Guardar fotograma</button><button aria-label="Reproducir o pausar" class="pp">${PLAY}</button><span class="t cur">0:00</span><div class="vbar"><i class="buf"></i><i class="fill"></i><i class="knob"></i></div><span class="t r dur">0:00</span></div></div>`;
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
      $(".pp", box).onclick = () => this.toggle();
      $(".frame-save", box).onclick = () => this.frame();
      const scrub = e => {
        if (!Number.isFinite(v.duration)) return;
        const r = bar.getBoundingClientRect();
        v.currentTime = clamp((e.clientX - r.left) / r.width, 0, 1) * v.duration; this.wake();
      };
      bar.onpointerdown = e => { bar.setPointerCapture(e.pointerId); scrub(e); };
      bar.onpointermove = e => { if (bar.hasPointerCapture(e.pointerId)) scrub(e); };
      box.addEventListener("pointermove", () => this.wake());
      this.vid = v; this.box = box;
      v.play().catch(() => state());
      this.wake();
    },
    wake() {
      if (!this.box) {
        if (!this.photo) return;
        this.el.classList.remove("photo-idle"); clearTimeout(this.idleT);
        this.idleT = setTimeout(() => this.el.classList.add("photo-idle"), 2600);
        return;
      }
      this.box.classList.remove("idle"); clearTimeout(this.idleT);
      this.idleT = setTimeout(() => { if (this.vid && !this.vid.paused) this.box.classList.add("idle"); }, 2600);
    },
    toggle() { const v = this.vid; if (!v) return; if (v.paused || v.ended) { if (v.ended) v.currentTime = 0; v.play().catch(() => {}); play("select"); } else { v.pause(); play("tick"); } },
    seek(s) { const v = this.vid; if (!v || !v.duration) return play("bump"); v.currentTime = clamp(v.currentTime + s, 0, Math.max(0, v.duration - .00001)); play("tick"); this.wake(); },
    transformPhoto(step = 0, dx = 0, dy = 0) {
      const img = this.el.querySelector(".photostage img"); if (!img) return;
      this.zoom = clamp(this.zoom + step, 1, 4);
      this.panX = clamp(this.panX + dx, -img.clientWidth * (this.zoom - 1)/2, img.clientWidth * (this.zoom - 1)/2);
      this.panY = clamp(this.panY + dy, -img.clientHeight * (this.zoom - 1)/2, img.clientHeight * (this.zoom - 1)/2);
      img.style.transform = `translate(${this.panX}px,${this.panY}px) scale(${this.zoom})`; this.wake(); updateFocus();
    },
    async stepFrame(dir) {
      const v = this.vid;
      if (!v || this.stepping || !v.paused || v.readyState < 2) return;
      const interval = 1 / (playbackList[this.idx].fps || 30);
      if (!v.requestVideoFrameCallback || !Number.isFinite(v.duration)) return this.seek(dir * interval);
      const origin = v.currentTime, limit = Math.max(0, v.duration - .000001);
      this.stepping = v;
      // El tiempo de presentación identifica el cuadro incluso cuando dos cuadros tienen los mismos píxeles.
      const sample = at => new Promise(resolve => {
        if (v !== this.vid || !v.paused) return resolve(null);
        let handle;
        const timer = setTimeout(() => { v.cancelVideoFrameCallback(handle); resolve(null); }, 180);
        const presented = (_, metadata) => {
          if (v.seeking) { handle = v.requestVideoFrameCallback(presented); return; }
          clearTimeout(timer); resolve(metadata.mediaTime);
        };
        v.currentTime = at;
        handle = v.requestVideoFrameCallback(presented);
      });
      try {
        // Chromium no presenta un cuadro nuevo si se asigna exactamente el mismo currentTime.
        const base = await sample(origin < limit ? Math.min(limit, origin + .00001) : Math.max(0, origin - .00001));
        if (base == null) {
          if (v === this.vid && v.paused) v.currentTime = clamp(origin + dir * interval, 0, limit);
          return;
        }
        let near = origin, far = origin, distance = interval;
        let changed = false;
        while (v === this.vid && v.paused) {
          far = clamp(origin + dir * distance, 0, limit);
          if (far === near) break;
          const stamp = await sample(far);
          if (stamp == null) return;
          if ((stamp - base) * dir > .000001) { changed = true; break; }
          near = far; distance *= 2;
        }
        if (!changed) return;
        // Acota el primer cambio de cuadro desde la posición de partida (también con video de tasa variable).
        while (Math.abs(far - near) > .001) {
          const middle = (near + far) / 2, stamp = await sample(middle);
          if (stamp == null) return;
          if ((stamp - base) * dir > .000001) far = middle; else near = middle;
        }
        if (v === this.vid && v.paused) v.currentTime = far;
      } finally { if (this.stepping === v) this.stepping = null; }
      this.wake();
    },
    async frame() {
      const v = this.vid, c = playbackList[this.idx];
      if (!v || this.saving || this.stepping || !v.paused || v.seeking || v.readyState < 2) return play("bump");
      this.saving = true;
      try {
        const canvas = document.createElement("canvas");
        canvas.width = v.videoWidth; canvas.height = v.videoHeight;
        canvas.getContext("2d").drawImage(v, 0, 0);
        await saveAlbumImage(c, canvas, true);
        toast("Fotograma guardado en el Álbum");
      } catch (e) { toast(e.message || "No se pudo guardar el fotograma"); }
      finally { this.saving = false; }
    },
    dispose() { this.stop(); },
    stop() { this.stepping = null; this.photo = null; if (this.vid) { try { this.vid.pause(); this.vid.removeAttribute("src"); this.vid.load(); } catch {} } this.vid = this.box = null; clearTimeout(this.idleT); },
    go(d) {
      const n = this.idx + d;
      if (n < 0 || n >= playbackList.length) return bump($(".vwrap img, .vwrap .vplayer", this.el), d > 0 ? "right" : "left");
      this.idx = n; play("move"); this.show();
    },
    more() {
      const c = playbackList[this.idx]; if (!c) return;
      if (this.vid && !this.vid.paused) this.vid.pause();
      const opts = [{ label: "Enviar al celular (código QR)", value: "share" }, { label: "Copiar (para pegar en Discord, WhatsApp…)", value: "copy" }];
      if (c.type === "video") opts.push({ label: "Guardar fotograma", value: "frame" }, { label: "Recortar video", value: "trim" });
      else opts.push({ label: "Añadir texto", value: "caption" });
      opts.push({ label: c.type === "video" ? "Borrar grabación" : "Borrar captura", value: "del" });
      openView(pickerView("Opciones", I.album, opts, null, v => {
        if (v === "share") openView(shareView([c.path]));
        else if (v === "copy") Host.has ? Host.call("album.copy", { path: c.path }).then(ok => toast(ok ? "Copiada: pégala con Ctrl+V" : "No se pudo copiar")).catch(() => toast("No se pudo copiar")) : toast("Copiada (demo)");
        else if (v === "frame") this.frame();
        else if (v === "caption") openView(albumCaptionView(c));
        else if (v === "trim") openView(trimView(c, addAlbumImage));
        else if (v === "del") this.onX();
      }));
    },
    onKey(k) {
      this.wake();
      if (k === "y" && this.vid?.paused) { this.frame(); return true; }
      if (this.photo && ["lt","rt","a"].includes(k)) {
        this.transformPhoto(k === "rt" ? .5 : k === "lt" ? -.5 : 1 - this.zoom); return true;
      }
      if (k === "start") { this.more(); return true; }
      if (k === "a" && this.vid) { this.toggle(); return true; }
      if (k === "lb" || k === "rb") { this.go(k === "lb" ? -1 : 1); return true; }
      return false;
    },
    nav(d) {
      if (this.photo && this.zoom > 1) {
        this.transformPhoto(0, d === "left" ? 48 : d === "right" ? -48 : 0, d === "up" ? 48 : d === "down" ? -48 : 0); return;
      }
      if (d !== "left" && d !== "right") return false;
      if (this.vid) {
        const dir = d === "right" ? 1 : -1;
        if (this.vid.paused) this.stepFrame(dir); else this.seek(dir * 5);
      } else this.go(d === "right" ? 1 : -1);
    },
    onB() { const i = album.list.findIndex(c => c.path === playbackList[this.idx].path); if (i >= 0) album.i = i; return false; },
    onX() {
      const c = playbackList[this.idx];
      confirmDialog(`¿Borrar esta ${c.type === "video" ? "grabación" : "captura"}?\nNo se puede deshacer.`, "Borrar").then(async ok => {
        if (!ok) return;
        this.stop();   // suelta el archivo antes de borrarlo
        if (Host.has && !await Host.call("album.delete", { path: c.path }).catch(() => false)) {
          this.show(); toast("No se pudo borrar la captura"); return;
        }
        albumData = albumData.filter(x => x.path !== c.path); album.refresh(); playbackList = album.list;
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
      // (1.8) con el dedo: se arrastra el inicio o el fin (el más cercano a donde tocas)
      const line = $(".tline", el);
      line.addEventListener("pointerdown", e => {
        const d = v.duration || 0; if (!d) return;
        e.preventDefault(); line.setPointerCapture && line.setPointerCapture(e.pointerId);
        const at = ev => { const r = line.getBoundingClientRect(); return clamp((ev.clientX - r.left) / r.width, 0, 1) * d; };
        const t0 = at(e); this.h = Math.abs(t0 - this.a) <= Math.abs(t0 - this.b) ? 0 : 1;
        const put = ev => { const t = at(ev); if (this.h === 0) this.a = clamp(t, 0, Math.max(0, this.b - 1)); else this.b = clamp(t, Math.min(d, this.a + 1), d);
          v.pause(); this.preview = false; v.currentTime = this.h === 0 ? this.a : this.b; this.paint(); };
        put(e); play("tick");
        const up = () => { line.removeEventListener("pointermove", put); line.removeEventListener("pointerup", up); line.removeEventListener("pointercancel", up); updateFocus(); };
        line.addEventListener("pointermove", put); line.addEventListener("pointerup", up); line.addEventListener("pointercancel", up);
      });
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

