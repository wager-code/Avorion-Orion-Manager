import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const { chromium } = require(
  "C:/Users/wager/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright",
);

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputDir = path.resolve(projectRoot, "..", "docs", "ai-review", "screenshots", "current");
fs.mkdirSync(outputDir, { recursive: true });

const routes = [
  ["01", "server-control", "/server/control", "服务器管理 · 控制"],
  ["02", "server-performance", "/server/performance", "服务器管理 · 性能"],
  ["03", "server-memory", "/server/memory", "服务器管理 · 内存与星区"],
  ["04", "server-update", "/server/update", "服务器管理 · 更新"],
  ["05", "server-tasks", "/server/tasks", "服务器管理 · 自动任务"],
  ["06", "server-backup", "/server/backup", "服务器管理 · 备份"],
  ["07", "server-logs", "/server/logs", "服务器管理 · 日志"],
  ["08", "server-diagnostics", "/server/diagnostics", "服务器管理 · 诊断"],
  ["09", "players", "/players", "玩家管理"],
  ["10", "alliances", "/alliances", "联盟管理"],
  ["11", "game-rewards", "/game/rewards", "游戏管理 · 奖励中心"],
];
const viewports = [
  { name: "1440x900", width: 1440, height: 900, fullPage: true },
  { name: "1920x1080", width: 1920, height: 1080, fullPage: false },
];

const browser = await chromium.launch({
  headless: true,
  executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe",
});
const report = {
  capturedAt: new Date().toISOString(),
  baseUrl: "http://127.0.0.1:4173",
  note: "Current local runtime state; no application API responses are mocked.",
  pages: [],
};

for (const viewport of viewports) {
  const context = await browser.newContext({
    viewport: { width: viewport.width, height: viewport.height },
    locale: "zh-CN",
    timezoneId: "Asia/Shanghai",
    reducedMotion: "reduce",
  });
  const page = await context.newPage();
  let currentRoute = "";
  const consoleErrors = [];
  const pageErrors = [];
  const failedResponses = [];
  page.on("console", (message) => {
    if (message.type() === "error") consoleErrors.push({ route: currentRoute, message: message.text() });
  });
  page.on("pageerror", (error) => pageErrors.push({ route: currentRoute, message: error.message }));
  page.on("response", (response) => {
    if (response.status() >= 400)
      failedResponses.push({ route: currentRoute, status: response.status(), url: response.url() });
  });

  for (const [order, slug, route, label] of routes) {
    currentRoute = route;
    const consoleStart = consoleErrors.length;
    const pageErrorStart = pageErrors.length;
    const failedStart = failedResponses.length;
    await page.goto(`http://127.0.0.1:4173${route}`, { waitUntil: "domcontentloaded" });
    await page.waitForTimeout(1300);
    await page.evaluate(() => document.fonts.ready);

    const screenshotName = `${order}-${slug}-${viewport.name}${viewport.fullPage ? "-full" : ""}.png`;
    const screenshotPath = path.join(outputDir, screenshotName);
    await page.screenshot({ path: screenshotPath, fullPage: viewport.fullPage, animations: "disabled" });
    const data = await page.evaluate(() => {
      const root = document.documentElement;
      const heading = document.querySelector("main h1, h1")?.textContent?.trim() ?? null;
      return {
        heading,
        title: document.title,
        bodyText: document.body.innerText.slice(0, 2400),
        viewportWidth: root.clientWidth,
        documentWidth: root.scrollWidth,
        viewportHeight: root.clientHeight,
        documentHeight: root.scrollHeight,
      };
    });
    report.pages.push({
      order,
      slug,
      label,
      route,
      viewport: viewport.name,
      screenshot: `screenshots/current/${screenshotName}`,
      horizontalOverflow: data.documentWidth > data.viewportWidth,
      ...data,
      consoleErrors: consoleErrors.slice(consoleStart),
      pageErrors: pageErrors.slice(pageErrorStart),
      failedResponses: failedResponses.slice(failedStart),
    });
  }
  await context.close();
}

await browser.close();
fs.writeFileSync(path.join(outputDir, "report.json"), JSON.stringify(report, null, 2));
console.log(JSON.stringify({
  pages: report.pages.length,
  screenshots: report.pages.map((page) => page.screenshot),
  horizontalOverflow: report.pages.filter((page) => page.horizontalOverflow).map((page) => `${page.viewport}:${page.route}`),
  pageErrors: report.pages.flatMap((page) => page.pageErrors),
}, null, 2));
