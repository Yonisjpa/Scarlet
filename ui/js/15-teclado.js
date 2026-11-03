/* --- teclado en pantalla --- */
/* opt: { title, placeholder, max, keys: "text" | "digits" | "hexUpper" | "hexLower", paste } */
function keyboardView(initial, onDone, countFor, opt = {}) {
  // caps: mayúsculas (nombres de juegos): tecla Mayús y la primera letra de cada palabra en mayúscula.
  const tail = opt.caps ? ["Mayús", "Espacio", ...(opt.paste ? ["Pegar"] : []), "Borrar", "Listo"] : opt.paste ? ["Pegar", "Borrar", "Listo"] : ["Espacio", "Borrar", "Listo"];
  const rows = opt.keys === "digits" ? [[..."1234567890"], tail]
    : opt.keys === "hexUpper" ? [[..."1234567890"], [..."ABCDEF"], tail]
    : opt.keys === "hexLower" ? [[..."1234567890"], [..."abcdef"], tail]
    : [[..."1234567890"], [..."qwertyuiop"], [..."asdfghjklñ"], [..."zxcvbnm-'."], tail];
  const last = rows.length - 1, max = opt.max || 40;
  const flatIndex = (r, c) => rows.slice(0, r).reduce((s, x) => s + x.length, 0) + c;
  return {
    text: initial || "", r: rows.length > 2 ? 1 : 0, c: 0, i: flatIndex(rows.length > 2 ? 1 : 0, 0),
    hints: [["B", "Borrar"], ["Y", opt.paste ? "Pegar" : "Espacio"], ["☰", "Listo"], ["A", "Escribir"]],
    render(el) {
      el.innerHTML = head(opt.title ? I.gear : I.search, opt.title || "Buscar en la biblioteca") + `<div class="kb"><div class="kbfield"><span class="kbtext"></span><i class="caret"></i></div><div class="kbcount"></div><div class="kbrows">${rows.map((row, ri) => `<div class="kbrow">${row.map((k, ci) => `<button class="key fi${ri === last ? " wide" : ""}${k === "Listo" ? " ok" : ""}" data-r="${ri}" data-c="${ci}">${esc(k)}</button>`).join("")}</div>`).join("")}</div></div>`;
      fis(el).forEach(b => b.onclick = () => { this.r = +b.dataset.r; this.c = +b.dataset.c; this.press(); });
      this.upd();
    },
    syncFromIndex() { let n = this.i; for (let r = 0; r < rows.length; r++) { if (n < rows[r].length) { this.r = r; this.c = n; return; } n -= rows[r].length; } },
    upd() {
      const t = $(".kbtext", this.el);
      if (opt.caps) { const up = this.shift || this.autoCap(); $$(".key", this.el).forEach(b => { if (b.textContent.length === 1) b.style.textTransform = up ? "uppercase" : ""; }); const sk = $$(".key", this.el).find(b => b.textContent === "Mayús"); if (sk) sk.classList.toggle("on", !!this.shift); }
      t.innerHTML = this.text ? esc(this.text) : `<span class="ph">${esc(opt.placeholder || "Nombre del juego…")}</span>`;
      if (countFor) { const n = countFor(this.text); $(".kbcount", this.el).textContent = this.text ? `${n} resultado${n === 1 ? "" : "s"}` : ""; }
      else $(".kbcount", this.el).textContent = opt.max ? `${this.text.length} / ${opt.max}` : "";
    },
    press() {
      const k = rows[this.r][this.c];
      if (k === "Mayús") { this.shift = !this.shift; play("tick"); return this.upd(); }
      if (k === "Espacio") this.type(" ");
      else if (k === "Pegar") this.paste();
      else if (k === "Borrar") this.del();
      else if (k === "Listo") this.done();
      else this.type(k);
    },
    type(ch) {
      if (this.text.length >= max) return play("bump");
      if (opt.caps && ch.length === 1 && /\p{L}/u.test(ch) && (this.shift || this.autoCap())) { ch = ch.toUpperCase(); this.shift = false; }
      this.text += ch; play("type"); this.upd();
    },
    autoCap() { return !opt.paste && (!this.text || this.text.endsWith(" ")); },
    async paste() {
      const t = Host.has ? await Host.call("clipboard.get").catch(() => "") : await navigator.clipboard?.readText?.().catch(() => "");
      const clean = String(t || "").trim().slice(0, max);
      if (!clean) { toast("No hay texto copiado"); return play("bump"); }
      this.text = clean; play("select"); this.upd();
    },
    del() { if (!this.text) return play("bump"); this.text = this.text.slice(0, -1); play("back"); this.upd(); },
    done() { closeView(); onDone(this.text); },
    nav(dir) {
      let r = this.r, c = this.c;
      if (dir === "left") c--; else if (dir === "right") c++;
      else {
        const nr = r + (dir === "down" ? 1 : -1);
        if (nr < 0 || nr >= rows.length) return bump(this.items()[this.i], dir);
        c = Math.round(c * (rows[nr].length - 1) / Math.max(1, rows[r].length - 1)); r = nr;
      }
      if (c < 0 || c >= rows[r].length) return bump(this.items()[this.i], dir);
      this.r = r; this.c = c; this.i = flatIndex(r, c); play("move"); updateFocus();
    },
    onKey(k) {
      if (k === "x") { this.del(); return true; }
      if (k === "y") { opt.paste ? this.paste() : this.type(" "); return true; }
      if (k === "start") { this.done(); return true; }
      return false;
    },
    onB() { if (!this.text) return false; this.del(); return true; },
    onType(ch) {
      if (opt.keys === "digits" && !/\d/.test(ch)) return play("bump");
      if (/^hex/.test(opt.keys || "") && !/[0-9a-f]/i.test(ch)) return play("bump");
      ch = opt.keys === "hexUpper" ? ch.toUpperCase() : opt.caps ? ch : ch.toLowerCase();
      if (opt.caps && ch !== ch.toLowerCase()) { this.text.length < max && (this.text += ch, play("type"), this.upd()); return; }
      this.type(ch);
    },
    items() { return fis(this.el); }
  };
}
