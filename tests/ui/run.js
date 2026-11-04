// Pruebas de la interfaz de Scarlet: arma ui/ con el anfitrión simulado (host.js), la sirve en el puerto 8799 y corre
// los escenarios con Chromium (Playwright). Uso:  node tests/ui/run.js [suite ...]   ·   ONLY=regex para filtrar.
// Sale con error si algún escenario falla (así GitHub no publica una versión rota).
const { execFileSync, spawn } = require('child_process');
const fs = require('fs'), path = require('path'), http = require('http');
const ROOT = path.resolve(__dirname, '..', '..'), OUT = path.join(__dirname, '.sitio');
const ALL = ['suite', 'beta', 'beta11', 'beta12', 'beta14', 'beta15', 'vpn', 'dl', 'install'];

fs.mkdirSync(OUT, { recursive: true });
execFileSync(process.env.PYTHON || 'python3', [path.join(ROOT, 'ui', 'build.py'), path.join(OUT, 'demo.html')], { stdio: 'inherit' });
const page = fs.readFileSync(path.join(OUT, 'demo.html'), 'utf8').replace('<head>', '<head><script src="host.js"></script>');
fs.writeFileSync(path.join(OUT, 'index.html'), page);
fs.copyFileSync(path.join(__dirname, 'host.js'), path.join(OUT, 'host.js'));

const server = http.createServer((req, res) => {
  const f = path.join(OUT, decodeURIComponent(req.url.split('?')[0]).replace(/^\/+/, '') || 'index.html');
  if (!f.startsWith(OUT) || !fs.existsSync(f)) { res.writeHead(404); return res.end(); }
  res.writeHead(200, { 'Content-Type': f.endsWith('.js') ? 'text/javascript' : 'text/html; charset=utf-8' });
  fs.createReadStream(f).pipe(res);
}).listen(8799, async () => {
  const suites = process.argv.slice(2).length ? process.argv.slice(2) : ALL;
  let bad = 0;
  for (const s of suites) {
    const t0 = Date.now();
    const out = await new Promise(res => {
      let buf = '';
      const p = spawn(process.execPath, [path.join(__dirname, 'escenarios', s + '.js')], { env: process.env });
      p.stdout.on('data', d => { buf += d; process.stdout.write(d); });
      p.stderr.on('data', d => { buf += d; process.stderr.write(d); });
      p.on('close', code => res({ buf, code }));
    });
    const fails = (out.buf.match(/^FALLA/gm) || []).length;
    const ok = out.code === 0 && fails === 0 && /escenarios/.test(out.buf);
    if (!ok) bad++;
    console.log(`\n== ${s}: ${ok ? 'bien' : 'CON FALLAS'} (${Math.round((Date.now() - t0) / 1000)} s)\n`);
  }
  server.close();
  console.log(bad ? `${bad} grupo(s) con fallas` : 'Todas las pruebas de la interfaz pasaron');
  process.exit(bad ? 1 : 0);
});
