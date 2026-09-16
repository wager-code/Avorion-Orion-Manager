import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const { launchQaBrowser } = require("../../tools/qa-browser-runtime.cjs");

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputDir = path.join(projectRoot, "qa", "inventory-catalog");
fs.mkdirSync(outputDir, { recursive: true });

const browser = await launchQaBrowser({
  headless: true
});
const report = { screenshots: [], checks: [], consoleErrors: [], pageErrors: [], failedResponses: [], failedCatalogResponses: [] };

for (const viewport of [
  { name: "1920x1080", width: 1920, height: 1080 },
  { name: "1440x900", width: 1440, height: 900 },
  { name: "1672x941", width: 1672, height: 941 },
]) {
  const context = await browser.newContext({
    viewport: { width: viewport.width, height: viewport.height },
    locale: "zh-CN",
    timezoneId: "Asia/Shanghai",
    reducedMotion: "reduce",
  });
  const page = await context.newPage();
  page.on("console", (message) => {
    if (message.type() === "error") report.consoleErrors.push(`${viewport.name}: ${message.text()}`);
  });
  page.on("pageerror", (error) => report.pageErrors.push(`${viewport.name}: ${error.message}`));
  page.on("response", (response) => {
    if (response.status() >= 400) {
      report.failedResponses.push(`${viewport.name}: ${response.status()} ${response.url()}`);
      if (/inventory-(catalog|icons)/.test(response.url()))
        report.failedCatalogResponses.push(`${viewport.name}: ${response.status()} ${response.url()}`);
    }
  });

  // The server is intentionally allowed to be stopped during this catalog-only QA.
  // Mock only the roster entry needed to open the existing player detail; catalog and icons stay real.
  await page.route("**/api/v1/servers/local/management-bridge/players?**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        sampledAt: new Date().toISOString(),
        freshness: "qa-roster-only",
        data: {
          componentVersion: "0.10.0",
          total: 1,
          offset: 0,
          limit: 10,
          items: [{ index: 1, name: "目录验收玩家", online: true, x: 0, y: 0 }],
        },
      }),
    });
  });
  await page.route("**/api/v1/servers/local/players/1/assets", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        playerIndex: 1,
        playerName: "目录验收玩家",
        online: true,
        credits: 0,
        resources: { iron: 0, titanium: 0, naonite: 0, trinium: 0, xanion: 0, ogonite: 0, avorion: 0 },
        sampledAt: new Date().toISOString(),
        source: "qa-player-shell-only",
        componentVersion: "0.10.0",
      }),
    });
  });
  await page.route("**/api/v1/servers/local/players/1/inventory?**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        ownerKind: "player",
        ownerIndex: 1,
        ownerName: "目录验收玩家",
        total: 0,
        occupiedSlots: 0,
        maxSlots: 1000,
        items: [],
        sampledAt: new Date().toISOString(),
        componentVersion: "0.10.0",
      }),
    });
  });

  await page.goto("http://127.0.0.1:4173/players", { waitUntil: "networkidle" });
  await page.getByRole("button", { name: "查看" }).first().click();
  await page.getByRole("button", { name: "打开 Inventory 工作台" }).click();
  await page.getByRole("button", { name: "物品目录" }).click();
  const dialog = page.locator(".inventory-workbench");
  await dialog.getByRole("heading", { name: "物品目录", exact: true }).waitFor({ timeout: 15_000 });
  await dialog.locator(".inventory-workbench__item").first().waitFor({ timeout: 15_000 });
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(700);

  const initialCount = await dialog.locator(".inventory-workbench__item").count();
  const loadedIconCount = await dialog.locator(".inventory-workbench__item img").evaluateAll((images) =>
    images.filter((image) => image.complete && image.naturalWidth > 0).length,
  );
  const text = await dialog.innerText();
  const metrics = await dialog.evaluate((element) => ({
    clientWidth: element.clientWidth,
    scrollWidth: element.scrollWidth,
    clientHeight: element.clientHeight,
    scrollHeight: element.scrollHeight,
  }));
  const screenshotPath = path.join(outputDir, `catalog-${viewport.name}.png`);
  await page.screenshot({ path: screenshotPath, fullPage: false, animations: "disabled" });

  await dialog.getByLabel("Grant Policy").selectOption("verified-grantable");
  const grantableCount = await dialog.locator(".inventory-workbench__item").count();
  await dialog.getByLabel("Grant Policy").selectOption("all");
  await dialog.getByLabel("类型").selectOption("turret");
  const turretCount = await dialog.locator(".inventory-workbench__item").count();
  await dialog.getByLabel("类型").selectOption("all");
  await dialog.getByLabel("搜索 Inventory").fill("采矿激光");
  const miningSearchCount = await dialog.locator(".inventory-workbench__item").count();

  report.screenshots.push(screenshotPath);
  report.checks.push({
    viewport: viewport.name,
    initialCount,
    loadedIconCount,
    grantableCount,
    turretCount,
    miningSearchCount,
    showsDefinitionTotal: text.includes("58 个定义"),
    showsIndexedIconTotal: text.includes("922 个安全图标"),
    showsConnectedSource: text.includes("客户端资源已连接"),
    showsStoryBlocked: text.includes("剧情禁止"),
    horizontalOverflow: metrics.scrollWidth > metrics.clientWidth,
    ...metrics,
  });
  await context.close();
}

await browser.close();
fs.writeFileSync(path.join(outputDir, "report.json"), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));




