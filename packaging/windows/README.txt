OrionAdmin Windows x64 package

Portable mode (no administrator rights)
1. Extract or copy this entire directory to a normal local folder.
2. Run Start-OrionAdmin.cmd.
3. The browser opens http://127.0.0.1:5088/server/control after the API is ready.
4. Runtime state and the management database are stored under this package's data\ directory.
5. Before Ctrl+C, safely stop any running Avorion game server in the UI.

Windows service mode (administrator rights required)
1. Open PowerShell as administrator in this directory.
2. Run:
   powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-OrionAdminService.ps1
3. OrionAdmin starts automatically (delayed) with Windows and listens only on 127.0.0.1:5088.
4. Service-mode state is stored outside the package at %ProgramData%\OrionAdmin\data.
5. Logs are available in Event Viewer > Windows Logs > Application (Source: OrionAdmin).

Safe uninstall or upgrade
1. If Avorion is running, use OrionAdmin's safe shutdown and wait until the UI reports stopped.
2. Run as administrator:
   powershell -NoProfile -ExecutionPolicy Bypass -File .\Uninstall-OrionAdminService.ps1
3. The script gracefully stops OrionAdmin, removes only the service registration, and preserves %ProgramData%\OrionAdmin\data.
4. Extract the new package to a new normal folder, then run its Install-OrionAdminService.ps1. The preserved service data is reused.
5. If the API cannot be reached, the uninstall script refuses to stop the service. Only after independently verifying the game server is stopped may you add -ConfirmGameServerStopped.

This package is self-contained: the target computer does not need Node.js or .NET.
Neither mode opens Windows Firewall or exposes OrionAdmin beyond localhost.
Do not copy portable data\ or ProgramData state between computers unless you intentionally migrate management state and have separately backed up the Galaxy.
