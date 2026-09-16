import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const { launchQaBrowser } = require("../../tools/qa-browser-runtime.cjs");

const steamCmdOperationId = process.argv[2];
if (!steamCmdOperationId) throw new Error("SteamCMD operation id is required");
const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputDir = path.join(projectRoot, "qa", "avorion-existing-reuse");
fs.mkdirSync(outputDir, { recursive: true });
const serverRoot = process.env.QA_AVORION_SERVER_ROOT;
const steamCmdRoot = process.env.QA_STEAMCMD_ROOT;
if (!serverRoot || !steamCmdRoot) {
  throw new Error("QA_AVORION_SERVER_ROOT and QA_STEAMCMD_ROOT are required for the real existing-server fixture");
}
const serverExecutable = path.join(serverRoot, "bin", "AvorionServer.exe");
const serverRunner = path.join(serverRoot, "bin", "ServerRunner.exe");
const steamCmdExecutable = path.join(steamCmdRoot, "steamcmd.exe");
const installIdentity = `${steamCmdExecutable}|${serverRoot}`.replaceAll("/", "\\").toLowerCase();
const beforeExecutable = fs.readFileSync(serverExecutable);
const beforeRunner = fs.readFileSync(serverRunner);
const browser = await launchQaBrowser({
  headless: true
});
const report = { screenshots: [], consoleErrors: [], pageErrors: [], checks: [] };

for (const viewport of [
  { name: "1920x1080", width: 1920, height: 1080 },
  { name: "1440x900", width: 1440, height: 900 },
]) {
  const context = await browser.newContext({ viewport, locale: "zh-CN", timezoneId: "Asia/Shanghai", reducedMotion: "reduce" });
  await context.addInitScript(({ steamCmdRoot, serverRoot, installIdentity }) => {
    window.sessionStorage.setItem("avorion.install.steamcmd", steamCmdRoot);
    window.sessionStorage.setItem("avorion.install.server", serverRoot);
    window.sessionStorage.setItem("avorion.install.server.idempotency", JSON.stringify({
      identity: installIdentity,
      key: "avorion-stale-known-failure-key",
    }));
  }, { steamCmdRoot, serverRoot, installIdentity });
  const page = await context.newPage();
  page.on("console", (message) => { if (message.type() === "error") report.consoleErrors.push(`${viewport.name}: ${message.text()}`); });
  page.on("pageerror", (error) => report.pageErrors.push(`${viewport.name}: ${error.message}`));
  await page.goto(`http://127.0.0.1:4173/server/update?flow=install&step=2&operation=${encodeURIComponent(steamCmdOperationId)}`, { waitUntil: "domcontentloaded", timeout: 10000 });
  await page.getByRole("button", { name: "检查并安装 Avorion 服务端", exact: true }).click();
  await page.getByText("现有 Avorion Dedicated Server 已验证并复用：", { exact: true }).waitFor();
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
  const continueVisible = await page.getByRole("button", { name: "验证路径并继续配置", exact: true }).isVisible();
  const operationRecoveredInUrl = new URL(page.url()).searchParams.get("operation") !== steamCmdOperationId;
  const screenshot = path.join(outputDir, `01-real-existing-server-${viewport.name}.png`);
  await page.screenshot({ path: screenshot, fullPage: true });
  report.screenshots.push(screenshot);
  report.checks.push({ viewport: viewport.name, continueVisible, operationRecoveredInUrl, ...bodyMetrics, visibleTextOverflows });
  await context.close();
}

await browser.close();
const afterExecutable = fs.readFileSync(serverExecutable);
const afterRunner = fs.readFileSync(serverRunner);
report.filesUnchanged = beforeExecutable.equals(afterExecutable) && beforeRunner.equals(afterRunner);
fs.writeFileSync(path.join(outputDir, "report.json"), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
