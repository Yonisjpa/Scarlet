"use strict";
/* Modo "overlay": la misma interfaz, pero solo el menú rápido, en una ventanita encima del juego (la abre la app). */
const OVERLAY = new URLSearchParams(location.search).get("overlay") === "quick";
if (OVERLAY) document.documentElement.classList.add("overlay");
/* el contorno gira con CSS; en navegadores sin @property se gira desde aquí (mismo ritmo: una vuelta cada 4 s) */
if (!(window.CSS && CSS.registerProperty)) {
  document.documentElement.classList.add("noprop");
  (function spin(t) {
    const a = (t / 4000 * 360) % 360 + "deg";
    document.querySelectorAll(".focused").forEach(e => e.style.setProperty("--ringa", a));
    requestAnimationFrame(spin);
  })(0);
}
const PADSRC = document.getElementById("padsrc").src, XPADSRC = document.getElementById("xpadsrc").src, PSPADSRC = document.getElementById("pspadsrc").src;
document.documentElement.style.setProperty("--padimg", `url("${PADSRC}")`);
document.querySelectorAll("#launch [data-src]").forEach(i => i.src = document.querySelector("#boot ." + i.dataset.src).src);
