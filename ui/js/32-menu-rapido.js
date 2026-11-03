/* ================= beta 1.0: menú rápido como en la Switch 2 =================
   Una sola lista de arriba a abajo (cruceta ▲▼), y ◀▶ ajustan las barras o eligen el botón de la canción. Lo que habla con
   Windows es lo mismo de antes (applySys, setRadio, vpnSet, mixer.set, media.cmd, quick.live): solo cambia cómo se ve y
   cómo se navega. El menú de antes sigue disponible en Ajustes › Sistema › Menú rápido clásico. */
const qmClassic = () => !!settings.quickClassic;
const qmRows = () => qmClassic() ? qmRowsClassic() : qmRowsNS();
function renderQuick() { return qmClassic() ? renderQuickClassic() : renderQuickNS(); }
function qmInput(k) { return qmClassic() ? qmInputClassic(k) : qmInputNS(k); }
const NQ = {
  moon: '<svg class="qi" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 14.5A8 8 0 1 1 9.5 4a6.5 6.5 0 0 0 10.5 10.5z"/></svg>',
  wifi: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><path d="M2.5 9a14 14 0 0 1 19 0M5.8 12.6a9 9 0 0 1 12.4 0M9.2 16.1a4 4 0 0 1 5.6 0"/><circle cx="12" cy="19.4" r="1.3" fill="currentColor"/></svg>',
  note: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M9 17.5a3 3 0 1 1-2-2.83V5l11-2v12.5a3 3 0 1 1-2-2.83V7.2l-7 1.3z"/></svg>'
};
const qmRowsNS = () => [
  ...(media ? [{ k: "media", type: "media" }] : []),
  { k: "hibernate", type: "act", icon: NQ.moon, label: "Hibernar" },
  ...(QM.autoBriOk ? [{ k: "autobri", type: "val", label: "Brillo automático", help: "Ajusta el brillo según la luz del lugar." }] : []),
  { k: "brightness", type: "slider" },
  { k: "volume", type: "slider" },
  ...(QM.mix || []).map(m => ({ k: "mix:" + m.exe, type: "mix", m })),
  { k: "airplane", type: "val", label: "Modo avión", help: "Con el modo avión no puedes usar internet, mandos inalámbricos ni Bluetooth." },
  { k: "wifi", type: "val", label: "Wi-Fi" },
  ...(VPN.provider !== "none" ? [{ k: "vpn", type: "val", label: "VPN" }] : [])
  // beta 1.3: sin «Apagar…» ni «Ajustes» (están en el Inicio, en los íconos de abajo)
];
/* Valor que se ve a la derecha de cada fila (en color cuando está prendido). */
function nqVal(r) {
  if (r.k === "autobri") return [sys.autoBri ? "Sí" : "No", !!sys.autoBri];
  if (r.k === "airplane") return [sys.airplane ? "Sí" : "No", !!sys.airplane];
  if (r.k === "wifi") { if (!sys.wifi) return ["Apagado", false]; return [netOn() ? (QM.ssid || "Conectado") : navigator.onLine ? "Sin internet" : "Encendido", netOn()]; }
  if (r.k === "vpn") return [VPN.busy ? "…" : vpnLabel(), vpnOn() && !VPN.busy];
  return ["", false];
}
/* Barra con su bolita; la burbuja con el número solo se ve en la barra elegida. */
const nqBar = (v, off) => `<span class="qbar nqbar"><i style="width:${off ? 0 : v}%"></i><u style="left:${off ? 0 : v}%"><em>${off ? "—" : v}</em></u></span>`;
function nqMedia(f) {
  const m = media, p = mediaPos(), pct = m.duration ? p / m.duration * 100 : 0;
  const sub = [m.artist || m.album || "", m.app || ""].filter(Boolean).join(" · ");
  const btns = mediaButtons().map((b, j) => j === 0 ? "" : `<span data-m="${j}" class="qmb${b.ok ? "" : " off"}${QM.m === j ? " sel" : ""}">${b.icon}</span>`).join("");
  return `<div class="qrow nqmu${f}"><span class="qmart nqart" style="${m.art ? `background-image:url('${esc(m.art)}')` : ""}">${m.art ? "" : NQ.note}</span>
    <span class="nqmtx"><b>${esc(m.title)}</b><span>${f && m.duration ? `<span class="mcur">${mmss(p)}</span> / ${mmss(m.duration)} · ` : ""}${esc(sub)}</span></span>
    <span class="nqctl">${btns}</span>${m.duration ? `<span class="qmprog nqline"><span class="qbar"><i style="width:${pct}%"></i></span></span>` : ""}</div>`;
}
function renderQuickNS() {
  const rows = qmRows(), q = $("#quick");
  q.classList.add("ns"); q.classList.remove("dense", "dense2");
  const list = $(".nqlist", q), keep = list ? list.scrollTop : 0;
  const row = (r, i) => {
    const f = QM.i === i ? " focused" : "", at = `data-qi="${i}"`;
    if (r.type === "media") return nqMedia(f).replace('<div class="qrow', `<div ${at} class="qrow`);
    if (r.type === "slider") {
      const off = r.k === "brightness" && QM.noBri, auto = r.k === "brightness" && sys.autoBri, v = sys[r.k], ic = r.k === "volume" ? volIcon(v) : sunIcon(off ? 50 : v);
      return `<div ${at} class="qrow nqsl${f}${off || auto ? " qoff" : ""}" aria-label="${r.k === "volume" ? "Volumen" : "Brillo"}">${ic}${nqBar(v, off)}</div>`;
    }
    if (r.type === "mix") { const m = r.m; return `<div ${at} class="qrow nqsl qmix nqmix${f}${m.volume <= 0 ? " muted" : ""}"><span class="mico">${mixIcon(m)}</span><span class="qlab">${esc(m.name)}</span>${nqBar(m.volume)}</div>`; }
    if (r.type === "val") { const [t, on] = nqVal(r); return `<div ${at} class="qrow nqit${f}${VPN.busy && r.k === "vpn" ? " busy" : ""}"><span class="qlab">${r.label}</span><span class="nqv${on ? " on" : ""}">${esc(t)}</span></div>` + (f && r.help ? `<p class="nqhelp">${r.help}</p>` : ""); }
    return `<div ${at} class="qrow nqit${f}">${r.icon || ""}<span class="qlab">${r.label}</span></div>`;
  };
  const b = Math.round(deviceBatt * 100), gold = settings.dolbyGold !== false;
  q.innerHTML = `<div class="nqst"><span class="qclock clock">${clockText()}</span><span class="nqwifi"${sys.wifi && !sys.airplane && netOn() ? "" : ' style="opacity:.3"'}>${NQ.wifi}</span><button class="nqbatt" aria-label="Batería">${b} % ${battSvg(deviceBatt)}</button></div>
    ${dolbyOn() ? `<div class="dolby qdolby${gold ? " gold" : ""}">${dolbyHTML()}</div>` : ""}
    <div class="nqwrap"><div class="qrows nqlist">${rows.map(row).join("")}</div><i class="nqscroll"></i></div>
    <div class="qhint nqfoot"><span class="nqdev"><span class="padsq">${$("#padsq").innerHTML}</span><span class="padicon ${padIconCls()}"></span></span><span class="hint"><span class="glyph">B</span>Atrás</span><span class="hint"><span class="glyph">A</span>Aceptar</span></div>`;
  const nl = $(".nqlist", q); nl.scrollTop = keep;
  const fr = $(".nqlist .focused", q);
  if (fr) { const h = fr.nextElementSibling && fr.nextElementSibling.classList.contains("nqhelp") ? fr.nextElementSibling : fr; fr.scrollIntoView({ block: "nearest" }); if (h !== fr) h.scrollIntoView({ block: "nearest" }); }
  nqScrollbar();
  $(".nqbatt", q).onclick = e => { e.stopPropagation(); battPop($(".nqbatt", q)); };
}
/* Rayita a la derecha que dice cuánto falta de la lista (solo si no cabe). */
function nqScrollbar() {
  const q = $("#quick"), l = $(".nqlist", q), s = $(".nqscroll", q); if (!l || !s) return;
  const max = l.scrollHeight - l.clientHeight;
  if (max <= 2) { s.style.display = "none"; return; }
  const h = Math.max(30, l.clientHeight * l.clientHeight / l.scrollHeight);
  s.style.display = ""; s.style.height = h + "px"; s.style.top = (l.scrollTop / max) * (l.clientHeight - h) + "px";
}
function qmInputNS(k) {
  const rows = qmRows(), r = rows[QM.i], n = rows.length;
  if (k === "b") return closeQuick();
  if (!r) { QM.i = 0; return renderQuick(); }
  if (k === "up" || k === "down") {
    const j = QM.i + (k === "down" ? 1 : -1);
    if (j < 0 || j >= n) return play("bump");
    QM.i = j; play("move"); return renderQuick();
  }
  if (r.type === "media") {
    if (k === "lb" || k === "rb") return mediaSeek((media ? mediaPos() : 0) + (k === "rb" ? 10 : -10));
    if (k === "left" || k === "right") {
      const opts = [1, 2, 3].filter(j => mediaButtons()[j].ok), at = opts.indexOf(QM.m), n2 = opts[(at < 0 ? opts.indexOf(2) : at) + (k === "right" ? 1 : -1)];
      if (n2 == null) return play("bump");
      QM.m = n2; play("move"); return renderQuick();
    }
    if (k === "a") { const b = mediaButtons()[QM.m]; if (!b || !b.ok) return play("bump"); play("select"); return b.run(); }
    if (k === "y") { const b = mediaButtons()[0]; if (b.ok) { play("select"); b.run(); } return; }
    return play("bump");
  }
  if (k === "left" || k === "right") {
    const d = k === "right" ? 1 : -1;
    if (r.type === "mix") {
      const v = clamp(r.m.volume + d * 5, 0, 100); if (v === r.m.volume) return play("bump");
      r.m.volume = v; QM.mixTouched = Date.now(); play("volume", v / 50); Host.call("mixer.set", { exe: r.m.exe, value: v }).catch(() => {});
      return renderQuick();
    }
    if (r.type === "slider") {
      if (r.k === "brightness" && QM.noBri) { play("bump"); return toast("Esta pantalla no deja cambiar el brillo desde Windows"); }
      if (r.k === "brightness" && sys.autoBri) { play("bump"); return toast("Quita el brillo automático para moverlo a mano"); }
      const v = clamp(sys[r.k] + d * 5, 0, 100); if (v === sys[r.k]) return play("bump");
      sys[r.k] = v; play(r.k === "volume" ? "volume" : d > 0 ? "up" : "down", v / 50); applySys(r.k); return renderQuick();
    }
    return play("bump");
  }
  if (k !== "a") return play("bump");
  if (r.type === "slider" || r.type === "mix") return play("bump");
  if (r.k === "vpn") return vpnSet(!vpnOn());
  if (r.k === "wifi" || r.k === "airplane") return setRadio(r.k, !sys[r.k]);
  if (r.k === "autobri") return setAutoBright(!sys.autoBri);
  if (OVERLAY && (r.k === "sleep" || r.k === "settings" || r.k === "hibernate")) {   // lo hace la pantalla principal
    closeQuick(true); return setTimeout(() => Host.call("quick.action", { action: r.k }).catch(() => {}), 240);
  }
  if (r.k === "hibernate") { play("select"); closeQuick(true); return setTimeout(() => sleepConsole("hibernate"), 250); }
  if (r.k === "sleep") { closeQuick(true); return setTimeout(powerMenu, 250); }
  if (r.k === "settings") {
    closeQuick(true);
    if (mode === "game") goHome();
    return setTimeout(() => { if (mode === "ui" && !(topView() && topView().isSettings)) openView(settingsView()); }, 380);
  }
  play("bump");
}
/* Brillo automático (solo aparece si la consola tiene sensor de luz). Windows decide el brillo mientras está prendido. */
function setAutoBright(on) {
  const prev = !!sys.autoBri;
  sys.autoBri = on; saveSys(); play(on ? "on" : "off");
  QM.sysTouched = Object.assign(QM.sysTouched || {}, { autoBri: Date.now() });
  if (QM.open) renderQuick();
  Host.call("system.autobright", { on }).then(r => { if (r && r.error) { sys.autoBri = prev; toast(r.error); } else if (r && typeof r.on === "boolean") sys.autoBri = r.on; })
    .catch(() => { sys.autoBri = prev; toast("No se pudo cambiar"); })
    .finally(() => { saveSys(); if (QM.open) renderQuick(); });
}

