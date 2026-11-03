/* ================= íconos (originales) ================= */
const I = {
  ally: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"><path d="M5.6 6.4h12.8c1.5 0 2.8 1 3.2 2.4l1.5 5.9c.5 2-1.9 3.4-3.4 2l-1.6-1.6H5.9l-1.6 1.6c-1.5 1.4-3.9 0-3.4-2l1.5-5.9c.4-1.4 1.7-2.4 3.2-2.4z"/><rect x="7" y="7.9" width="10" height="5.6" rx=".6"/><circle cx="4.5" cy="9.3" r=".95"/><path d="M4.5 11.6v1.6M3.7 12.4h1.6" stroke-width="1.2"/><path d="M19.5 8.5v.01M18.7 9.3v.01M20.3 9.3v.01M19.5 10.1v.01" stroke-width="1.4"/><circle cx="19.5" cy="12.4" r=".8"/></svg>',
  friends: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="9" cy="8" r="3.4"/><path d="M2.8 19.5c.8-3.4 3.3-5.3 6.2-5.3s5.4 1.9 6.2 5.3"/><circle cx="17" cy="9" r="2.6"/><path d="M16.2 14.3c2.6-.2 4.5 1.4 5 4.3"/></svg>',
  news: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"><path d="M4 5h12v14H6a2 2 0 0 1-2-2z"/><path d="M16 9h4v8a2 2 0 0 1-2 2h-2"/><path d="M7.5 9h5M7.5 12.5h5M7.5 16h3" stroke-linecap="round"/></svg>',
  store: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"><path d="M5 8h14l-1.2 11.2a1.5 1.5 0 0 1-1.5 1.3H7.7a1.5 1.5 0 0 1-1.5-1.3z"/><path d="M9 10V7a3 3 0 0 1 6 0v3" stroke-linecap="round"/></svg>',
  album: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"><rect x="3" y="5" width="18" height="14" rx="2.5"/><circle cx="8.5" cy="10" r="1.6"/><path d="M4 17l5-4.5 3.5 3 3-2.5L20 17"/></svg>',
  trophy: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"><path d="M8 4h8v5a4 4 0 0 1-8 0z"/><path d="M8 6H4.5a3 3 0 0 0 3.6 4M16 6h3.5a3 3 0 0 1-3.6 4"/><path d="M12 13v4M8.5 20h7M10 17h4"/></svg>',
  chat: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"><path d="M4 6.5A2.5 2.5 0 0 1 6.5 4h11A2.5 2.5 0 0 1 20 6.5v7a2.5 2.5 0 0 1-2.5 2.5H10l-4.5 4v-4A1.5 1.5 0 0 1 4 14.5z"/><path d="M8.5 10h.01M12 10h.01M15.5 10h.01" stroke-width="2.8" stroke-linecap="round"/></svg>',
  pad: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 7h10a4.5 4.5 0 0 1 4.4 5.4l-.8 4a2.6 2.6 0 0 1-4.5 1.2L14.5 16h-5l-1.6 1.6a2.6 2.6 0 0 1-4.5-1.2l-.8-4A4.5 4.5 0 0 1 7 7z"/><path d="M8 10.5v3M6.5 12h3"/><path d="M15.5 11h.01M17.5 13h.01" stroke-width="2.6"/></svg>',
  gear: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linejoin="round"><circle cx="12" cy="12" r="3"/><path d="M12 2.8l1.6 2.3 2.7-.7.6 2.7 2.6 1-.9 2.6 1.6 2.3-2.2 1.6.2 2.8-2.8.2-1.3 2.4L12 19l-2.3 1.2-1.3-2.4-2.8-.2.2-2.8L3.6 13.6l1.6-2.3-.9-2.6 2.6-1 .6-2.7 2.7.7z"/></svg>',
  power: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><path d="M12 3v8"/><path d="M6.6 6.6a8 8 0 1 0 10.8 0"/></svg>',
  all: '<svg viewBox="0 0 24 24" fill="currentColor"><rect x="3" y="3" width="7.5" height="7.5" rx="2"/><rect x="13.5" y="3" width="7.5" height="7.5" rx="2"/><rect x="3" y="13.5" width="7.5" height="7.5" rx="2"/><rect x="13.5" y="13.5" width="7.5" height="7.5" rx="2"/></svg>',
  lock: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="5" y="10.5" width="14" height="10" rx="2"/><path d="M8.5 10.5V8a3.5 3.5 0 0 1 7 0v2.5"/></svg>',
  home: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round" stroke-linecap="round"><path d="M3.5 11 12 4l8.5 7"/><path d="M6 9.5V20h12V9.5"/><path d="M10 20v-5.5h4V20"/></svg>',
  play: '<svg viewBox="0 0 12 14" fill="currentColor"><path d="M1 1.2v11.6a.8.8 0 0 0 1.2.7l9.4-5.8a.8.8 0 0 0 0-1.4L2.2.5A.8.8 0 0 0 1 1.2z"/></svg>',
  cloud: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 18a4.5 4.5 0 0 1-.6-9 6 6 0 0 1 11.5 1.6A3.8 3.8 0 0 1 17.5 18"/><path d="M12 11v8M9 16l3 3 3-3"/></svg>',
  bt: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M7 7l10 10-5 5V2l5 5L7 17"/></svg>',
  tune: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M4 7h10M18 7h2M4 17h4M12 17h8"/><circle cx="16" cy="7" r="2"/><circle cx="10" cy="17" r="2"/></svg>',
  test: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="8"/><circle cx="12" cy="12" r="3" fill="currentColor"/></svg>',
  search: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><circle cx="10.5" cy="10.5" r="6"/><path d="M15 15l5 5"/></svg>',
  battLow: '<svg viewBox="0 0 30 16"><rect x=".8" y=".8" width="25" height="14.4" rx="3" fill="none" stroke="currentColor" stroke-width="1.6"/><rect x="27.2" y="5" width="2.2" height="6" rx="1" fill="currentColor"/><rect x="3" y="3" width="4" height="10" rx="1.5" fill="#e5484d"/></svg>'
};

const APPS = [
  { label: "Chat", icon: I.chat, c: "#dc6b17", act: el => openSystemApp(SYS.chat, el) },
  { label: "Noticias", icon: I.news, c: "#1ace61", act: el => openView(newsView(), el) },
  { label: "Tienda", icon: I.store, c: "#e23839", act: el => openSystemApp(SYS.store, el) },
  { label: "Álbum", icon: I.album, c: "#499fed", act: el => openView(albumView(), el) },
  { label: "Amigos", icon: I.friends, c: "#1ac4a9", act: el => { const v = profileView(); v.ci = 1; v.i = 1; openView(v, el); } },
  { label: "Controles", icon: I.ally, act: el => openView(controlsView(), el) },
  { label: "Logros", icon: I.trophy, act: el => openView(achHubView(), el) },
  { label: "Ajustes", icon: I.gear, act: el => openView(settingsView(), el) },
  { label: "Apagar", icon: I.power, act: () => powerMenu() }
];

