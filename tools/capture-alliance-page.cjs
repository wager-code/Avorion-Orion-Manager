const fs = require("node:fs");
const path = require("node:path");
const { chromium } = require("C:/Users/wager/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright");

const projectRoot = "E:/OrionAdmin";
const outputDirectory = path.join(projectRoot, ".e2e");
const sourcePath = path.join(projectRoot, "references", "联盟管理_方案2_已确认.png");
const implementationPath = path.join(outputDirectory, "alliance-management-implementation-1672x941.png");
const comparisonPath = path.join(outputDirectory, "alliance-management-comparison-full.png");
const focusedComparisonPath = path.join(outputDirectory, "alliance-management-comparison-focused.png");

async function waitForImages(page) {
  await page.waitForFunction(() => Array.from(document.images).every((image) => image.complete));
}

(async () => {
  fs.mkdirSync(outputDirectory, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    executablePath: "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  });
  const page = await browser.newPage({ viewport: { width: 1672, height: 941 }, deviceScaleFactor: 1 });
  const consoleErrors = [];
  const pageErrors = [];
  page.on("console", (message) => { if (message.type() === "error") consoleErrors.push(message.text()); });
  page.on("pageerror", (error) => pageErrors.push(String(error)));

  await page.goto("http://127.0.0.1:4173/alliances", { waitUntil: "networkidle", timeout: 60_000 });
  await page.waitForTimeout(4_500);
  await page.screenshot({ path: implementationPath });

  const initialBody = await page.locator("body").innerText();
  const allianceRows = await page.locator(".alliances-list__row").count();
  const memberRows = await page.locator(".alliance-members tbody tr").count();
  const refreshButton = page.getByRole("button", { name: /刷新/ });
  if (await refreshButton.count()) {
    await refreshButton.click();
    await page.waitForTimeout(1_000);
  }

  let memberDetailOpened = false;
  const memberButton = page.getByRole("button", { name: "查看玩家" });
  if (await memberButton.count()) {
    await memberButton.first().click();
    await page.getByRole("dialog").waitFor({ state: "visible", timeout: 5_000 });
    memberDetailOpened = true;
    await page.keyboard.press("Escape");
  }

  let navigationRoundTrip = false;
  const playerNavigation = page.getByText("玩家管理", { exact: true });
  if (await playerNavigation.count()) {
    await playerNavigation.first().click();
    await page.waitForURL(/\/players$/, { timeout: 5_000 });
    const allianceNavigation = page.getByText("联盟管理", { exact: true });
    await allianceNavigation.first().click();
    await page.waitForURL(/\/alliances$/, { timeout: 5_000 });
    navigationRoundTrip = true;
  }

  const responsiveChecks = [];
  for (const viewport of [{ width: 1366, height: 768 }, { width: 1024, height: 768 }, { width: 768, height: 900 }]) {
    const responsivePage = await browser.newPage({ viewport, deviceScaleFactor: 1 });
    const errors = [];
    responsivePage.on("console", (message) => { if (message.type() === "error") errors.push(message.text()); });
    responsivePage.on("pageerror", (error) => errors.push(String(error)));
    await responsivePage.goto("http://127.0.0.1:4173/alliances", { waitUntil: "networkidle", timeout: 60_000 });
    await responsivePage.waitForTimeout(1_200);
    responsiveChecks.push({
      viewport: `${viewport.width}x${viewport.height}`,
      bodyOverflow: await responsivePage.evaluate(() => document.documentElement.scrollWidth > window.innerWidth),
      errors,
    });
    await responsivePage.close();
  }

  const comparison = await browser.newPage({ viewport: { width: 3344, height: 997 }, deviceScaleFactor: 1 });
  const sourceUrl = `data:image/png;base64,${fs.readFileSync(sourcePath).toString("base64")}`;
  const implementationUrl = `data:image/png;base64,${fs.readFileSync(implementationPath).toString("base64")}`;
  await comparison.setContent(`<!doctype html><style>
    *{box-sizing:border-box}body{margin:0;background:#0c1524;color:#fff;font-family:Segoe UI,sans-serif}
    main{display:grid;grid-template-columns:1672px 1672px}.panel{position:relative;padding-top:56px;background:#eef3f9}
    .label{position:absolute;inset:0 0 auto;height:56px;display:flex;align-items:center;padding:0 24px;background:#0c1d35;font-size:20px;font-weight:700}
    img{display:block;width:1672px;height:941px;object-fit:fill}
  </style><main><section class="panel"><div class="label">视觉基准：联盟管理方案 2</div><img src="${sourceUrl}"></section><section class="panel"><div class="label">当前实现：真实联盟数据</div><img src="${implementationUrl}"></section></main>`);
  await waitForImages(comparison);
  await comparison.screenshot({ path: comparisonPath });

  const focused = await browser.newPage({ viewport: { width: 2400, height: 700 }, deviceScaleFactor: 1 });
  await focused.setContent(`<!doctype html><style>
    *{box-sizing:border-box}body{margin:0;background:#0c1524;color:#fff;font-family:Segoe UI,sans-serif}
    main{display:grid;grid-template-columns:1fr 1fr;gap:2px}.panel{position:relative;padding-top:52px}
    .label{position:absolute;inset:0 0 auto;height:52px;display:flex;align-items:center;padding:0 20px;background:#0c1d35;font-size:18px;font-weight:700}
    .crop{height:648px;background-repeat:no-repeat;background-size:1672px 941px;background-position:-264px -78px;background-color:#eef3f9}
  </style><main><section class="panel"><div class="label">方案 2 · 主工作区</div><div class="crop" style="background-image:url('${sourceUrl}')"></div></section><section class="panel"><div class="label">当前实现 · 主工作区</div><div class="crop" style="background-image:url('${implementationUrl}')"></div></section></main>`);
  await focused.waitForTimeout(500);
  await focused.screenshot({ path: focusedComparisonPath });

  console.log(JSON.stringify({
    url: page.url(),
    title: await page.title(),
    allianceRows,
    memberRows,
    memberDetailOpened,
    navigationRoundTrip,
    allianceVisibleInitially: initialBody.includes("联盟列表") && initialBody.includes("超人特工队"),
    hasLiveConnection: initialBody.includes("实时连接"),
    hasRealSource: initialBody.includes("只读实时数据"),
    consoleErrors,
    pageErrors,
    responsiveChecks,
    implementationPath,
    comparisonPath,
    focusedComparisonPath,
  }, null, 2));
  await browser.close();
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
