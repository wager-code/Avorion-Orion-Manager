import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const { launchQaBrowser } = require("../../tools/qa-browser-runtime.cjs");

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputDir = path.join(projectRoot, "qa", "inventory-real");
fs.mkdirSync(outputDir, { recursive: true });

const browser = await launchQaBrowser({
  headless: true
});
const report = { test: "inventory-real-visual-qa", status: "PASS", startedAt: new Date().toISOString(), screenshots: [], checks: [], consoleErrors: [], pageErrors: [], failedResponses: [], lastCompletedStep: null, failedStep: null, reason: null };

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
    if (response.status() >= 400) report.failedResponses.push(`${viewport.name}: ${response.status()} ${response.url()}`);
  });

  // The test galaxy has no online players. Expose its known player in the online-only roster
  // for this browser context so the existing detail dialog can render the real inventory API.
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
          items: [{ index: 1, name: "超人特工队", online: true, x: 0, y: 0 }],
        },
      }),
    });
  });

  await page.goto("http://127.0.0.1:4173/players", { waitUntil: "networkidle" });
  await page.getByRole("button", { name: "查看" }).click();
  await page.getByRole("button", { name: "打开 Inventory 工作台" }).click();
  await page.locator(".inventory-workbench").waitFor({ state: "visible", timeout: 15_000 });
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(500);
  if (viewport.name === "1440x900") {
    await page.locator(".inventory-workbench__list").scrollIntoViewIfNeeded();
  }

  const workbench = page.locator(".inventory-workbench");
  const turretRows = workbench.locator(".inventory-workbench__item", { hasText: "炮塔" });
  await turretRows.first().waitFor({ timeout: 15_000 });
  const turretRowCount = await turretRows.count();
  const turretDetailTexts = [];
  for (let index = 0; index < turretRowCount; index += 1) {
    await turretRows.nth(index).click();
    turretDetailTexts.push(await workbench.locator(".inventory-workbench__inspector").innerText());
  }
  const dialogText = turretDetailTexts.join("\n");
  const metrics = await workbench.evaluate((element) => ({
    clientWidth: element.clientWidth,
    scrollWidth: element.scrollWidth,
    clientHeight: element.clientHeight,
    scrollHeight: element.scrollHeight,
  }));
  const screenshotPath = path.join(outputDir, `inventory-${viewport.name}.png`);
  await page.screenshot({ path: screenshotPath, fullPage: false, animations: "disabled" });

  report.screenshots.push(screenshotPath);
  report.lastCompletedStep = "inventory workbench rendered";
  report.checks.push({
    viewport: viewport.name,
    turretRows: turretRowCount,
    workbenchVisible: await workbench.isVisible(),
    showsMiningLaser: dialogText.includes("采矿激光"),
    showsChainGun: dialogText.includes("机枪"),
    showsMaterial: dialogText.includes("铁"),
    showsTech: dialogText.includes("科技"),
    showsDps: dialogText.includes("DPS"),
    showsRange: dialogText.includes("射程"),
    leaksUserdata: dialogText.includes("userdata:"),
    horizontalOverflow: metrics.scrollWidth > metrics.clientWidth,
    ...metrics,
  });
  await context.close();
}

await browser.close();
const failedCheck = report.checks.find((check) =>
  check.turretRows !== 2
  || !check.workbenchVisible
  || !check.showsMiningLaser
  || !check.showsChainGun
  || !check.showsMaterial
  || !check.showsTech
  || !check.showsDps
  || !check.showsRange
  || check.leaksUserdata
  || check.horizontalOverflow
);
if (report.failedResponses.some((value) => value.includes(" 503 "))) {
  report.status = "BLOCKED";
  report.failedStep = "real inventory API";
  report.reason = "SERVER_NOT_RUNNING: disposable Avorion + OrionAdminBridge is not running";
} else if (failedCheck || report.consoleErrors.length > 0 || report.pageErrors.length > 0 || report.failedResponses.length > 0) {
  report.status = "FAIL";
  report.failedStep = "inventory detail assertions";
  report.reason = failedCheck
    ? `Inventory assertions failed at ${failedCheck.viewport}`
    : "Browser console, page, or network errors were detected";
}
report.finishedAt = new Date().toISOString();
report.durationMs = Date.now() - Date.parse(report.startedAt);
fs.writeFileSync(path.join(outputDir, "report.json"), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));






