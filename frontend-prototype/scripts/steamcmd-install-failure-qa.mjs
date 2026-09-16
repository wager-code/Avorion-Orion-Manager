import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const { launchQaBrowser } = require("../../tools/qa-browser-runtime.cjs");

const operationId = process.argv[2];
if (!operationId) throw new Error("operation id is required");
const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const repositoryRoot = path.resolve(projectRoot, "..");
const installTarget = process.env.QA_STEAMCMD_INSTALL_TARGET || path.join(repositoryRoot, "backend", ".qa-steamcmd-install");
const outputDir = path.join(projectRoot, "qa", "steamcmd-install");
fs.mkdirSync(outputDir, { recursive: true });
const browser = await launchQaBrowser({
  headless: true
});
const report = { screenshots: [], consoleErrors: [], pageErrors: [], checks: [] };

for (const viewport of [
  { name: "1920x1080", width: 1920, height: 1080 },
  { name: "1440x900", width: 1440, height: 900 },
]) {
  const context = await browser.newContext({ viewport, locale: "zh-CN", timezoneId: "Asia/Shanghai", reducedMotion: "reduce" });
  const page = await context.newPage();
  page.on("console", (message) => { if (message.type() === "error") report.consoleErrors.push(`${viewport.name}: ${message.text()}`); });
  page.on("pageerror", (error) => report.pageErrors.push(`${viewport.name}: ${error.message}`));
  await page.goto(`http://127.0.0.1:4173/server/update?flow=install&step=2&operation=${encodeURIComponent(operationId)}`, { waitUntil: "domcontentloaded", timeout: 10000 });
  await page.getByText("STEAMCMD_DOWNLOAD_FAILED", { exact: true }).waitFor();
  const targetAbsent = !fs.existsSync(installTarget);
  const bodyMetrics = await page.evaluate(() => ({
    horizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth,
    verticalScroll: document.documentElement.scrollHeight > document.documentElement.clientHeight,
  }));
  const visibleTextOverflows = await page.locator("body *").evaluateAll((elements) => elements
    .filter((element) => {
      const style = getComputedStyle(element);
      const rect = element.getBoundingClientRect();
      return rect.width > 0 && rect.height > 0 && style.overflowX === "visible" && element.scrollWidth > element.clientWidth + 1;
    })
    .slice(0, 10)
    .map((element) => ({ tag: element.tagName, className: element.className, text: element.textContent?.trim().slice(0, 80) })));
  const screenshot = path.join(outputDir, `01-real-download-failed-${viewport.name}.png`);
  await page.screenshot({ path: screenshot, fullPage: true });
  report.screenshots.push(screenshot);
  report.checks.push({ viewport: viewport.name, targetAbsent, ...bodyMetrics, visibleTextOverflows });
  await context.close();
}

await browser.close();
console.log(JSON.stringify(report, null, 2));
