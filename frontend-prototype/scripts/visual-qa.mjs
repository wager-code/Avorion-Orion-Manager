import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const { launchQaBrowser } = require("../../tools/qa-browser-runtime.cjs");

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputDir = path.join(projectRoot, "qa", "screenshots");
fs.mkdirSync(outputDir, { recursive: true });

const baseUrl = "http://127.0.0.1:4173";
const viewports = [
  { name: "1920x1080", width: 1920, height: 1080 },
  { name: "1440x900", width: 1440, height: 900 },
  { name: "1366x768", width: 1366, height: 768 },
  { name: "1536x864-at-125pct", width: 1536, height: 864, dpr: 1.25 },
];

const browser = await launchQaBrowser({
  headless: true
});
const report = { screenshots: [], consoleErrors: [], pageErrors: [], checks: [] };

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

  for (const target of [
    { name: "control", path: "/server/control" },
    { name: "performance", path: "/server/performance?range=24h" },
    { name: "memory", path: "/server/memory" },
    { name: "update", path: "/server/update" },
    { name: "tasks", path: "/server/tasks" },
  ]) {
    await page.goto(`${baseUrl}${target.path}`, { waitUntil: "networkidle" });
    await page.evaluate(() => document.fonts.ready);
    await page.waitForTimeout(180);

    const dimensions = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth,
      clientHeight: document.documentElement.clientHeight,
      scrollHeight: document.documentElement.scrollHeight,
    }));

    const screenshotPath = path.join(outputDir, `${target.name}-${viewport.name}.png`);
    await page.screenshot({ path: screenshotPath, fullPage: false, animations: "disabled" });
    report.screenshots.push(screenshotPath);
    report.checks.push({
      page: target.name,
      viewport: viewport.name,
      horizontalOverflow: dimensions.scrollWidth > dimensions.clientWidth,
      verticalScroll: dimensions.scrollHeight > dimensions.clientHeight,
      ...dimensions,
    });
  }

  await page.goto(`${baseUrl}/server/control`, { waitUntil: "networkidle" });
  await page.getByRole("button", { name: "安全重启" }).click();
  await page.getByRole("dialog").waitFor({ state: "visible" });
  const restartDialogVisible = await page.getByText("安全重启服务器？").isVisible();
  await page.keyboard.press("Escape");

  await page.getByRole("button", { name: "强制终止进程" }).click();
  const dangerDialogVisible = await page.getByText("强制终止服务器进程？").isVisible();
  await page.keyboard.press("Escape");

  await page.getByRole("tab", { name: "性能" }).click();
  await page.waitForURL(/\/server\/performance/);
  await page.getByRole("button", { name: "1 小时" }).click();
  await page.waitForURL(/range=1h/);
  await page.waitForFunction(() =>
    document.querySelector('button[aria-pressed="true"]')?.textContent?.includes("1 小时"),
  );
  const rangeSelected = await page.getByRole("button", { name: "1 小时" }).getAttribute("aria-pressed");

  await page.getByRole("tab", { name: "内存与星区" }).click();
  await page.waitForURL(/\/server\/memory/);
  await page.getByRole("button", { name: /可卸载 6/ }).click();
  await page.waitForURL(/filter=eligible/);
  await page.waitForFunction(() =>
    Array.from(document.querySelectorAll(".sector-filter__button")).some(
      (button) => button.textContent?.includes("可卸载") && button.getAttribute("aria-pressed") === "true",
    ),
  );
  const eligibleFilterSelected = await page.getByRole("button", { name: /可卸载 6/ }).getAttribute("aria-pressed");

  const sectorSearch = page.getByRole("searchbox", { name: "搜索星区" });
  await sectorSearch.fill("裂隙");
  await page.waitForURL(/q=/);
  const filteredSectorVisible = await page.getByRole("cell", { name: "裂隙边缘", exact: true }).isVisible();
  await sectorSearch.click();
  await page.keyboard.press("Control+A");
  await page.keyboard.press("Backspace");
  await page.waitForFunction(() => !(new URL(window.location.href).searchParams.get("q") ?? ""));
  await page.waitForTimeout(150);

  await page.getByRole("combobox", { name: "星区排序" }).selectOption("memory");
  await page.waitForURL(/sort=memory/);
  await page.waitForFunction(() =>
    Array.from(document.querySelectorAll("select")).some((select) => select.value === "memory"),
  );
  const sortSelected = await page.getByRole("combobox", { name: "星区排序" }).inputValue();

  await page.getByRole("radio", { name: /尝试安全卸载空闲星区/ }).check();
  await page.getByRole("button", { name: "保存策略" }).click();
  const savedStrategyVisible = await page.locator(".memory-strategy-option", { hasText: "尝试安全卸载空闲星区" }).getByText("当前策略").isVisible();

  await page.getByRole("button", { name: "安全尝试卸载 2 个星区" }).click();
  const unloadDialogVisible = await page.getByText("安全尝试卸载 2 个星区？").isVisible();
  await page.keyboard.press("Escape");

  const scrollCheck = await page.evaluate(() => {
    window.scrollTo({ top: 0, behavior: "instant" });
    const before = window.scrollY;
    window.scrollTo({ top: document.documentElement.scrollHeight, behavior: "instant" });
    const after = window.scrollY;
    window.scrollTo({ top: before, behavior: "instant" });
    return { before, after, scrollHeight: document.documentElement.scrollHeight };
  });

  await page.getByRole("tab", { name: "更新" }).click();
  await page.waitForURL(/\/server\/update/);
  const serverManagementNavVisible = await page.getByRole("button", { name: "服务器管理", exact: true }).isVisible();
  await page.getByRole("button", { name: "开始扫描", exact: true }).click();
  await page.getByRole("heading", { name: "检测结果", exact: true }).waitFor({ timeout: 5000 });
  await page.getByRole("button", { name: "确认并使用", exact: true }).click();
  await page.getByRole("button", { name: "进入更新管理", exact: true }).waitFor({ timeout: 5000 });
  await page.getByRole("button", { name: "进入更新管理", exact: true }).click();
  await page.getByRole("button", { name: "检查更新", exact: true }).click();
  await page.getByText("已是最新", { exact: true }).waitFor({ state: "visible", timeout: 5000 });
  const updateCheckCompleted = await page.getByText("已是最新", { exact: true }).isVisible();
  const safeUpdateStillDisabled = await page.getByRole("button", { name: "开始安全更新" }).isDisabled();
  await page.getByRole("button", { name: "验证服务器文件", exact: true }).click();
  await page.getByRole("button", { name: "文件验证通过", exact: true }).waitFor({ state: "visible", timeout: 5000 });
  const fileVerificationCompleted = await page.getByRole("button", { name: "文件验证通过", exact: true }).isVisible();

  await page.getByRole("tab", { name: "自动任务" }).click();
  await page.waitForURL(/\/server\/tasks/);
  await page.getByRole("button", { name: /已暂停 1/ }).click();
  const pausedTaskVisible = await page.getByRole("cell", { name: "定时安全重启", exact: true }).isVisible();
  await page.getByRole("switch", { name: "启用定时安全重启" }).click();
  const pausedCountUpdated = await page.getByLabel("自动任务摘要")
    .getByText("0", { exact: true }).isVisible();
  await page.getByRole("button", { name: "全部 6" }).click();
  await page.getByRole("button", { name: "编辑", exact: true }).first().click();
  const editTaskDialogVisible = await page.getByRole("dialog").getByText("编辑任务", { exact: true }).isVisible();
  await page.getByRole("button", { name: "保存修改" }).click();
  await page.getByRole("button", { name: "新建任务" }).click();
  const newTaskDialogVisible = await page.getByRole("dialog").getByText("新建任务", { exact: true }).isVisible();
  await page.getByRole("combobox", { name: "任务类型" }).selectOption("broadcast");
  await page.getByRole("button", { name: "创建任务" }).click();
  const newTaskCountVisible = await page.getByText("任务总数").locator("..").getByText("7", { exact: true }).isVisible();

  report.checks.push({
    page: "interactions",
    viewport: viewport.name,
    restartDialogVisible,
    dangerDialogVisible,
    rangeSelected,
    eligibleFilterSelected,
    filteredSectorVisible,
    sortSelected,
    savedStrategyVisible,
    unloadDialogVisible,
    pageScrollWorks: scrollCheck.scrollHeight <= viewport.height || scrollCheck.after > scrollCheck.before,
    serverManagementNavVisible,
    updateCheckCompleted,
    safeUpdateStillDisabled,
    fileVerificationCompleted,
    pausedTaskVisible,
    pausedCountUpdated,
    editTaskDialogVisible,
    newTaskDialogVisible,
    newTaskCountVisible,
  });

  if (viewport.name === "1920x1080") {
    await page.goto(`${baseUrl}/server/control`, { waitUntil: "networkidle" });
    await page.getByRole("button", { name: "安全关闭" }).click();
    await page.getByRole("button", { name: "确认安全关闭" }).click();
    await page.getByText("服务器当前已停止").waitFor({ state: "visible", timeout: 5000 });
    const stoppedStateVisible = await page.getByText("服务器当前已停止").isVisible();
    const rconDisconnected = await page.getByText("未连接", { exact: true }).isVisible();
    await page.getByRole("button", { name: "启动服务器" }).click();
    await page.getByText("正常运行", { exact: true }).first().waitFor({ state: "visible", timeout: 5000 });
    const restartedStateVisible = await page.getByText("正常运行", { exact: true }).first().isVisible();

    report.checks.push({
      page: "lifecycle-smoke",
      viewport: viewport.name,
      stoppedStateVisible,
      rconDisconnected,
      restartedStateVisible,
    });

    await page.goto(`${baseUrl}/server/memory`, { waitUntil: "networkidle" });
    await page.getByRole("button", { name: "安全尝试卸载 2 个星区" }).click();
    await page.getByRole("button", { name: "确认安全尝试卸载" }).click();
    await page.getByText("已卸载", { exact: true }).first().waitFor({ state: "visible", timeout: 5000 });
    const unloadCompleted = await page.getByText("已卸载", { exact: true }).first().isVisible();
    const loadedCountUpdated = await page.locator(".memory-summary-item").last().getByText("10", { exact: true }).isVisible();

    report.checks.push({
      page: "memory-unload-smoke",
      viewport: viewport.name,
      unloadCompleted,
      loadedCountUpdated,
    });
  }

  await context.close();
}

await browser.close();
fs.writeFileSync(path.join(projectRoot, "qa", "visual-qa-report.json"), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
