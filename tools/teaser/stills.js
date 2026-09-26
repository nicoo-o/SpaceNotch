const { chromium } = require('playwright');
(async () => {
  const times = process.argv.slice(2).map(Number);
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
  page.on('console', m => console.log('page:', m.text()));
  page.on('pageerror', e => console.log('ERR', e.message));
  await page.goto('file://' + __dirname + '/teaser.html');
  await page.evaluate(() => window.ready);
  for (const t of times) {
    await page.evaluate(t => window.render(t), t);
    await page.screenshot({ path: `${__dirname}/still-${t.toFixed(2)}.png` });
  }
  await browser.close();
})();
