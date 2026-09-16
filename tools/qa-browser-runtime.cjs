const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

function loadPlaywright() {
  const candidates = [
    process.env.QA_PLAYWRIGHT_PATH,
    "playwright",
    path.join(
      os.homedir(),
      ".cache",
      "codex-runtimes",
      "codex-primary-runtime",
      "dependencies",
      "node",
      "node_modules",
      "playwright",
    ),
  ].filter(Boolean);

  const failures = [];
  for (const candidate of candidates) {
    try {
      const module = require(candidate);
      if (module?.chromium) return module;
      failures.push(`${candidate}: module does not export chromium`);
    } catch (error) {
      failures.push(`${candidate}: ${error instanceof Error ? error.message : String(error)}`);
    }
  }

  throw new Error(
    "Playwright is unavailable. Install it in the workspace or set QA_PLAYWRIGHT_PATH. " +
    failures.join(" | "),
  );
}

function browserExecutablePath() {
  const explicit = process.env.QA_BROWSER_EXECUTABLE;
  if (explicit) {
    if (!fs.existsSync(explicit)) {
      throw new Error(`QA_BROWSER_EXECUTABLE does not exist: ${explicit}`);
    }
    return explicit;
  }

  const candidates = process.platform === "win32"
    ? [
        process.env.PROGRAMFILES && path.join(process.env.PROGRAMFILES, "Google", "Chrome", "Application", "chrome.exe"),
        process.env["PROGRAMFILES(X86)"] && path.join(process.env["PROGRAMFILES(X86)"], "Microsoft", "Edge", "Application", "msedge.exe"),
        process.env.LOCALAPPDATA && path.join(process.env.LOCALAPPDATA, "Google", "Chrome", "Application", "chrome.exe"),
        process.env.PROGRAMFILES && path.join(process.env.PROGRAMFILES, "Microsoft", "Edge", "Application", "msedge.exe"),
      ]
    : [
        "/usr/bin/google-chrome",
        "/usr/bin/google-chrome-stable",
        "/usr/bin/chromium",
        "/usr/bin/chromium-browser",
      ];

  return candidates.filter(Boolean).find(candidate => fs.existsSync(candidate));
}

async function launchQaBrowser(options = {}) {
  const { chromium } = loadPlaywright();
  const executablePath = browserExecutablePath();
  return chromium.launch(executablePath ? { ...options, executablePath } : options);
}

module.exports = { launchQaBrowser };
