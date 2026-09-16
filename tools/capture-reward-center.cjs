const fs = require("node:fs");
const path = require("node:path");
const { launchQaBrowser } = require("./qa-browser-runtime.cjs");

const projectRoot = path.resolve(__dirname, "..");
const outputDirectory = path.join(projectRoot, ".e2e");

async function capture(browser, width, height, confirm = false) {
  const page = await browser.newPage({ viewport: { width, height }, deviceScaleFactor: 1 });
  const consoleErrors = [];
  const pageErrors = [];
  page.on("console", (message) => { if (message.type() === "error") consoleErrors.push(message.text()); });
  page.on("pageerror", (error) => pageErrors.push(String(error)));
  await page.goto("http://127.0.0.1:4173/game/rewards", { waitUntil: "networkidle", timeout: 60_000 });
  await page.getByRole("heading", { name: "游戏管理" }).waitFor({ state: "visible", timeout: 15_000 });
  await page.getByPlaceholder("输入玩家名称或索引，添加到批次……").fill("超人");
  await page.getByRole("button", { name: /超人特工队/ }).click();
  const selectedTargetText = await page.locator(".reward-targets").innerText();

  await page.getByRole("button", { name: "直接到账" }).click();
  const mailHiddenInDirectMode = await page.getByText("邮件标题", { exact: true }).count() === 0;
  await page.getByRole("button", { name: "游戏内邮件" }).click();
  await page.getByText("邮件标题", { exact: true }).waitFor({ state: "visible" });

  const output = path.join(outputDirectory, `reward-center-${width}x${height}.png`);
  await page.screenshot({ path: output, fullPage: false });
  let confirmOutput = null;
  let confirmationText = "";
  if (confirm) {
    await page.getByRole("button", { name: "核对并创建批次" }).click();
    const dialog = page.getByRole("dialog").filter({ hasText: "确认创建奖励批次" });
    await dialog.waitFor({ state: "visible", timeout: 5_000 });
    confirmationText = await dialog.innerText();
    confirmOutput = path.join(outputDirectory, `reward-center-confirm-${width}x${height}.png`);
    await page.screenshot({ path: confirmOutput, fullPage: false });
    await dialog.getByRole("button", { name: "取消" }).click();
  }

  const metrics = await page.evaluate(() => ({
    horizontalOverflow: document.documentElement.scrollWidth > window.innerWidth,
    viewportWidth: window.innerWidth,
    viewportHeight: window.innerHeight,
    visibleHistoryCards: document.querySelectorAll(".reward-history article").length,
    submitEnabled: !document.querySelector(".reward-submit-bar button")?.disabled,
  }));
  await page.close();
  return {
    viewport: `${width}x${height}`,
    output,
    confirmOutput,
    mailHiddenInDirectMode,
    selectedTargetUsesIdentityOnly: selectedTargetText.includes("超人特工队") &&
      !selectedTargetText.includes("在线") && !selectedTargetText.includes("位置") && !selectedTargetText.includes("资产"),
    confirmationHasTargetCount: !confirm || confirmationText.includes("1 名玩家"),
    confirmationHasAssets: !confirm || confirmationText.includes("25,000 Credits") && confirmationText.includes("2,500 钛"),
    consoleErrors,
    pageErrors,
    ...metrics,
  };
}

(async () => {
  fs.mkdirSync(outputDirectory, { recursive: true });
  const browser = await launchQaBrowser({
    headless: true
  });
  try {
    const results = [
      await capture(browser, 1440, 900, true),
      await capture(browser, 1920, 1080, false),
    ];
    console.log(JSON.stringify(results, null, 2));
    if (results.some((result) => result.horizontalOverflow || !result.mailHiddenInDirectMode ||
        !result.selectedTargetUsesIdentityOnly || !result.confirmationHasTargetCount ||
        !result.confirmationHasAssets || !result.submitEnabled || result.visibleHistoryCards < 1 ||
        result.consoleErrors.length || result.pageErrors.length)) process.exitCode = 1;
  } finally {
    await browser.close();
  }
})().catch((error) => { console.error(error); process.exitCode = 1; });
