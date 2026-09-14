import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const { chromium } = require(
  "C:/Users/wager/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright",
);

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputDir = path.join(projectRoot, "qa", "update-real-environment");
fs.mkdirSync(outputDir, { recursive: true });

const browser = await chromium.launch({
  headless: true,
  executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe",
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

  await page.goto("http://127.0.0.1:4173/server/update", { waitUntil: "domcontentloaded", timeout: 10000 });
  await page.getByRole("button", { name: "开始扫描", exact: true }).click();
  await page.getByText("真实扫描结果", { exact: true }).waitFor();
  const noMockSuccess = await page.getByText(/没有在已配置位置/).isVisible();
  await capture(page, viewport, "01-real-not-found");

  await page.getByRole("button", { name: "选择路径", exact: true }).click();
  const inputs = page.locator(".path-form-fields input");
  await inputs.nth(0).fill("C:\\missing\\steamcmd.exe");
  await inputs.nth(1).fill("D:\\missing-server");
  await page.getByRole("button", { name: "下一步：验证前确认", exact: true }).click();
  await page.getByRole("button", { name: "验证并使用", exact: true }).click();
  await page.getByText("真实验证未通过", { exact: true }).waitFor();
  const realValidationIssuesVisible = await page.getByText(/未找到 steamcmd.exe；服务端目录不存在/).isVisible();
  const stayedOutOfReadyState = await page.getByText("更新能力", { exact: true }).count() === 0;
  await capture(page, viewport, "02-real-validation-failed");

  report.checks.push({ viewport: viewport.name, noMockSuccess, realValidationIssuesVisible, stayedOutOfReadyState });
  await context.close();
}

await browser.close();
fs.writeFileSync(path.join(outputDir, "report.json"), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));

async function capture(page, viewport, state) {
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.evaluate(() => document.fonts.ready);
  await page.waitForFunction(() => !document.querySelector(".toast")?.classList.contains("toast--visible"), null, { timeout: 5000 });
  await page.waitForTimeout(120);
  const metrics = await page.evaluate(() => {
    const root = document.documentElement;
    const visibleTextOverflows = Array.from(document.querySelectorAll("h1,h2,h3,h4,p,strong,span,code,button,label,dd"))
      .filter((element) => {
        const style = window.getComputedStyle(element);
        const rect = element.getBoundingClientRect();
        return style.display !== "none" && style.visibility !== "hidden" && rect.width > 0 && rect.height > 0;
      })
      .filter((element) => element.scrollWidth > element.clientWidth + 1 || element.scrollHeight > element.clientHeight + 1)
      .map((element) => ({ tag: element.tagName, className: String(element.className), text: element.textContent?.trim().slice(0, 80) }));
    return {
      horizontalOverflow: root.scrollWidth > root.clientWidth,
      verticalScroll: root.scrollHeight > root.clientHeight,
      visibleTextOverflows,
    };
  });
  const screenshotPath = path.join(outputDir, `${state}-${viewport.name}.png`);
  await page.screenshot({ path: screenshotPath, fullPage: false, animations: "disabled" });
  report.screenshots.push(screenshotPath);
  report.checks.push({ viewport: viewport.name, state, ...metrics });
}
