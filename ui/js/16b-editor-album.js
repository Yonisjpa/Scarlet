/* Una copia PNG conserva la resolución original. La vista previa usa el mismo lienzo que se guarda. */
function addAlbumImage(item) {
  if (!item?.path) return;
  albumData = [item, ...(albumData || []).filter(c => c.path !== item.path)];
}
async function saveAlbumImage(source, canvas, frame) {
  const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/png"));
  if (!blob) throw new Error("No se pudo preparar la imagen.");
  const url = await new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result);
    reader.onerror = () => reject(new Error("No se pudo leer la imagen."));
    reader.readAsDataURL(blob);
  });
  const item = Host.has ? await Host.call("album.savePng", { path: source.path, frame, png: url.slice(url.indexOf(",") + 1) })
    : { ...source, path: `captura_${Date.now()}.png`, url, thumb: url, type: "image" };
  if (!item?.path) throw new Error("No se pudo guardar la copia.");
  addAlbumImage(item);
}
function albumCaptionView(source) {
  const positions = ["Abajo", "Centro", "Arriba"], sizes = ["Pequeño","Mediano","Grande"];
  const colors = [["Blanco","#fff"],["Negro","#161619"],["Amarillo","#ffdb60"],["Azul","#92caff"]];
  return {
    cls: "caption-editor", text: "", position: 0, size: 1, color: 0, outline: true,
    hints: [["B","Volver"],["◀▶","Ajustar"],["A","Elegir"],["☰","Guardar copia"]],
    render(el) {
      el.innerHTML = head(I.album, "Añadir texto", source.game) +
        '<div class="caption-layout"><div class="caption-preview"><canvas hidden></canvas><p>Cargando imagen…</p></div><div class="caption-tools"></div></div>';
      const tools = $(".caption-tools", el);
      for (const label of ["Texto","Posición","Tamaño","Color","Contorno","Guardar copia"]) {
        const button = document.createElement("button"); button.className = "lrow fi";
        button.innerHTML = `<b>${label}</b><span class="val"></span>`;
        const i = tools.children.length; button.onclick = () => this.change(i, 1); tools.append(button);
      }
      this.canvas = $("canvas", el);
      this.img = new Image(); this.img.crossOrigin = "anonymous";
      this.img.onload = () => {
        this.canvas.width = this.img.naturalWidth; this.canvas.height = this.img.naturalHeight;
        this.canvas.hidden = false; $(".caption-preview p", el).hidden = true; this.paint();
      };
      this.img.onerror = () => { $(".caption-preview p", el).textContent = "No se pudo abrir la captura."; };
      this.img.src = source.url; this.paint();
    },
    change(i, d) {
      if (this.saving) return;
      const cycle = (n,len) => (n + d + len) % len;
      if (i === 0) return openView(keyboardView(this.text, t => { this.text = t.trim(); this.paint(); }, null, { title: "Texto de la captura", caps: true, max: 120 }));
      if (i === 1) this.position = cycle(this.position, positions.length);
      if (i === 2) this.size = cycle(this.size, sizes.length);
      if (i === 3) this.color = cycle(this.color, colors.length);
      if (i === 4) this.outline = !this.outline;
      if (i === 5) return this.save();
      this.paint();
    },
    paint() {
      const vals = [this.text || "Escribir…", positions[this.position], sizes[this.size], colors[this.color][0], this.outline ? "Sí" : "No", ""];
      $$(".val", this.el).forEach((e,i) => e.textContent = vals[i]);
      if (this.canvas.hidden) return;
      const canvas = this.canvas, ctx = canvas.getContext("2d"), w = canvas.width, h = canvas.height;
      ctx.drawImage(this.img, 0, 0, w, h);
      if (!this.text) return;
      const font = Math.max(1, Math.round(Math.min(w,h) * [.035,.055,.08][this.size]));
      ctx.font = `600 ${font}px "Segoe UI", sans-serif`; ctx.textAlign = "center"; ctx.textBaseline = "middle";
      ctx.fillStyle = colors[this.color][1]; ctx.strokeStyle = this.color === 1 ? "white" : "#18181c";
      ctx.lineJoin = "round"; ctx.lineWidth = Math.max(1, font * .12);
      const width = w * .89, lines = [];
      let line = "";
      for (const word of this.text.split(/\s+/)) {
        const next = line ? line + " " + word : word;
        if (line && ctx.measureText(next).width > width) { lines.push(line); line = word; } else line = next;
      }
      lines.push(line);
      const leading = font * 1.2, pad = h * .04 + font/2;
      const y = [h-pad-(lines.length-1)*leading, (h-(lines.length-1)*leading)/2, pad][this.position];
      lines.forEach((t,i) => {
        if (this.outline) ctx.strokeText(t,w/2,y+i*leading,width);
        ctx.fillText(t,w/2,y+i*leading,width);
      });
    },
    async save() {
      if (this.saving || this.canvas.hidden) return;
      if (!this.text) return this.change(0, 1);
      this.saving = true;
      try {
        await saveAlbumImage(source, this.canvas, false);
        if (topView() === this) closeView();
        toast("Copia con texto guardada");
      } catch (e) { toast(e.message || "No se pudo guardar la copia"); }
      finally { this.saving = false; }
    },
    onLR(i,d) { if (i > 0 && i < 5) this.change(i,d); },
    onKey(k) { if (k === "start") { this.save(); return true; } return false; },
    onB() { return !!this.saving; },
    items() { return fis(this.el); }
  };
}
