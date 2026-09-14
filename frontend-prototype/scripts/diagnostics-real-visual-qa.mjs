import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const { chromium } = require(
  "C:/Users/wager/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright",
);

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputDir = path.join(projectRoot, "qa", "diagnostics-real");
fs.mkdirSync(outputDir, { recursive: true });

const browser = await chromium.launch({
  headless: true,
  executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe",
});
const report = { screenshots: [], checks: [], consoleErrors: [], pageErrors: [] };

for (const viewport of [
  { name: "1920x1080", width: 1920, height: 1080 },
  { name: "1440x900", width: 1440, height: 900 },
]) {
  const context = await browser.newContext({
    viewport: { width: viewport.width, height: viewport.height },
    locale: "zh-CN",
    timezoneId: "Asia/Shanghai",
    reducedMotion: "reduce",
  });
  const page = await context.newPage();
  let diagnosticPosts = 0;
  page.on("request", (request) => {
    if (request.method() === "POST" && request.url().includes("/diagnostics/runs")) diagnosticPosts += 1;
  });
  page.on("console", (message) => {
    if (message.type() === "error") report.consoleErrors.push(`${viewport.name}: ${message.text()}`);
  });
  page.on("pageerror", (error) => report.pageErrors.push(`${viewport.name}: ${error.message}`));

  await page.goto("http://127.0.0.1:4173/server/diagnostics", { waitUntil: "networkidle" });
  await page.getByRole("heading", { name: "服务器诊断" }).waitFor();
  await page.getByRole("button", { name: "开始诊断" }).click();
  await page.waitForFunction(() => {
    const button = Array.from(document.querySelectorAll("button")).find((item) => item.textContent?.includes("重新诊断"));
    const completed = document.querySelector(".diagnostics-health__time")?.textContent?.includes("完成");
    return Boolean(button && completed);
  }, { timeout: 20_000 });
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(2800);

  const dimensions = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
    clientHeight: document.documentElement.clientHeight,
    scrollHeight: document.documentElement.scrollHeight,
  }));
  const visibleText = await page.locator("body").innerText();
  const screenshotPath = path.join(outputDir, `diagnostics-${viewport.name}.png`);
  await page.screenshot({ path: screenshotPath, fullPage: true, animations: "disabled" });
  report.screenshots.push(screenshotPath);

  const beforeRestorePosts = diagnosticPosts;
  if (viewport.name === "1920x1080") {
    await page.goto("http://127.0.0.1:4173/server/logs", { waitUntil: "networkidle" });
    await page.goto("http://127.0.0.1:4173/server/diagnostics", { waitUntil: "networkidle" });
    await page.getByRole("button", { name: "重新诊断" }).waitFor();
    await page.waitForTimeout(250);
  }
  const restoredText = await page.locator("body").innerText();

  report.checks.push({
    viewport: viewport.name,
    horizontalOverflow: dimensions.scrollWidth > dimensions.clientWidth,
    verticalScroll: dimensions.scrollHeight > dimensions.clientHeight,
    showsMockText: /Mock/i.test(visibleText),
    standardRows: await page.locator(".diagnostics-row").count(),
    memoryRows: await page.locator(".diagnostics-memory-row").count(),
    showsUnavailableTruthfully: visibleText.includes("不可用"),
    showsExistingModLogLabel: visibleText.includes("现有 MOD 日志错误"),
    hasRealCompletionTime: /\d{2}:\d{2}:\d{2}\s*完成/.test(visibleText),
    diagnosticPosts,
    restoredWithoutNewPost: viewport.name !== "1920x1080" || diagnosticPosts === beforeRestorePosts,
    restoredCompletedResult: viewport.name !== "1920x1080" || !restoredText.includes("尚未运行诊断"),
    ...dimensions,
  });

  await context.close();
}

await browser.close();
fs.writeFileSync(path.join(outputDir, "report.json"), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
