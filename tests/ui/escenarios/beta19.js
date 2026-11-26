const { chromium } = require("playwright");
const assert = require("node:assert/strict");

(async () => {
  const browser = await chromium.launch();
  try {
    const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    await page.goto("http://localhost:8799/index.html");
    await page.waitForFunction(() => typeof bootStart !== "undefined" && performance.now() - bootStart > 750 && games.length);
    await page.evaluate(() => { if (mode === "boot") dismissBoot(); });
    await page.evaluate(() => {
      settings.lumina = true; Lumina.apply();
      const canvas = document.createElement("canvas"); canvas.width = 640; canvas.height = 360;
      const ctx = canvas.getContext("2d");
      ctx.fillStyle = "#334488"; ctx.fillRect(0, 0, 640, 360);
      const url = canvas.toDataURL();
      __qa.album = Array.from({ length: 1200 }, (_, i) => ({ path: "shot" + i, type: "image", url, game: "Alba", date: "hoy" }));
      const call = Host.call.bind(Host);
      Host.call = async (type, payload) => {
        if (type !== "album.savePng") return call(type, payload);
        const item = { path: payload.path + "-copy.png", url: "data:image/png;base64," + payload.png, type: "image", game: "Alba", date: "hoy" };
        __qa.album.unshift(item); return item;
      };
      openView(albumView());
    });
    await page.waitForSelector(".agrid .shot.focused");
    assert.ok(await page.locator(".agrid .shot").count() < 100);
    await page.evaluate(() => { topView().i = 350; updateFocus(); input("a"); input("right"); input("rt"); });
    assert.equal(await page.evaluate(() => topView().zoom), 1.5);
    await page.evaluate(() => {
      topView().more();
      const picker = topView(); picker.items().find(b => b.textContent.includes("Añadir texto")).click();
    });
    await page.waitForSelector(".caption-preview canvas:not([hidden])");
    await page.evaluate(() => { input("a"); topView().text = "Una captura de Alba"; topView().done(); });
    const pixels = await page.evaluate(() => $(".caption-preview canvas").toDataURL());
    await page.evaluate(() => input("start"));
    await page.waitForFunction(() => topView().cls === "viewer" && albumData[0].path === "shot351-copy.png");
    assert.equal(await page.evaluate(() => albumData[0].url), pixels);
    await page.evaluate(() => input("b"));
    assert.equal(await page.evaluate(() => topView().list[topView().i].path), "shot351");
    assert.ok(await page.locator(".agrid .shot").count() < 100);
    console.log("OK    1200 capturas: cuadrícula limitada, zoom, copia idéntica al editor y regreso al elemento correcto");

    // Seis imágenes de color con tiempos 0, 55, 190, 210, 435 y 500 ms.
    const fixture = require("fs").readFileSync(require("path").join(__dirname, "../fixtures/album-frames.webm")).toString("base64");
    await page.evaluate(data => {
      const c = { path: "clip", type: "video", url: "data:video/webm;base64," + data, game: "Alba", date: "hoy" };
      __qa.album.unshift(c); albumData = __qa.album.slice(); topView().i = 0; topView().refresh();
      input("a");
    }, fixture);
    await page.waitForFunction(() => topView().vid?.readyState >= 2);
    await page.evaluate(() => { const v = topView().vid; v.pause(); v.currentTime = .1; });
    await page.waitForFunction(() => !topView().vid.seeking);
    const color = () => page.evaluate(() => {
      const c = document.createElement("canvas"), ctx = c.getContext("2d");
      ctx.drawImage(topView().vid,0,0,1,1); return Array.from(ctx.getImageData(0,0,1,1).data).slice(0,3).map(x => x > 140);
    });
    assert.deepEqual(await color(), [false,true,false]);
    await page.evaluate(() => topView().stepFrame(1));
    await page.waitForFunction(() => !topView().vid.seeking);
    assert.deepEqual(await color(), [false,false,true]);
    await page.evaluate(() => topView().stepFrame(1));
    await page.waitForFunction(() => !topView().vid.seeking);
    assert.deepEqual(await color(), [true,true,false]);
    await page.evaluate(() => topView().stepFrame(-1));
    await page.waitForFunction(() => !topView().vid.seeking);
    assert.deepEqual(await color(), [false,false,true]);
    const expected = await page.evaluate(() => {
      const v = topView().vid; v.pause();
      const c = document.createElement("canvas"); c.width = v.videoWidth; c.height = v.videoHeight;
      c.getContext("2d").drawImage(v, 0, 0); input("y"); return c.toDataURL();
    });
    await page.waitForFunction(() => albumData[0].path === "clip-copy.png");
    assert.equal(await page.evaluate(() => albumData[0].url), expected);
    assert.deepEqual(errors, []);
    console.log("OK    El PNG guardado coincide con el fotograma pausado del video");
    await page.evaluate(() => {
      closeAllViews(); mode = "ui";
      const f = { name: "Ada", steamid: "42", game: "Alba", avatar: albumData[0].url };
      notifyFriend(f);
    });
    await page.waitForFunction(() => {
      const img = document.querySelector(".notif .portrait img"); return img && !img.hidden && img.naturalWidth > 0;
    });
    const notice = await page.evaluate(() => {
      mode = "game"; notifyFriend({ name: "Ada", game: "Alba", steamid: "42", avatar: "https://files.scarlet/f?p=C%3A%5Cavatar.png" });
      return __qa.log.filter(x => x.type === "overlay.notify").pop().payload;
    });
    assert.equal(notice.letter, "A"); assert.equal(notice.friendId, "42");
    assert.equal(notice.image, "C:\\avatar.png"); assert.equal(notice.style, "b");
    console.log("OK    El aviso muestra el avatar del amigo que juega y envía su identidad al renderizador nativo");
    console.log("3/3 escenarios OK");
  } finally { await browser.close(); }
})().catch(error => { console.error("FALLA", error); process.exitCode = 1; });
