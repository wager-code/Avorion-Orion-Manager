import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const { launchQaBrowser } = require("../../tools/qa-browser-runtime.cjs");

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputDir = path.join(projectRoot, "qa", "screenshots");
fs.mkdirSync(outputDir, { recursive: true });

const viewports = [
  { name: "1920x1080", width: 1920, height: 1080 },
  { name: "1440x900", width: 1440, height: 900 },
  { name: "1366x768", width: 1366, height: 768 },
  { name: "1536x864-at-125pct", width: 1536, height: 864, dpr: 1.25 },
];

const report = { reference: "qa/server-tasks-approved.png", screenshots: [], consoleErrors: [], pageErrors: [], checks: [] };
const browser = await launchQaBrowser({
  headless: true
});

for (const viewport of viewports) {
  const context = await browser.newContext({
    viewport: { width: viewport.width, height: viewport.height },
    deviceScaleFactor: viewport.dpr ?? 1,
    locale: "zh-CN",
    timezoneId: "Asia/Shanghai",
    reducedMotion: "reduce",
  });
  const page = await context.newPage();
  page.on("console", (message) => {
    if (message.type() === "error") report.consoleErrors.push(`${viewport.name}: ${message.text()}`);
  });
  page.on("pageerror", (error) => report.pageErrors.push(`${viewport.name}: ${error.message}`));

  await page.goto("http://127.0.0.1:4173/server/tasks", { waitUntil: "networkidle" });
  await page.evaluate(() => document.fonts.ready);

  const dimensions = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
    clientHeight: document.documentElement.clientHeight,
    scrollHeight: document.documentElement.scrollHeight,
  }));
  const screenshotPath = path.join(outputDir, `tasks-${viewport.name}.png`);
  await page.screenshot({ path: screenshotPath, fullPage: false, animations: "disabled" });
  report.screenshots.push(screenshotPath);

  await page.getByRole("button", { name: /已暂停 1/ }).click();
  const pausedRows = await page.locator(".tasks-table tbody tr").count();
  const pausedTaskVisible = await page.getByText("定时安全重启", { exact: true }).isVisible();
  await page.getByRole("switch", { name: "启用定时安全重启" }).click();
  const pausedZeroVisible = await page.getByRole("button", { name: /已暂停 0/ }).isVisible();
  await page.getByRole("button", { name: /全部 6/ }).click();

  await page.getByRole("button", { name: "编辑", exact: true }).first().click();
  const editDialogVisible = await page.getByRole("dialog").getByText("编辑任务", { exact: true }).isVisible();
  await page.getByRole("textbox", { name: "执行计划" }).fill("每 45 分钟");
  await page.getByRole("button", { name: "保存修改" }).click();
  const editedScheduleVisible = await page.getByRole("cell", { name: "每 45 分钟", exact: true }).isVisible();

  await page.getByRole("button", { name: "新建任务" }).click();
  const newDialogVisible = await page.getByRole("dialog").getByText("新建任务", { exact: true }).isVisible();
  await page.getByRole("combobox", { name: "任务类型" }).selectOption("broadcast");
  await page.getByRole("button", { name: "创建任务" }).click();
  const taskTotalAfterCreate = await page.locator(".tasks-summary__item").first().locator("strong").textContent();

  report.checks.push({
    viewport: viewport.name,
    horizontalOverflow: dimensions.scrollWidth > dimensions.clientWidth,
    verticalScroll: dimensions.scrollHeight > dimensions.clientHeight,
    pausedRows,
    pausedTaskVisible,
    pausedZeroVisible,
    editDialogVisible,
    editedScheduleVisible,
    newDialogVisible,
    taskTotalAfterCreate,
    ...dimensions,
  });
  await context.close();
}

await browser.close();
fs.writeFileSync(path.join(projectRoot, "qa", "tasks-visual-qa-report.json"), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
