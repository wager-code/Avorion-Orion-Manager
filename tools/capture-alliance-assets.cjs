const fs = require("node:fs");
const path = require("node:path");
const { chromium } = require("C:/Users/wager/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright");

const outputDirectory = "E:/OrionAdmin/.e2e";

async function capture(browser, width, height, confirm = false) {
  const page = await browser.newPage({ viewport: { width, height }, deviceScaleFactor: 1 });
  const consoleErrors = [];
  const pageErrors = [];
  page.on("console", (message) => { if (message.type() === "error") consoleErrors.push(message.text()); });
  page.on("pageerror", (error) => pageErrors.push(String(error)));
  await page.goto("http://127.0.0.1:4173/alliances", { waitUntil: "networkidle", timeout: 60_000 });
  const assetsButton = page.getByRole("button", { name: "资产与发放" });
  await assetsButton.waitFor({ state: "visible", timeout: 15_000 });
  await assetsButton.click();
  const assetsDialog = page.getByRole("dialog").filter({ hasText: "联盟资产" });
  await assetsDialog.waitFor({ state: "visible", timeout: 15_000 });
  await assetsDialog.getByText("Credits", { exact: true }).waitFor({ state: "visible", timeout: 15_000 });
  await assetsDialog.getByRole("button", { name: "发放奖励" }).click();
  await assetsDialog.getByLabel("联盟奖励数量").fill("2500");
  const bodyText = await assetsDialog.innerText();
  const output = path.join(outputDirectory, `alliance-assets-${width}x${height}.png`);
  await page.screenshot({ path: output });

  let confirmationText = "";
  let confirmOutput = null;
  if (confirm) {
    await assetsDialog.getByRole("button", { name: "核对发放" }).click();
    const confirmDialog = page.getByRole("dialog").filter({ hasText: "确认发放联盟奖励" });
    await confirmDialog.waitFor({ state: "visible", timeout: 5_000 });
    confirmationText = await confirmDialog.innerText();
    confirmOutput = path.join(outputDirectory, `alliance-assets-confirm-${width}x${height}.png`);
    await page.screenshot({ path: confirmOutput });
  }

  const metrics = await page.evaluate(() => ({
    horizontalOverflow: document.documentElement.scrollWidth > window.innerWidth,
    viewportWidth: window.innerWidth,
    viewportHeight: window.innerHeight,
  }));
  await page.close();
  return {
    viewport: `${width}x${height}`,
    output,
    confirmOutput,
    hasRealBalances: bodyText.includes("Credits") && bodyText.includes("钛") && bodyText.includes("OrionAdminBridge 0.7.0"),
    hasLimits: bodyText.includes("10 亿 Credits") && bodyText.includes("不允许负数或自动重试"),
    confirmationNamesTarget: !confirm || /联盟 1（#5）/.test(confirmationText),
    confirmationHasAmount: !confirm || confirmationText.includes("2,500 Credits"),
    consoleErrors,
    pageErrors,
    ...metrics,
  };
}

(async () => {
  fs.mkdirSync(outputDirectory, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    executablePath: "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  });
  try {
    const results = [];
    results.push(await capture(browser, 1440, 900, true));
    results.push(await capture(browser, 1920, 1080, false));
    console.log(JSON.stringify(results, null, 2));
    if (results.some((result) => !result.hasRealBalances || !result.hasLimits ||
        !result.confirmationNamesTarget || !result.confirmationHasAmount || result.horizontalOverflow ||
        result.consoleErrors.length || result.pageErrors.length)) process.exitCode = 1;
  } finally {
    await browser.close();
  }
})().catch((error) => { console.error(error); process.exitCode = 1; });
