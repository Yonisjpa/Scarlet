/* ================= v2.9.8: capturas ================= */
let capT = 0, recTimer = 0;
function captureDown() {
  if (capT) return;
  capT = performance.now();
  recTimer = setTimeout(() => { if (mode === "game" && !state.overlay) { $("#recdot").classList.add("on"); play("recstart"); } }, 650);
}
function captureUp() {
  const held = performance.now() - capT; capT = 0; clearTimeout(recTimer); $("#recdot").classList.remove("on");
  if (mode !== "game" || state.overlay) return notify({ icon: I.album, title: "No se puede capturar aquí", text: "Las capturas se toman mientras juegas" });
  saveCapture(held >= 650 ? "video" : "image");
}
function saveCapture(type, hostItem) {
  const g = runningGame();
  if (type === "image") { const f = $("#flash"); f.classList.remove("go"); void f.offsetWidth; f.classList.add("go"); Snd.click(0, 5200, 2, .05, .5); Snd.click(.06, 3800, 2, .04, .35); }
  else play("select");
  const d = new Date(), months = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];
  const item = hostItem || { url: shotSvg(g || { appid: 7, name: "Captura" }, Math.floor(Math.random() * 50)), path: `captura_${Date.now()}.png`, game: g ? g.name : "Sistema", date: `${d.getDate()} ${months[d.getMonth()]} ${d.getFullYear()} · ${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`, type };
  if (!albumData) albumData = Host.has ? [] : mockAlbum();
  albumData.unshift(item);
  notifyCapture(item);
  if (!hostItem) Host.call("capture.take", { type }).catch(() => {});
}
Events.on("capture.saving", d => { if (d && d.path) notifyCapture(d, true); });
Events.on("capture.saved", d => {
  if (/ \(recorte\)/.test(d.path || "")) return;            // los recortes los agrega el editor, sin aviso de "últimos 30 s"
  if (albumData && albumData.some(x => x.path === d.path)) return;
  saveCapture(d.type || "image", d);
});

