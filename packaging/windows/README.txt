OrionAdmin Windows x64 portable package

1. Extract or copy this entire directory to a normal local folder.
2. Run Start-OrionAdmin.cmd.
3. The browser opens http://127.0.0.1:5088/server/control after the API is ready.
4. Runtime state and the management database are stored under data\.

This package is self-contained: the target computer does not need Node.js or .NET.
It listens only on 127.0.0.1 by default and does not open Windows Firewall.
Keep the launcher window open. If an Avorion game server is running, use the UI's safe shutdown first, then press Ctrl+C to stop OrionAdmin.
Do not copy data\ between computers unless you intentionally migrate management state and have separately backed up the Galaxy.
