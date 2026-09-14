const fs = require("node:fs");
const path = require("node:path");
const { chromium } = require("C:/Users/wager/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright");

const outputDirectory = "E:/OrionAdmin/.e2e";

async function capture(browser, width, height, inspectConfirmation) {
  const page = await browser.newPage({ viewport: { width, height }, deviceScaleFactor: 1 });
  const consoleErrors = [];
  const pageErrors = [];
  page.on("console", (message) => { if (message.type() === "error") consoleErrors.push(message.text()); });
  page.on("pageerror", (error) => pageErrors.push(String(error)));
  await page.goto("http://127.0.0.1:4173/alliances", { waitUntil: "networkidle", timeout: 60_000 });
  await page.getByRole("button", { name: "资产与发放" }).click();
  const dialog = page.getByRole("dialog").filter({ hasText: "联盟资产" });
  await dialog.waitFor({ state: "visible", timeout: 15_000 });
  const inventory = dialog.locator(".inventory-panel");
  await inventory.waitFor({ state: "visible", timeout: 15_000 });
  await page.waitForTimeout(1_000);
  await inventory.scrollIntoViewIfNeeded();
  const body = await inventory.innerText();
  const output = path.join(outputDirectory, `inventory-alliance-${width}x${height}.png`);
  await page.screenshot({ path: output });

  let confirmOutput = null;
  let confirmationText = "";
  if (inspectConfirmation) {
    await inventory.getByRole("button", { name: "核对发放" }).click();
    const confirmDialog = page.getByRole("dialog").filter({ hasText: "确认发放系统插件" });
    await confirmDialog.waitFor({ state: "visible", timeout: 5_000 });
    confirmationText = await confirmDialog.innerText();
    confirmOutput = path.join(outputDirectory, `inventory-confirm-${width}x${height}.png`);
    await page.screenshot({ path: confirmOutput });
    await confirmDialog.getByRole("button", { name: "取消" }).click();
  }

  const result = {
    viewport: `${width}x${height}`,
    output,
    confirmOutput,
    hasLiveItem: body.includes("Cargo Extension") && body.includes("槽位 #0"),
    hasCapacity: /1\s*\/\s*1000/.test(body),
    confirmationNamesOwner: !inspectConfirmation || confirmationText.includes("联盟 1（#5）"),
    confirmationSaysPermanentOne: !inspectConfirmation || confirmationText.includes("永久加入 1 个"),
    horizontalOverflow: await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth),
    dialogInViewport: await dialog.evaluate((element) => {
      const box = element.getBoundingClientRect();
      return box.left >= 0 && box.right <= window.innerWidth && box.top >= 0 && box.bottom <= window.innerHeight;
    }),
    consoleErrors,
    pageErrors,
  };
  await page.close();
  return result;
}

(async () => {
  fs.mkdirSync(outputDirectory, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    executablePath: "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  });
  try {
    const results = [
      await capture(browser, 1440, 1000, true),
      await capture(browser, 1920, 1080, false),
    ];
    console.log(JSON.stringify(results, null, 2));
    if (results.some((result) => !result.hasLiveItem || !result.hasCapacity ||
        !result.confirmationNamesOwner || !result.confirmationSaysPermanentOne ||
        result.horizontalOverflow || !result.dialogInViewport || result.consoleErrors.length || result.pageErrors.length)) {
      process.exitCode = 1;
    }
  } finally {
    await browser.close();
  }
})().catch((error) => { console.error(error); process.exitCode = 1; });
