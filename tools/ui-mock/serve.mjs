// Serves Modern Editor's ui/ folder with dev-mock.js (a fake editor host with sample traders, items and levels),
// so the editor page runs in any browser: node tools/ui-mock/serve.mjs  →  http://localhost:8766/index.html
// index.html loads dev-mock.js by itself when it isn't inside WebView2.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const ui = path.resolve(here, '../../ModernEditor/ModernEditor.Editor/ui');
const port = Number(process.env.PORT || 8766);
const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.png': 'image/png', '.json': 'application/json', '.webp': 'image/webp' };

http.createServer((req, res) => {
  let name = decodeURIComponent(new URL(req.url, 'http://x').pathname).replace(/^\/+/, '') || 'index.html';
  const file = name === 'dev-mock.js' ? path.join(here, name) : path.join(ui, name);
  if (!file.startsWith(ui) && !file.startsWith(here)) { res.writeHead(403).end(); return; }
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404).end('not found'); return; }
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' }).end(data);
  });
}).listen(port, () => console.log(`Modern Editor UI mock: http://localhost:${port}/index.html`));
