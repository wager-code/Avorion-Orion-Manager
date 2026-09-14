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

  // UI-only online-player fixture; asset values still come from the live local API.
  await page.route("**/api/v1/servers/local/management-bridge/players?*", async (route) => {
    await route.fulfill({
      contentType: "application/json",
      body: JSON.stringify({
        sampledAt: new Date().toISOString(),
        freshness: "live",
        data: {
          componentVersion: "0.6.0",
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
  const detail = page.locator(".player-detail-dialog");
  await detail.locator(".player-assets__credits").waitFor({ state: "visible", timeout: 10_000 });
  await detail.getByRole("button", { name: "发放奖励" }).click();
  const form = detail.locator(".player-reward");
  await form.waitFor({ state: "visible", timeout: 5_000 });

  const results = [];
  for (const viewport of [{ width: 1440, height: 900 }, { width: 1920, height: 1080 }]) {
    await page.setViewportSize(viewport);
    await page.waitForTimeout(250);
    const output = path.join(outputDirectory, `player-reward-${viewport.width}x${viewport.height}.png`);
    await page.screenshot({ path: output, fullPage: true });
    results.push({
      viewport: `${viewport.width}x${viewport.height}`,
      horizontalOverflow: await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth),
      detailInViewport: await detail.evaluate((element) => {
        const box = element.getBoundingClientRect();
        return box.left >= 0 && box.right <= window.innerWidth && box.top >= 0 && box.bottom <= window.innerHeight;
      }),
      output,
    });
  }

  await page.setViewportSize({ width: 1440, height: 900 });
  await form.getByRole("button", { name: "核对发放" }).click();
  const confirm = page.getByRole("dialog", { name: "确认发放玩家奖励" });
  await confirm.waitFor({ state: "visible", timeout: 5_000 });
  await page.waitForTimeout(250);
  const confirmationOutput = path.join(outputDirectory, "player-reward-confirm-1440x900.png");
  await page.screenshot({ path: confirmationOutput, fullPage: true });

  const formText = await form.innerText();
  const confirmText = await confirm.innerText();
  console.log(JSON.stringify({
    formHasSingleRewardScope: formText.includes("每次只发一种资产"),
    formHasLimits: formText.includes("单次上限"),
    confirmNamesExactPlayer: confirmText.includes("超人特工队") && confirmText.includes("#1"),
    confirmShowsExactAmount: confirmText.includes("1,000 Credits"),
    consoleErrors,
    pageErrors,
    confirmationOutput,
    results,
  }, null, 2));
  await browser.close();
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
