const fs = require("node:fs");
const path = require("node:path");
const { chromium } = require("C:/Users/wager/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright");

const projectRoot = "E:/OrionAdmin";
const outputDirectory = path.join(projectRoot, ".e2e");

(async () => {
  fs.mkdirSync(outputDirectory, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    executablePath: "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  });
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 });
  const consoleErrors = [];
  const pageErrors = [];
  page.on("console", (message) => {
    if (message.type() === "error") consoleErrors.push(message.text());
  });
  page.on("pageerror", (error) => pageErrors.push(String(error)));

  // UI-only fixture: the live API has an offline real player, while the roster intentionally shows online players only.
  await page.route("**/api/v1/servers/local/management-bridge/players?*", async (route) => {
    await route.fulfill({
      contentType: "application/json",
      body: JSON.stringify({
        sampledAt: new Date().toISOString(),
        freshness: "live",
        data: {
          componentVersion: "0.5.0",
          total: 1,
          offset: 0,
          limit: 10,
          items: [{ index: 1, name: "超人特工队", online: true, x: -447, y: 47 }],
        },
      }),
    });
  });
  await page.route("**/api/v1/servers/local/events?*", async (route) => {
    await route.fulfill({ contentType: "application/json", body: JSON.stringify({ items: [] }) });
  });

  await page.goto("http://127.0.0.1:4173/players", { waitUntil: "networkidle", timeout: 60_000 });
  await page.getByRole("button", { name: "查看" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.waitFor({ state: "visible", timeout: 5_000 });
  await dialog.locator(".player-assets__credits").waitFor({ state: "visible", timeout: 10_000 });

  const results = [];
  for (const viewport of [{ width: 1440, height: 900 }, { width: 1920, height: 1080 }]) {
    await page.setViewportSize(viewport);
    await page.waitForTimeout(300);
    const output = path.join(outputDirectory, `player-assets-${viewport.width}x${viewport.height}.png`);
    await page.screenshot({ path: output, fullPage: true });
    results.push({
      viewport: `${viewport.width}x${viewport.height}`,
      horizontalOverflow: await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth),
      dialogInViewport: await dialog.evaluate((element) => {
        const box = element.getBoundingClientRect();
        return box.left >= 0 && box.right <= window.innerWidth && box.top >= 0 && box.bottom <= window.innerHeight;
      }),
      output,
    });
  }

  const resourceCount = await dialog.locator(".player-assets__resources > div").count();
  const body = await dialog.innerText();
  console.log(JSON.stringify({
    creditsVisible: body.includes("5,000"),
    allSevenResourcesVisible: resourceCount === 7,
    versionVisible: body.includes("OrionAdminBridge 0.5.0"),
    consoleErrors,
    pageErrors,
    results,
  }, null, 2));
  await browser.close();
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
