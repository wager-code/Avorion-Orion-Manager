import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const { chromium } = require(
  "C:/Users/wager/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright",
);

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputDir = path.join(projectRoot, "qa", "server-setup-preflight");
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
  const context = await browser.newContext({
    viewport,
    locale: "zh-CN",
    timezoneId: "Asia/Shanghai",
    reducedMotion: "reduce",
  });
  const page = await context.newPage();
  page.on("console", (message) => {
    if (message.type() === "error") report.consoleErrors.push(`${viewport.name}: ${message.text()}`);
  });
  page.on("pageerror", (error) => report.pageErrors.push(`${viewport.name}: ${error.message}`));

  await page.goto("http://127.0.0.1:4173/server/update?step=1", { waitUntil: "domcontentloaded", timeout: 10000 });
  await page.getByRole("heading", { name: "Galaxy 与基本信息", exact: true }).waitFor();
  await page.getByRole("button", { name: "选择目录", exact: true }).click();
  const picker = page.getByRole("dialog");
  await picker.getByRole("heading", { name: /选择新 Galaxy 保存位置|选择已有 Galaxy 目录/ }).waitFor();
  const realDirectoryEntries = await picker.locator(".directory-picker__list button").count();
  const pickerHasMockCopy = await picker.getByText(/Mock/i).count() > 0;
  const pickerScreenshot = path.join(outputDir, `01-real-galaxy-directory-picker-${viewport.name}.png`);
  await page.screenshot({ path: pickerScreenshot, fullPage: false, animations: "disabled" });
  report.screenshots.push(pickerScreenshot);
  await picker.getByRole("button", { name: "取消", exact: true }).click();

  await page.goto("http://127.0.0.1:4173/server/update?step=2", { waitUntil: "domcontentloaded", timeout: 10000 });
  await page.getByRole("heading", { name: "网络与 RCON", exact: true }).waitFor();
  await page.getByRole("button", { name: "保存草稿", exact: true }).waitFor({ state: "visible" });
  await page.waitForFunction(() => {
    const button = Array.from(document.querySelectorAll("button"))
      .find((candidate) => candidate.textContent?.trim() === "保存草稿");
    return button instanceof HTMLButtonElement && !button.disabled;
  });
  await page.locator('input[type="password"]').fill("QaOnly-setup-secret-9284");
  await page.getByRole("button", { name: "下一步：管理 MOD", exact: true }).click();
  await page.getByRole("button", { name: "下一步：检查与启动", exact: true }).click();
  await page.getByRole("heading", { name: "执行前确认", exact: true }).waitFor();
  await page.getByRole("button", { name: "保存并运行真实预检", exact: true }).click();
  await page.getByText("预检未通过", { exact: true }).waitFor({ timeout: 10000 });

  const metrics = await page.evaluate(() => {
    const root = document.documentElement;
    const visibleTextOverflows = Array.from(document.querySelectorAll("h1,h2,h3,h4,p,strong,span,code,button,small"))
      .filter((element) => {
        const style = window.getComputedStyle(element);
        const rect = element.getBoundingClientRect();
        return style.display !== "none" && style.visibility !== "hidden" && rect.width > 0 && rect.height > 0;
      })
      .filter((element) => element.scrollWidth > element.clientWidth + 1 || element.scrollHeight > element.clientHeight + 1)
      .map((element) => ({
        tag: element.tagName,
        className: element.className,
        text: element.textContent?.trim().slice(0, 100),
        clientWidth: element.clientWidth,
        scrollWidth: element.scrollWidth,
        clientHeight: element.clientHeight,
        scrollHeight: element.scrollHeight,
      }));
    return {
      clientWidth: root.clientWidth,
      scrollWidth: root.scrollWidth,
      clientHeight: root.clientHeight,
      scrollHeight: root.scrollHeight,
      horizontalOverflow: root.scrollWidth > root.clientWidth,
      verticalScroll: root.scrollHeight > root.clientHeight,
      visibleTextOverflows,
    };
  });

  const savedDraftResponse = await page.request.get("http://127.0.0.1:5088/api/v1/servers/local/server-setup/draft");
  const savedDraftText = await savedDraftResponse.text();
  const screenshot = path.join(outputDir, `02-real-preflight-result-${viewport.name}.png`);
  await page.screenshot({ path: screenshot, fullPage: true, animations: "disabled" });
  report.screenshots.push(screenshot);
  report.checks.push({
    viewport: viewport.name,
    realDirectoryEntries,
    pickerHasMockCopy,
    savedDraftRequestOk: savedDraftResponse.ok(),
    secretAbsentFromDraftResponse: !savedDraftText.includes("QaOnly-setup-secret-9284") && !savedDraftText.includes("rconPassword"),
    preflightFailureVisible: await page.getByText("预检未通过", { exact: true }).isVisible(),
    environmentMissingVisible: await page.getByText("尚未保存 SteamCMD 与 Avorion 服务端路径", { exact: true }).isVisible(),
    realPortRows: await page.locator(".setup-capability-list > div").count(),
    ...metrics,
  });
  await context.close();
}

await browser.close();
fs.writeFileSync(path.join(outputDir, "report.json"), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
