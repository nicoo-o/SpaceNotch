const { chromium } = require('playwright');
(async () => {
  const times = process.argv.slice(2).map(Number);
  const b = await chromium.launch({ args: ['--allow-file-access-from-files'] });
  const p = await b.newPage({ viewport: { width: 1920, height: 1080 } });
  p.on('pageerror', e => console.log('ERR', e.message));
  await p.goto('http://127.0.0.1:8123/edit.html');
  await p.evaluate(() => window.ready);
  for (const t of times) { await p.evaluate(t => window.render(t), t); await p.screenshot({ path: `${__dirname}/still-${t.toFixed(2)}.png` }); }
  await b.close();
})();
