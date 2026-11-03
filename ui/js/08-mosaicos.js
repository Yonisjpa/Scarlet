/* ================= piezas de los mosaicos ================= */
function tileExtras(g) {
  let h = "";
  if (isMusicTile(g)) { const on = !!(media && media.playing && norm(media.app || "") === norm(g.name)); h += `<span class="playing music${on ? "" : " paused"}"><span class="pav">${avatarHTML()}</span><b><i class="eq"><i></i><i></i><i></i></i>${on ? "Sonando" : "En pausa"}</b></span>`; }
  else if (g.appid === state.running || g.appid === state.side) h += `<span class="playing"><span class="pav">${avatarHTML()}</span><b>En uso</b></span>`;
  const d = state.downloads[g.appid];
  if (d) h += `<div class="dl${d.st === "paused" ? " paused" : d.st === "queued" ? " queued" : ""}" data-dl="${g.appid}" aria-label="${esc(dlLabel(d))}"><b><i style="width:${d.p * 100}%"></i></b></div>`;
  else if (state.cloud[g.appid]) { const c = state.cloud[g.appid]; h += `<div class="dl cloudsync${c.p < 0 ? " indet" : ""}" data-cl="${g.appid}" aria-label="Sincronizando con la nube${c.p >= 0 ? " " + Math.round(c.p) + " %" : ""}"><b><i style="width:${c.p < 0 ? 35 : c.p}%"></i></b></div>`; }
  return h;
}
/* ¿Este mosaico es la app que está sonando (Apple Music, Spotify…)? */
/* Apps de música (Apple Music, Spotify…) dicen "Sonando" / "En pausa" aunque sean la app abierta; YouTube, Netflix,
   Prime Video y demás siguen con "Jugando". */
const MUSIC_APP = /apple music|spotify|itunes|amazon music|deezer|tidal|youtube music|soundcloud/i;
const isMusicTile = g => g.kind === "app" && (media && media.app && norm(g.name) === norm(media.app) || ((g.appid === state.running || g.appid === state.side) && MUSIC_APP.test(g.name)));
function buildTile(g, cls = "tile") {
  const b = document.createElement("button");
  b.className = cls + (g.installed || state.downloads[g.appid] ? "" : " uninst"); b.setAttribute("aria-label", g.name);
  const img = document.createElement("img"); img.alt = ""; b.append(img); Art.apply(img, g);
  b.insertAdjacentHTML("beforeend", tileExtras(g));
  return b;
}
/* imagen real del logro (Steam); si no hay o no carga, el trofeo / candado de siempre */
function achImg(x, cls = "") {
  const u = x.unlocked ? x.icon : (x.iconGray || x.icon);
  if (!u) return x.unlocked ? I.trophy : I.lock;
  const fb = esc(x.unlocked ? I.trophy : I.lock).replace(/'/g, "&#39;");
  return `<img class="${cls}" alt="" loading="lazy" src="${esc(u)}" onerror="this.outerHTML=this.dataset.fb" data-fb="${fb}">`;
}
function avatarFrame() { return profile.frameUrl ? `<img class="aframe" alt="" src="${esc(profile.frameUrl)}">` : ""; }
function avatarHTML() {
  const u = profile.avatarUrl; if (!u) return esc((profile.name || "?")[0].toUpperCase());
  return /\.(webm|mp4)(\?|&|$)/i.test(decodeURIComponent(u)) ? `<video src="${esc(u)}" autoplay loop muted playsinline style="width:100%;height:100%;border-radius:50%;object-fit:cover"></video>` : `<img alt="" src="${esc(u)}">`;
}

