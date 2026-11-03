/* ================= v2.9.8: noticias ================= */
const NEWS_T = [
  ["Actualización 1.4 ya disponible", "Llegan nuevas misiones secundarias, ajustes de equilibrio y mejoras de rendimiento en equipos portátiles.", "El parche reduce los tiempos de carga, corrige errores reportados por la comunidad y agrega opciones de accesibilidad. También mejora la estabilidad en modo ventana sin bordes."],
  ["Evento de temporada por tiempo limitado", "Recompensas exclusivas, un nuevo modo y desafíos semanales durante las próximas tres semanas.", "Inicia sesión durante el evento para conseguir objetos cosméticos y completa los desafíos para desbloquear un título especial en tu perfil."],
  ["Notas del parche: correcciones y rendimiento", "Corregimos fallos al reanudar tras suspender y mejoramos la compatibilidad con mandos.", "Esta versión soluciona el cierre inesperado al volver de la suspensión, mejora la detección de mandos DualSense y ajusta el escalado de la interfaz en pantallas de 7 pulgadas."],
  ["Nuevo contenido descargable anunciado", "Una nueva región, enemigos inéditos y una historia que continúa después del final.", "El contenido estará disponible más adelante este año. Quienes tengan el pase de temporada lo recibirán sin costo adicional."],
  ["Oferta de fin de semana", "Descuento especial en el juego base y sus expansiones solo hasta el lunes.", "Aprovecha la oferta en la Tienda de Steam. El descuento aplica también a los paquetes que incluyen todo el contenido adicional."]
];
function mockNews() {
  const pool = games.filter(g => g.kind !== "app" && (g.source || "steam") === "steam"), out = [];
  for (let i = 0; i < 9; i++) {
    const g = pool[(i * 3) % pool.length], t = NEWS_T[(i + g.appid) % NEWS_T.length];
    out.push({ appid: g.appid, game: g.name, title: t[0], summary: t[1], body: [t[1], t[2]], date: `${25 - i * 2} oct 2026`, url: "steam://store/" + g.appid });
  }
  return out;
}
let newsData = null;
function newsView() {
  return {
    hints: [["B", "Atrás"], ["A", "Leer"]],
    render(el) {
      el.innerHTML = head(I.news, "Noticias", "Cargando…") + `<div class="vbody"></div>`;
      (newsData ? Promise.resolve(newsData) : Host.call("steam.news").catch(() => mockNews())).then(list => {
        newsData = list; $(".vhead .sub", el).textContent = "De tus juegos";
        $(".vbody", el).innerHTML = list.map((n, i) => `<button class="ncard fi" data-i="${i}"><img alt=""><span class="nm"><span class="ng">${esc(n.game)} · ${esc(n.date)}</span><b>${esc(n.title)}</b><p>${esc(n.summary)}</p></span></button>`).join("");
        $$(".ncard", el).forEach(b => { const n = list[+b.dataset.i]; Art.apply($("img", b), byId(n.appid) || { appid: n.appid, name: n.game }, "hero"); b.onclick = () => openView(articleView(n)); });
        updateFocus();
      });
    },
    items() { return fis(this.el); }
  };
}
function articleView(n) {
  return {
    hints: [["B", "Atrás"], ["A", "Abrir en Steam"]],
    render(el) {
      el.innerHTML = head(I.news, n.game) + `<div class="vbody"><article class="article"><img class="ahero" alt=""><h1>${esc(n.title)}</h1><div class="ameta">${esc(n.game)} · ${esc(n.date)}</div>${(Array.isArray(n.body) && n.body.length ? n.body : [n.summary || ""]).map(p => `<p>${esc(p)}</p>`).join("")}<div class="btnrow"><button class="btn primary fi">Abrir en Steam</button></div></article></div>`;
      Art.apply($(".ahero", el), byId(n.appid) || { appid: n.appid, name: n.game }, "hero");
      $(".btn", el).onclick = () => openSystemApp({ ...SYS.news, name: n.title, url: n.url });
    },
    items() { return fis(this.el); }
  };
}

