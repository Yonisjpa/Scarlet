/* ================= utilidades ================= */
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const esc = s => String(s).replace(/[&<>"']/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));
const xml = s => String(s).replace(/[&<>"']/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&apos;"}[c]));
const trunc = (s, n) => s.length > n ? s.slice(0, n - 1) + "…" : s;
const norm = s => String(s).normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();
const rem = () => parseFloat(getComputedStyle(document.documentElement).fontSize);
const wait = ms => new Promise(r => setTimeout(r, ms));
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const store = {
  get(k, d) { try { const v = localStorage.getItem("scarlet." + k); return v == null ? d : JSON.parse(v); } catch { return d; } },
  set(k, v) { try { localStorage.setItem("scarlet." + k, JSON.stringify(v)); } catch {} }
};
const settings = Object.assign({ theme: "dark", sound: true, rumble: true, sgdbKey: "" }, store.get("settings", {}));
const saveSettings = () => store.set("settings", settings);
let activePad = "ally", activeLayout = "";   // el mando que estás usando (ver «el mando que estás usando»)
/* "auto": el mismo tema que Windows (claro u oscuro), y cambia en cuanto Windows cambia. */
const darkMQ = matchMedia("(prefers-color-scheme: dark)");
const applyTheme = () => { document.documentElement.dataset.theme = settings.theme === "auto" ? (darkMQ.matches ? "dark" : "light") : settings.theme === "light" ? "light" : "dark"; };
darkMQ.addEventListener("change", () => { if (settings.theme === "auto") applyTheme(); });
document.documentElement.classList.toggle("showcursor", settings.hideCursor === false);
applyTheme();
applyGlass(false);

let toastT;
function toast(msg) { const t = $("#toast"); t.textContent = msg; t.classList.add("show"); clearTimeout(toastT); toastT = setTimeout(() => t.classList.remove("show"), 2300); }

