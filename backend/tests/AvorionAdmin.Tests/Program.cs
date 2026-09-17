using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using AvorionAdmin.Agent;
using AvorionAdmin.Core;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Extensions.Options;

var failures = new List<string>();
var root = Path.Combine(Path.GetTempPath(), $"avorion-admin-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
ManagedServerProcessRegistry? initializationRegistryForCleanup = null;
string? fakeServerExecutableForCleanup = null;

try
{
    OpenApiContractChecks.Run(Check);
    ManagementBridgeChecks.Run(Check);
    await ManagementBridgeChecks.CheckTransportAsync(Check);
    var galaxy = Directory.CreateDirectory(Path.Combine(root, "MyGalaxy")).FullName;
    var backups = Directory.CreateDirectory(Path.Combine(root, "backups")).FullName;
    var logs = Directory.CreateDirectory(Path.Combine(root, "logs")).FullName;
    var data = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
    var browseParent = Directory.CreateDirectory(Path.Combine(root, "browse-parent")).FullName;
    var browseChild = Directory.CreateDirectory(Path.Combine(browseParent, "child-folder")).FullName;
    var steamCmdDirectory = Directory.CreateDirectory(Path.Combine(root, "SteamCMD")).FullName;
    var steamCmdExecutable = Path.Combine(steamCmdDirectory, "steamcmd.exe");
    var serverDirectory = Directory.CreateDirectory(Path.Combine(root, "AvorionServer")).FullName;
    var serverExecutable = Path.Combine(serverDirectory, "AvorionServer.exe");
    var clientDirectory = Directory.CreateDirectory(Path.Combine(root, "AvorionClient")).FullName;
    var clientIconDirectory = Directory.CreateDirectory(Path.Combine(clientDirectory, "data", "textures", "icons")).FullName;

    await File.WriteAllTextAsync(Path.Combine(galaxy, "save-state.dat"), "real test fixture");
    await File.WriteAllBytesAsync(Path.Combine(backups, "backup-20260830.bak"), new byte[1_024]);
    await File.WriteAllLinesAsync(Path.Combine(logs, "server.log"),
    [
        "2026-08-30 10:00:00 Save completed",
        "2026-08-30 10:00:01 Player TestPlayer joined",
        "2026-08-30 10:00:02 MOD Example error: failed to load"
    ]);
    var peFixture = new byte[70 * 1024];
    peFixture[0] = (byte)'M';
    peFixture[1] = (byte)'Z';
    await File.WriteAllBytesAsync(steamCmdExecutable, peFixture);
    await File.WriteAllBytesAsync(serverExecutable, peFixture);
    await File.WriteAllBytesAsync(Path.Combine(clientIconDirectory, "chaingun.png"), [137, 80, 78, 71, 13, 10, 26, 10, 0]);
    await File.WriteAllTextAsync(Path.Combine(clientIconDirectory, "invalid.png"), "not an image");
    var serverRunnerExecutable = Path.Combine(serverDirectory, "ServerRunner.exe");
    await File.WriteAllBytesAsync(serverRunnerExecutable, peFixture);
    var steamAppsDirectory = Directory.CreateDirectory(Path.Combine(steamCmdDirectory, "steamapps")).FullName;
    var manifestPath = Path.Combine(steamAppsDirectory, "appmanifest_565060.acf");
    await File.WriteAllTextAsync(manifestPath, "\"AppState\" { \"appid\" \"565060\" \"buildid\" \"12345678\" }");

    var options = Options.Create(new ServerNodeOptions
    {
        ServerId = "test",
        Name = "测试节点",
        ProcessName = Process.GetCurrentProcess().ProcessName.Split('.')[0],
        ProcessId = Environment.ProcessId,
        GalaxyPath = galaxy,
        SavePath = galaxy,
        BackupPath = backups,
        LogPath = logs,
        DataDirectory = data,
        SteamCmdPath = steamCmdExecutable,
        ExecutablePath = serverExecutable,
        ClientDirectory = clientDirectory,
        DiskWarningAvailableBytes = 1,
        MemoryWarningBytes = long.MaxValue
    });

    var runtimePaths = new ServerRuntimePathResolver(options);
    ISteamQueryProbe steamQueryProbe = new StaticSteamQueryProbe();
    IServerProbe probe = new SystemProbe(options, steamQueryProbe);
    var backupScanner = new BackupScanner(runtimePaths);
    ILogReader logReader = new LogReader(runtimePaths);
    var sqlite = new SqliteStore(options);
    IPerformanceStore performanceStore = sqlite;
    IOperationStore operationStore = sqlite;
    IUpdateEnvironmentStore updateEnvironmentStore = sqlite;
    IServerSetupDraftStore serverSetupDraftStore = sqlite;
    IAutomationTaskStore automationTaskStore = sqlite;
    IMemoryPolicyStore memoryPolicyStore = sqlite;
    await performanceStore.InitializeAsync();
    await operationStore.InitializeAsync();
    await automationTaskStore.InitializeAsync();

    IInventoryCatalogService inventoryCatalog = new InventoryCatalogService(options);
    var catalog = await inventoryCatalog.QueryAsync(new InventoryCatalogQuery(null, null, null, 0, 100));
    Check(catalog.Total == 58 && catalog.Items.Count == 58,
        "inventory catalog must expose the 19 verified turret types and 39 vanilla system scripts without pagination loss");
    Check(catalog.Items.Count(item => item.ItemType == "turret") == 19 &&
          catalog.Items.Count(item => item.ItemType == "system-upgrade") == 39,
        "inventory catalog must preserve distinct turret and system-upgrade types");
    Check(catalog.Items.Count(item => item.GrantPolicy == InventoryGrantPolicies.VerifiedGrantable) == 11 &&
          catalog.Items.Count(item => item.GrantPolicy == InventoryGrantPolicies.CatalogOnly) == 33 &&
          catalog.Items.Count(item => item.GrantPolicy == InventoryGrantPolicies.StoryBlocked) == 14,
        "inventory catalog must fail closed outside the 11 already verified system-upgrade grants");
    Check(catalog.IndexedIconCount == 1 && catalog.Items.Single(item => item.Key == "chain-gun").IconAvailable,
        "icon discovery must index only files with an allowed image signature");
    var chainGunIcon = await inventoryCatalog.ReadIconAsync("data/textures/icons/chaingun.png");
    Check(chainGunIcon is { ContentType: "image/png" } && chainGunIcon.ETag.Length == 64,
        "catalog icon reads must return verified content type and a stable SHA-256 ETag");
    Check(await inventoryCatalog.ReadIconAsync("data/textures/icons/../invalid.png") is null &&
          await inventoryCatalog.ReadIconAsync("data/textures/icons/invalid.png") is null,
        "catalog icon reads must reject traversal and invalid image payloads");
    var turretSearch = await inventoryCatalog.QueryAsync(new InventoryCatalogQuery("机枪", "turret", null, 0, 100));
    Check(turretSearch.Total == 2 && turretSearch.Items.All(item => item.ItemType == "turret"),
        "inventory catalog search and type filtering must remain deterministic");

    var saveTaskRequest = new AutomationTaskUpsertRequest("scheduled-save", "interval", 30, null, null, false);
    Check(AutomationSchedule.Validate(saveTaskRequest) is null, "verified scheduled save should pass constrained schedule validation");
    Check(AutomationSchedule.Validate(saveTaskRequest with { IntervalMinutes = 17 }) is not null,
        "arbitrary automatic task intervals must be rejected");
    var weeklyRestart = new AutomationTaskUpsertRequest("scheduled-safe-restart", "weekly", null, 1, "05:00", true);
    var weeklyNext = AutomationSchedule.NextRun(weeklyRestart, new DateTimeOffset(2026, 9, 6, 21, 0, 0, TimeSpan.FromHours(8)));
    Check(weeklyNext.ToOffset(TimeSpan.FromHours(8)) == new DateTimeOffset(2026, 9, 7, 5, 0, 0, TimeSpan.FromHours(8)),
        "weekly automatic restart should compute the next China Standard Time occurrence exactly");

    var createdTask = await automationTaskStore.CreateAsync(
        "task_test", "task-test-idempotency", "same-request-hash", saveTaskRequest,
        DateTimeOffset.UtcNow.AddMinutes(30));
    var repeatedTask = await automationTaskStore.CreateAsync(
        "task_other", "task-test-idempotency", "same-request-hash", saveTaskRequest,
        DateTimeOffset.UtcNow.AddMinutes(30));
    Check(createdTask.TaskId == repeatedTask.TaskId, "automatic task creation must be idempotent for an identical key and request");
    var conflictingTaskRejected = false;
    try
    {
        await automationTaskStore.CreateAsync(
            "task_conflict", "task-test-idempotency", "different-request-hash", weeklyRestart,
            DateTimeOffset.UtcNow.AddDays(1));
    }
    catch (IdempotencyConflictException)
    {
        conflictingTaskRejected = true;
    }
    Check(conflictingTaskRejected, "automatic task creation must reject reuse of an idempotency key for a different request");

    var dueAt = DateTimeOffset.UtcNow.AddMinutes(-1);
    await automationTaskStore.UpdateAsync("task_test", saveTaskRequest with { Enabled = true }, dueAt);
    var dueTasks = await automationTaskStore.ClaimDueAsync(DateTimeOffset.UtcNow, 8);
    Check(dueTasks.Count == 1 && dueTasks[0].Task.TaskId == "task_test", "enabled due automatic task must be claimed exactly once");
    var afterClaim = await automationTaskStore.GetAsync("task_test");
    Check(afterClaim is not null && afterClaim.NextRunAt > DateTimeOffset.UtcNow,
        "claiming an automatic task must persist a future next-run time before execution");
    Check(await automationTaskStore.DeleteAsync("task_test"), "automatic tasks must be removable from the persistent scheduler");
    Check(await automationTaskStore.GetAsync("task_test") is null, "deleted automatic tasks must no longer be returned");

    var memoryPolicy = await memoryPolicyStore.SaveMemoryPolicyAsync(new MemoryPolicyUpdateRequest(
        8L * 1024 * 1024 * 1024, true, false, false));
    var loadedMemoryPolicy = await memoryPolicyStore.GetMemoryPolicyAsync();
    Check(loadedMemoryPolicy == memoryPolicy, "memory alert policy must round-trip through SQLite without browser-only state");

    var status = await probe.GetStatusAsync();
    Check(status.Lifecycle == Lifecycle.Running, "real process probe should detect the running test process");
    Check(status.ProcessId is not null, "process id should be real, not a default value");
    Check(status.LastSave.At is not null && status.LastSave.Source == "filesystem", "last save should come from the fixture filesystem");
    Check(status.Rcon.Status == ConnectionState.Unknown, "RCON must stay unknown until the authenticated protocol probe exists");
    Check(status.SteamQuery.Status == ConnectionState.Unknown, "Steam Query must stay unknown until the protocol probe exists");

    await probe.GetPerformanceAsync();
    await Task.Delay(150);
    var performance = await probe.GetPerformanceAsync();
    Check(performance.Memory.WorkingSetBytes is > 0, "working set should be read from the real process");
    Check(performance.Network.Scope == "host", "network scope must be explicit");
    await performanceStore.AppendAsync(new PerformancePoint(
        performance.Provenance.SampledAt,
        performance.Cpu.ProcessPercent,
        performance.Memory.WorkingSetBytes,
        performance.Network.DownloadBytesPerSecond,
        performance.Network.UploadBytesPerSecond,
        null));
    var history = await performanceStore.QueryAsync("1h");
    Check(history.Points.Count == 1 && history.WarningCode == "INSUFFICIENT_HISTORY", "history must report insufficient real samples instead of inventing a trend");
    var sixHourHistory = await performanceStore.QueryAsync("6h");
    Check(sixHourHistory.Range == "6h" && sixHourHistory.Points.Count == 1, "diagnostics must be able to query the confirmed six-hour memory window");

    var backupRecords = await backupScanner.ScanAsync();
    Check(backupRecords.Count == 1, "backup scanner should return exactly the file on disk");
    Check(backupRecords[0].SizeBytes == 1_024, "backup size must match the real file size");
    var resolvedBackup = await backupScanner.ResolveAvailableAsync(backupRecords[0].BackupId);
    Check(resolvedBackup is not null && resolvedBackup.Record.BackupId == backupRecords[0].BackupId &&
          Path.GetFileName(resolvedBackup.Path) == "backup-20260830.bak",
        "backup restore target must resolve only from an exact scanned backup id");
    Check(await backupScanner.ResolveAvailableAsync("backup_not-present") is null,
        "an unknown backup id must fail closed without resolving a filesystem path");

    var logEntries = await logReader.ReadRecentAsync(20);
    Check(logEntries.Count == 3, "log reader should return the three real fixture lines");
    Check(logEntries.Any(entry => entry.Category == "Save"), "save log classification should work");
    Check(logEntries.Any(entry => entry.Category == "Error"), "error log classification should take precedence over MOD classification");
    var originalSaveId = logEntries.Single(entry => entry.RawLine.Contains("Save completed", StringComparison.Ordinal)).Id;
    await File.AppendAllLinesAsync(Path.Combine(logs, "server.log"), ["2026-08-30 10:00:03 Player NewPlayer joined"]);
    var appendedLogEntries = await logReader.ReadRecentAsync(20);
    Check(appendedLogEntries.Single(entry => entry.RawLine.Contains("Save completed", StringComparison.Ordinal)).Id == originalSaveId,
        "existing log entry ids must remain stable when the source file grows");
    await File.AppendAllLinesAsync(Path.Combine(logs, "server.log"),
        Enumerable.Range(0, 350).Select(index => $"2026-08-30 10:01:{index % 60:00} RCON poll {index}"));
    var playerLogEntries = await logReader.ReadRecentAsync(6, "Player");
    Check(playerLogEntries.Count == 2 && playerLogEntries.Any(entry => entry.RawLine.Contains("NewPlayer", StringComparison.Ordinal)),
        "player event filtering must retain real joins beyond the noisy default log tail");

    var managedLogData = Directory.CreateDirectory(Path.Combine(root, "managed-log-data")).FullName;
    var managedGalaxy = Directory.CreateDirectory(Path.Combine(root, "managed-log-galaxy")).FullName;
    var managedBackupDirectory = Directory.CreateDirectory(Path.Combine(root, "managed-backups")).FullName;
    await File.WriteAllLinesAsync(Path.Combine(managedGalaxy, "serverlog 2026-08-30 10-00-00.txt"),
        ["2026-08-30 10:00:04 Player ManagedPlayer joined"]);
    await File.WriteAllTextAsync(Path.Combine(managedGalaxy, "server.ini"), $"[System]{Environment.NewLine}backups=true{Environment.NewLine}backupsPath={managedBackupDirectory}");
    await File.WriteAllBytesAsync(Path.Combine(managedBackupDirectory, "managed-auto-backup.bak"), new byte[2_048]);
    await File.WriteAllTextAsync(Path.Combine(managedBackupDirectory, "not-an-avorion-backup.txt"), "must not be scanned");
    var managedDirectory = Directory.CreateDirectory(Path.Combine(managedLogData, "managed")).FullName;
    var managedProfile = new ManagedLaunchProfile(
        1,
        serverExecutable,
        serverDirectory,
        managedGalaxy,
        "existing",
        ["--datapath", Directory.GetParent(managedGalaxy)!.FullName, "--galaxy-name", Path.GetFileName(managedGalaxy),
            "--port", "27000", "--max-players", "10", "--server-name", "Managed Test"],
        "0.0.0.0",
        27003,
        true,
        "127.0.0.1",
        27015,
        false,
        false,
        DateTimeOffset.UtcNow);
    await File.WriteAllTextAsync(
        Path.Combine(managedDirectory, "server-launch-profile.json"),
        System.Text.Json.JsonSerializer.Serialize(managedProfile, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
    var managedLogOptions = Options.Create(new ServerNodeOptions { DataDirectory = managedLogData });
    ILogReader managedLogReader = new LogReader(new ServerRuntimePathResolver(managedLogOptions));
    var managedLogEntries = await managedLogReader.ReadRecentAsync(20);
    Check(managedLogEntries.Count == 1 && managedLogEntries[0].RawLine.Contains("ManagedPlayer", StringComparison.Ordinal),
        "log reader should resolve Avorion serverlog files from the verified managed Galaxy profile without reading server.ini");
    IBackupScanner managedBackupScanner = new BackupScanner(new ServerRuntimePathResolver(managedLogOptions));
    var managedBackupRecords = await managedBackupScanner.ScanAsync();
    Check(managedBackupRecords.Count == 1 && managedBackupRecords[0].SizeBytes == 2_048,
        "backup scanner should resolve backupsPath from the verified managed Galaxy and read only .bak files");

    IDiagnosticService diagnostics = new DiagnosticService(
        probe,
        new StaticManagedControl(Lifecycle.Running),
        performanceStore,
        backupScanner,
        logReader,
        steamQueryProbe,
        updateEnvironmentStore,
        runtimePaths,
        options);
    var diagnosticRun = await diagnostics.RunAsync("diag_test");
    Check(diagnosticRun.Items.Count == 11, "diagnostics must contain the 11 confirmed checks only");
    Check(diagnosticRun.Items.Single(item => item.Key == "rcon").Status == DiagnosticState.Unknown, "unimplemented RCON probe must not be green");
    Check(diagnosticRun.Items.Single(item => item.Key == "last-backup").Status == DiagnosticState.Healthy, "real backup evidence should be healthy");
    Check(diagnosticRun.Items.Single(item => item.Key == "memory-trend").Status == DiagnosticState.Unknown, "insufficient memory samples must not fabricate a healthy trend");
    Check(diagnosticRun.Items.Single(item => item.Key == "mod-errors").Status == DiagnosticState.Warning, "real MOD errors in scanned logs must be reported");

    IFileSystemBrowser fileSystemBrowser = new FileSystemBrowser();
    var browseResult = await fileSystemBrowser.BrowseDirectoriesAsync(browseParent);
    Check(browseResult.CurrentPath == browseParent && browseResult.ParentPath == root, "directory browser should return canonical current and parent paths");
    Check(browseResult.Items.Count == 1 && browseResult.Items[0].Path == browseChild, "directory browser should return real child directories only");

    IUpdateEnvironmentService updateEnvironmentService = new UpdateEnvironmentService(options);
    var detectedEnvironment = await updateEnvironmentService.DetectAsync();
    Check(detectedEnvironment.Status == "complete", "configured real SteamCMD and server files should be detected");
    var environmentValidation = await updateEnvironmentService.ValidateAsync(steamCmdExecutable, serverDirectory);
    Check(environmentValidation.Valid, "existing readable SteamCMD and Avorion server files should validate");
    Check(environmentValidation.Server.ResolvedExecutablePath == serverExecutable, "server validation should resolve the real executable");
    await updateEnvironmentStore.SaveAsync(new UpdateEnvironmentConfiguration(steamCmdExecutable, serverDirectory, environmentValidation.ValidatedAt));
    var savedEnvironment = await updateEnvironmentStore.GetAsync();
    Check(savedEnvironment?.SteamCmdPath == steamCmdExecutable && savedEnvironment.ServerDirectory == serverDirectory, "validated update environment should persist in SQLite");
    Check(UpdateInspectionService.ParseLatestPublicBuildId(
        "\"branches\" { \"public\" { \"buildid\" \"87654321\" } \"beta\" { \"buildid\" \"1\" } }") == "87654321",
        "official SteamCMD app-info parser must select the public branch Build ID");
    var serverBeforeVerification = await File.ReadAllBytesAsync(serverExecutable);
    var runnerBeforeVerification = await File.ReadAllBytesAsync(serverRunnerExecutable);
    IUpdateInspectionService updateInspection = new UpdateInspectionService(
        updateEnvironmentStore,
        updateEnvironmentService,
        new AcceptValveSignature());
    var verification = await updateInspection.VerifyLocalAsync((_, _, _) => Task.CompletedTask);
    Check(verification.Valid && verification.Files.Count == 2 && verification.Files.All(file => file.Sha256.Length == 64),
        "local server verification must return real SHA-256 evidence for both required executables");
    Check(verification.InstalledBuildId == "12345678", "local server verification must read the installed App 565060 Build ID");
    Check(serverBeforeVerification.SequenceEqual(await File.ReadAllBytesAsync(serverExecutable)) &&
          runnerBeforeVerification.SequenceEqual(await File.ReadAllBytesAsync(serverRunnerExecutable)),
        "local server verification must not modify or repair server files");

    var setupDraft = new ServerSetupDraftConfiguration(
        "测试服务器", "TestGalaxy", "new", 10, Path.Combine(root, "galaxies", "TestGalaxy"),
        "0.0.0.0", 27000, 27003, true, 27015, false, true, DateTimeOffset.UtcNow);
    await serverSetupDraftStore.SaveAsync(setupDraft);
    var savedSetupDraft = await serverSetupDraftStore.GetAsync();
    Check(savedSetupDraft?.ServerName == "测试服务器" && savedSetupDraft.GalaxyDirectory == setupDraft.GalaxyDirectory, "non-secret server setup draft should persist in SQLite");
    Check(savedSetupDraft?.InstallManagementMod == true, "explicit OrionAdminBridge installation intent should persist without storing secrets");

    var gamePort = ReserveFreeUdpPort();
    var queryPort = ReserveFreeUdpPort(gamePort);
    var rconTestPort = ReserveFreeTcpPort();
    var setupPreflight = new ServerSetupPreflightService(updateEnvironmentStore, updateEnvironmentService);
    var validPreflight = await setupPreflight.ValidateAsync(new ServerSetupPreflightRequest(
        "测试服务器", "TestGalaxy", "new", 10, Path.Combine(root, "galaxies", "TestGalaxy"),
        "0.0.0.0", gamePort, queryPort, true, rconTestPort, "safe-password-123", false, true));
    Check(validPreflight.Valid && validPreflight.UpdateEnvironmentValid && validPreflight.GalaxyPathValid, "server setup preflight should validate the real saved environment and writable Galaxy ancestor");
    Check(validPreflight.Ports.Count == 3 && validPreflight.Ports.All(port => port.Available), "server setup preflight should read the real host listener tables for all configured protocols");
    Check(validPreflight.Issues.All(issue => issue.Severity != "error"), "OrionAdminBridge installation intent should not invalidate setup preflight");

    using (var occupiedListener = new TcpListener(IPAddress.Loopback, 0))
    {
        occupiedListener.Start();
        var occupiedPort = ((IPEndPoint)occupiedListener.LocalEndpoint).Port;
        var occupiedPreflight = await setupPreflight.ValidateAsync(new ServerSetupPreflightRequest(
            "测试服务器", "TestGalaxy", "new", 10, Path.Combine(root, "galaxies", "TestGalaxy"),
            "127.0.0.1", gamePort, queryPort, true, occupiedPort, "safe-password-123", false, false));
        Check(!occupiedPreflight.Valid && occupiedPreflight.Issues.Any(issue => issue.Code == "PORT_IN_USE"), "preflight must reject a RCON TCP port that is really occupied on the host");
    }

    var passwordPreflight = await setupPreflight.ValidateAsync(new ServerSetupPreflightRequest(
        "测试服务器", "TestGalaxy", "new", 10, Path.Combine(root, "galaxies", "TestGalaxy"),
        "127.0.0.1", gamePort, queryPort, true, rconTestPort, "short", false, false));
    Check(!passwordPreflight.Valid && !passwordPreflight.RconPasswordAccepted, "preflight must reject an unsafe RCON password without persisting or echoing it");

    IServerSetupApplicationService setupApplication = new ServerSetupApplicationService(
        setupPreflight, updateEnvironmentStore, updateEnvironmentService, options);
    var newGalaxyRequest = new ServerSetupPreflightRequest(
        "测试服务器", "TestGalaxy", "new", 10, Path.Combine(root, "galaxies", "TestGalaxy"),
        "0.0.0.0", gamePort, queryPort, true, rconTestPort, "apply-secret-not-persisted-123", false, true);
    var newGalaxyApplication = await setupApplication.ApplyAsync(
        "op_setup_new", newGalaxyRequest, (_, _, _) => Task.CompletedTask);
    Check(newGalaxyApplication.Mode == "launch-profile-ready" && !newGalaxyApplication.ServerIniUpdated,
        "new Galaxy setup should create only the verified launch profile before first initialization");
    var launchProfileBytes = await File.ReadAllBytesAsync(newGalaxyApplication.LaunchProfilePath);
    var launchProfileText = System.Text.Encoding.UTF8.GetString(launchProfileBytes);
    Check(!launchProfileText.Contains("apply-secret-not-persisted-123", StringComparison.Ordinal) &&
          launchProfileText.Contains("--galaxy-name", StringComparison.Ordinal),
        "managed launch profile must contain fixed argument evidence without the RCON secret");

    var serverIniPath = Path.Combine(galaxy, "server.ini");
    const string originalServerIni = "[Game]\r\nSeed=fixture\r\n[Networking]\r\nport=27000\r\nisPublic=true\r\n[Administration]\r\nmaxPlayers=4\r\nname=Old Name\r\n";
    await File.WriteAllTextAsync(serverIniPath, originalServerIni, new System.Text.UTF8Encoding(false));
    var existingGalaxyRequest = new ServerSetupPreflightRequest(
        "已接入服务器", "MyGalaxy", "existing", 12, galaxy,
        "0.0.0.0", gamePort, queryPort, true, rconTestPort, "existing-secret-456", false, false);
    var existingApplication = await setupApplication.ApplyAsync(
        "op_setup_existing", existingGalaxyRequest, (_, _, _) => Task.CompletedTask);
    var appliedIni = await File.ReadAllTextAsync(serverIniPath);
    Check(existingApplication.Mode == "server-ini-updated" && existingApplication.RconConfigured,
        "existing Galaxy setup should atomically update a stopped server.ini and report RCON configured");
    Check(appliedIni.Contains("Seed=fixture", StringComparison.Ordinal) &&
          appliedIni.Contains("name=已接入服务器", StringComparison.Ordinal) &&
          appliedIni.Contains("rconIp=127.0.0.1", StringComparison.Ordinal) &&
          appliedIni.Contains("rconPassword=existing-secret-456", StringComparison.Ordinal),
        "server.ini patch must preserve unrelated settings and bind RCON to loopback with the requested secret");

    var stableProfile = await File.ReadAllBytesAsync(existingApplication.LaunchProfilePath);
    const string ambiguousIni = "[Networking]\r\nport=27000\r\nport=27001\r\n[Administration]\r\nmaxPlayers=10\r\n";
    await File.WriteAllTextAsync(serverIniPath, ambiguousIni, new System.Text.UTF8Encoding(false));
    var ambiguousRejected = false;
    try
    {
        await setupApplication.ApplyAsync(
            "op_setup_ambiguous",
            existingGalaxyRequest with { ServerName = "不应写入", RconPassword = "rollback-secret-789" },
            (_, _, _) => Task.CompletedTask);
    }
    catch (ServerSetupApplicationException exception)
    {
        ambiguousRejected = exception.Code == "SERVER_INI_AMBIGUOUS";
    }
    Check(ambiguousRejected && await File.ReadAllTextAsync(serverIniPath) == ambiguousIni,
        "ambiguous server.ini must be rejected without modifying the original file");
    Check((await File.ReadAllBytesAsync(existingApplication.LaunchProfilePath)).SequenceEqual(stableProfile),
        "a failed server.ini application must roll the managed launch profile back to its prior bytes");

    var bridgePackage = Directory.CreateDirectory(Path.Combine(root, "bridge-package")).FullName;
    Directory.CreateDirectory(Path.Combine(bridgePackage, "data", "scripts", "commands"));
    await File.WriteAllTextAsync(Path.Combine(bridgePackage, "modinfo.lua"), "meta = { version = \"0.10.0\" }");
    await File.WriteAllTextAsync(Path.Combine(bridgePackage, "data", "scripts", "commands", "orionadmin.lua"), "return true");
    IManagementBridgeInstaller bridgeInstaller = new ManagementBridgeInstaller(bridgePackage);
    var bridgeInstallation = await bridgeInstaller.InstallAsync(galaxy, "op_bridge_install");
    var bridgeStatus = await bridgeInstaller.InspectAsync(galaxy);
    Check(bridgeInstallation.Version == "0.10.0" && bridgeInstallation.Configured && bridgeStatus.Current,
        "management bridge installer should atomically publish the packaged component and create config only when absent");
    Check(File.Exists(Path.Combine(galaxy, "mods", "OrionAdminBridge", "modinfo.lua")) &&
          File.ReadAllText(Path.Combine(galaxy, "modconfig.lua")).Contains("OrionAdminBridge", StringComparison.Ordinal),
        "management bridge installation should expose verified files without overwriting an existing config");

    var serverSnapshotBefore = await File.ReadAllBytesAsync(serverExecutable);
    var galaxySnapshotBefore = await File.ReadAllBytesAsync(Path.Combine(galaxy, "save-state.dat"));
    IUpdateRollbackPointService rollbackPointService = new UpdateRollbackPointService(
        updateEnvironmentStore,
        updateEnvironmentService,
        new StaticManagedControl(Lifecycle.Stopped),
        options);
    var rollbackProgress = new List<string>();
    var rollbackPoint = await rollbackPointService.CreateAsync(
        "op_rollback_point_test",
        (_, step, _) => { rollbackProgress.Add(step); return Task.CompletedTask; });
    Check(rollbackPoint.Verified && rollbackPoint.ServerFileCount >= 2 && rollbackPoint.GalaxyFileCount >= 2,
        "update rollback point must include and fully verify both server and Galaxy files");
    Check(File.Exists(Path.Combine(rollbackPoint.StoragePath, "manifest.json")) &&
          File.Exists(Path.Combine(rollbackPoint.StoragePath, "manifest.sha256")) &&
          File.Exists(Path.Combine(rollbackPoint.StoragePath, "server", "AvorionServer.exe")) &&
          File.Exists(Path.Combine(rollbackPoint.StoragePath, "galaxy", "save-state.dat")),
        "verified update rollback point must atomically publish a manifest and both isolated scopes");
    Check(rollbackProgress.Contains("verifying-update-safety-point") && rollbackProgress[^1] == "update-safety-point-ready",
        "rollback point progress must expose copy verification and final publication checkpoints");
    Check(serverSnapshotBefore.SequenceEqual(await File.ReadAllBytesAsync(serverExecutable)) &&
          galaxySnapshotBefore.SequenceEqual(await File.ReadAllBytesAsync(Path.Combine(galaxy, "save-state.dat"))),
        "creating an update rollback point must not modify server or Galaxy source files");
    var latestRollbackPoint = await rollbackPointService.GetLatestAsync();
    Check(latestRollbackPoint.Available && latestRollbackPoint.Point?.PointId == rollbackPoint.PointId,
        "latest rollback point discovery must return only a manifest whose SHA-256 sidecar is valid");

    var runningRollbackRejected = false;
    try
    {
        var runningRollbackService = new UpdateRollbackPointService(
            updateEnvironmentStore,
            updateEnvironmentService,
            new StaticManagedControl(Lifecycle.Running),
            options);
        await runningRollbackService.CreateAsync("op_rollback_running", (_, _, _) => Task.CompletedTask);
    }
    catch (UpdateRollbackPointException exception)
    {
        runningRollbackRejected = exception.Code == "SERVER_MUST_BE_STOPPED";
    }
    Check(runningRollbackRejected, "update rollback point creation must fail closed while the server is running");

    // The fixture is copied beside the test assembly for both normal and custom output builds.
    // Resolve it from the deployed test directory instead of assuming a bin/Debug layout.
    var fakeServerDirectory = AppContext.BaseDirectory;
    var fakeServerExecutable = Path.Combine(fakeServerDirectory, "AvorionServer.exe");
    fakeServerExecutableForCleanup = fakeServerExecutable;
    Check(File.Exists(fakeServerExecutable), "the managed-process integration fixture must be built as AvorionServer.exe");
    var initializationRoot = Directory.CreateDirectory(Path.Combine(root, "initialization")).FullName;
    var initializationGalaxies = Directory.CreateDirectory(Path.Combine(initializationRoot, "galaxies")).FullName;
    var initializationData = Directory.CreateDirectory(Path.Combine(initializationRoot, "data")).FullName;
    var initializationOptions = Options.Create(new ServerNodeOptions
    {
        ServerId = "initialization-test",
        Name = "首次初始化测试节点",
        ProcessName = "AvorionServer-fixture-not-running",
        DataDirectory = initializationData,
        SteamCmdPath = steamCmdExecutable,
        ExecutablePath = fakeServerExecutable
    });
    var initializationStore = new SqliteStore(initializationOptions);
    await ((IOperationStore)initializationStore).InitializeAsync();
    IUpdateEnvironmentStore initializationEnvironmentStore = initializationStore;
    IUpdateEnvironmentService initializationEnvironmentService = new UpdateEnvironmentService(initializationOptions);
    var initializationEnvironmentValidation = await initializationEnvironmentService.ValidateAsync(steamCmdExecutable, fakeServerDirectory);
    Check(initializationEnvironmentValidation.Valid, "the real fixture executable directory must pass update-environment validation");
    await initializationEnvironmentStore.SaveAsync(new UpdateEnvironmentConfiguration(
        steamCmdExecutable,
        fakeServerDirectory,
        initializationEnvironmentValidation.ValidatedAt));
    var initializationPreflight = new ServerSetupPreflightService(
        initializationEnvironmentStore,
        initializationEnvironmentService);
    IServerSetupApplicationService initializationApplication = new ServerSetupApplicationService(
        initializationPreflight,
        initializationEnvironmentStore,
        initializationEnvironmentService,
        initializationOptions);
    var initializationRegistry = new ManagedServerProcessRegistry();
    var idempotentRegistry = new ManagedServerProcessRegistry();
    var firstProcessHandle = Process.GetProcessById(Environment.ProcessId);
    var duplicateProcessHandle = Process.GetProcessById(Environment.ProcessId);
    var canonicalProcessHandle = idempotentRegistry.RegisterOrGet(firstProcessHandle);
    var recoveredProcessHandle = idempotentRegistry.RegisterOrGet(duplicateProcessHandle);
    Check(ReferenceEquals(canonicalProcessHandle, recoveredProcessHandle), "concurrent status polling must idempotently recover the same managed PID");
    idempotentRegistry.Clear(canonicalProcessHandle);
    initializationRegistryForCleanup = initializationRegistry;
    IManagedServerRuntime initializationRuntime = new ManagedServerRuntime(initializationRegistry);
    IServerInitializationService initializationService = new ServerInitializationService(
        initializationApplication,
        initializationRuntime);
    var initializationGamePort = ReserveFreeTcpPort();
    var initializationRconPort = ReserveFreeTcpPort();
    var initializationQueryPort = ReserveFreeUdpPort();
    var initializationGalaxy = Path.Combine(initializationGalaxies, "ManagedGalaxy");
    const string initializationSecret = "initialization-secret-never-returned-28420";
    var initializationRequest = new ServerSetupPreflightRequest(
        "受管首次启动服务器",
        "ManagedGalaxy",
        "new",
        10,
        initializationGalaxy,
        "0.0.0.0",
        initializationGamePort,
        initializationQueryPort,
        true,
        initializationRconPort,
        initializationSecret,
        false,
        false);
    var initializationProgress = new List<string>();
    var initializationResult = await initializationService.InitializeAsync(
        "op_initialize_fixture",
        initializationRequest,
        (_, step, _) => { initializationProgress.Add(step); return Task.CompletedTask; });
    Check(initializationResult.Mode == "new-galaxy-running" && initializationResult.ProcessId > 0,
        "new Galaxy initialization must finish with a real managed process id");
    Check(initializationResult.ConsoleSaveConfirmed && initializationResult.ConsoleStopConfirmed &&
          initializationResult.RconConfigured && initializationResult.RconAuthenticated,
        "initialization must send /save and /stop before atomically configuring and authenticating RCON");
    Check(initializationProgress.Contains("server-ini-created") && initializationProgress.Contains("server-ready") &&
          initializationProgress.Contains("rcon-authenticated"),
        "initialization progress must wait for real server readiness before console and RCON checkpoints");
    var initializedIni = await File.ReadAllTextAsync(Path.Combine(initializationGalaxy, "server.ini"));
    Check(initializedIni.Contains($"rconPort={initializationRconPort}", StringComparison.Ordinal) &&
          initializedIni.Contains($"rconPassword={initializationSecret}", StringComparison.Ordinal),
        "RCON must be written only after the fixture process confirms a safe first stop");
    var initializationResultJson = System.Text.Json.JsonSerializer.Serialize(initializationResult);
    Check(!initializationResultJson.Contains(initializationSecret, StringComparison.Ordinal),
        "initialization result must never expose the RCON password");
    IManagedServerControlService managedControl = new ManagedServerControlService(initializationRegistry, steamQueryProbe, initializationOptions);
    var managedStatus = await managedControl.GetStatusAsync();
    Check(managedStatus.Lifecycle == Lifecycle.Running && managedStatus.ProcessId == initializationResult.ProcessId &&
          managedStatus.Rcon.Status == ConnectionState.Connected,
        "managed status must verify the exact initialized process and authenticate real loopback RCON");
    var saveResult = await managedControl.ExecuteAsync("save", (_, _, _) => Task.CompletedTask);
    Check(saveResult.Outcome == "save-acknowledged" && saveResult.RconAuthenticated &&
          File.Exists(Path.Combine(initializationGalaxy, "save-confirmed.marker")),
        "managed save must authenticate RCON and receive a real /save acknowledgement");

    var recoveredRegistry = new ManagedServerProcessRegistry();
    IManagedServerControlService recoveredControl = new ManagedServerControlService(recoveredRegistry, steamQueryProbe, initializationOptions);
    var recoveredStatus = await recoveredControl.GetStatusAsync();
    Check(recoveredStatus.Lifecycle == Lifecycle.Running && recoveredStatus.ProcessId == initializationResult.ProcessId &&
          recoveredStatus.Rcon.Status == ConnectionState.Connected,
        "a fresh Agent process registry must reacquire the single process from the exact managed executable path");
    var restartResult = await recoveredControl.ExecuteAsync("restart", (_, _, _) => Task.CompletedTask);
    Check(restartResult.Outcome == "restarted" && restartResult.Lifecycle == Lifecycle.Running &&
          restartResult.ProcessId is > 0 && restartResult.ProcessId != initializationResult.ProcessId,
        "managed restart must confirm the old process exit and authenticate RCON on a replacement process");
    var shutdownResult = await recoveredControl.ExecuteAsync("shutdown", (_, _, _) => Task.CompletedTask);
    Check(shutdownResult.Outcome == "stopped" && shutdownResult.Lifecycle == Lifecycle.Stopped,
        "managed shutdown must acknowledge /save and /stop and confirm the exact process exited without force killing it");
    var stoppedStatus = await recoveredControl.GetStatusAsync();
    Check(stoppedStatus.Lifecycle == Lifecycle.Stopped && stoppedStatus.ProcessId is null,
        "managed status must report stopped only after no exact owned process remains");
    var forceTarget = await recoveredControl.ExecuteAsync("start", (_, _, _) => Task.CompletedTask);
    var forcePid = forceTarget.ProcessId ?? throw new InvalidOperationException("fixture restart did not return a PID");
    var mismatchedPidRejected = false;
    try
    {
        await recoveredControl.ForceStopAsync(forcePid + 1, (_, _, _) => Task.CompletedTask);
    }
    catch (ServerControlException exception) when (exception.Code == "PROCESS_ID_CHANGED")
    {
        mismatchedPidRejected = true;
    }
    Check(mismatchedPidRejected, "force stop must reject a stale or mismatched expected PID without killing the server");
    Check((await recoveredControl.GetStatusAsync()).Lifecycle == Lifecycle.Running,
        "a rejected force stop must leave the exact managed server running");
    var forceResult = await recoveredControl.ForceStopAsync(forcePid, (_, _, _) => Task.CompletedTask);
    Check(forceResult.Outcome == "force-stopped" && forceResult.Lifecycle == Lifecycle.Stopped,
        "force stop must terminate only the exact expected managed process and confirm exit");

    var operation = await operationStore.CreateAsync("op_test", "diagnostics");
    await operationStore.MarkRunningAsync(operation.OperationId, "testing");
    await operationStore.CompleteAsync(operation.OperationId, diagnosticRun);
    var completed = await operationStore.GetAsync(operation.OperationId);
    Check(completed?.Status == OperationState.Succeeded && completed.Result is not null, "SQLite operation state should persist the real result");

    var requestEvidence = System.Text.Json.JsonSerializer.SerializeToElement(new { installDirectory = "D:\\test" });
    var idempotent = await operationStore.CreateIdempotentAsync("op_idempotent", "diagnostics", "test-key-123456", "HASH-A", request: requestEvidence);
    Check(idempotent.Created, "first idempotent operation request should create a queued operation");
    var replay = await operationStore.CreateIdempotentAsync("op_should_not_exist", "diagnostics", "test-key-123456", "HASH-A");
    Check(!replay.Created && replay.Operation.OperationId == idempotent.Operation.OperationId, "same idempotency key and hash should return the original operation");
    var conflictRejected = false;
    try
    {
        await operationStore.CreateIdempotentAsync("op_conflict", "diagnostics", "test-key-123456", "HASH-B");
    }
    catch (IdempotencyConflictException)
    {
        conflictRejected = true;
    }
    Check(conflictRejected, "reusing an idempotency key with a different request hash must be rejected");
    await operationStore.MarkRunningAsync(idempotent.Operation.OperationId, "safe-step");
    await operationStore.UpdateProgressAsync(idempotent.Operation.OperationId, 42, "checking-preconditions");
    var progressed = await operationStore.GetAsync(idempotent.Operation.OperationId);
    Check(progressed?.Status == OperationState.Running && progressed.ProgressPercent == 42, "operation progress and current step should persist");
    Check(progressed?.Request?.GetProperty("installDirectory").GetString() == "D:\\test", "operation request evidence should persist for browser recovery");
    await operationStore.FailInterruptedAsync(new ApiErrorDetail("AGENT_RESTARTED", "test recovery", "test", true));
    var recovered = await operationStore.GetAsync(idempotent.Operation.OperationId);
    Check(recovered?.Status == OperationState.Failed && recovered.Error?.Code == "AGENT_RESTARTED", "interrupted queued or running operations should fail closed on restart");

    var installParent = Directory.CreateDirectory(Path.Combine(root, "install-parent")).FullName;
    var installTarget = Path.Combine(installParent, "steamcmd");
    var progress = new List<double>();
    var archiveSource = new FixtureArchiveSource(CreateSteamCmdArchive());
    var installer = new SteamCmdInstaller(archiveSource, new AcceptValveSignature());
    var installResult = await installer.InstallAsync(
        "op_install_test",
        installTarget,
        (percent, _, _) => { progress.Add(percent); return Task.CompletedTask; });
    Check(File.Exists(Path.Combine(installTarget, "steamcmd.exe")), "SteamCMD installer should atomically create the validated target executable");
    Check(installResult.ArchiveSha256?.Length == 64 && installResult.ExecutableSha256.Length == 64 && installResult.Signer == "Valve" && !installResult.ReusedExisting, "SteamCMD result should persist archive and executable hashes plus verified signer evidence");
    Check(progress.Count > 0 && progress[^1] == 100, "SteamCMD installation should report persisted step progress through completion");
    Check(!Directory.EnumerateFileSystemEntries(installParent).Any(path => Path.GetFileName(path).StartsWith(".avorion-admin-steamcmd-", StringComparison.Ordinal)), "successful SteamCMD installation should remove download and staging artifacts");

    var reuseProgress = new List<string>();
    var reusedResult = await installer.InstallAsync("op_reuse_test", installTarget, (_, step, _) => { reuseProgress.Add(step); return Task.CompletedTask; });
    Check(reusedResult.ReusedExisting && reusedResult.ArchiveSha256 is null && reusedResult.ExecutableSha256.Length == 64, "an existing Valve-signed SteamCMD installation should be verified and reused without inventing archive evidence");
    Check(archiveSource.DownloadCount == 1 && reuseProgress.Contains("existing-installation-ready"), "reusing an existing SteamCMD installation must not download or overwrite it");

    var unrelatedTarget = Directory.CreateDirectory(Path.Combine(installParent, "unrelated-existing")).FullName;
    await File.WriteAllTextAsync(Path.Combine(unrelatedTarget, "user-file.txt"), "preserve me");
    var unrelatedRejected = false;
    try { installer.PrepareTarget(unrelatedTarget); }
    catch (SteamCmdInstallException exception) { unrelatedRejected = exception.Code == "INSTALL_TARGET_NOT_STEAMCMD"; }
    Check(unrelatedRejected && File.Exists(Path.Combine(unrelatedTarget, "user-file.txt")), "a non-SteamCMD directory must be rejected without changing user files");

    var emptyTarget = Directory.CreateDirectory(Path.Combine(installParent, "existing-empty")).FullName;
    var emptyResult = await installer.InstallAsync("op_empty_target_test", emptyTarget, (_, _, _) => Task.CompletedTask);
    Check(!emptyResult.ReusedExisting && File.Exists(Path.Combine(emptyTarget, "steamcmd.exe")) && archiveSource.DownloadCount == 2, "an existing empty directory should be safely populated by the fresh installer");

    var unsafeTarget = Path.Combine(installParent, "unsafe-steamcmd");
    var unsafeInstaller = new SteamCmdInstaller(new FixtureArchiveSource(CreateUnsafeArchive()), new AcceptValveSignature());
    var traversalRejected = false;
    try
    {
        await unsafeInstaller.InstallAsync("op_unsafe_test", unsafeTarget, (_, _, _) => Task.CompletedTask);
    }
    catch (SteamCmdInstallException exception)
    {
        traversalRejected = exception.Code == "STEAMCMD_ARCHIVE_UNSAFE";
    }
    Check(traversalRejected && !Directory.Exists(unsafeTarget), "unsafe ZIP paths must be rejected and staging content cleaned without creating the target");

    var serverInstallTarget = Path.Combine(installParent, "avorion-server");
    var serverRunner = new FixtureAvorionSteamCmdRunner(true);
    var serverInstaller = new AvorionServerInstaller(new AcceptValveSignature(), serverRunner);
    var serverProgress = new List<double>();
    var serverInstallResult = await serverInstaller.InstallAsync(
        "op_avorion_install_test",
        installResult.ExecutablePath,
        serverInstallTarget,
        (percent, _, _) => { serverProgress.Add(percent); return Task.CompletedTask; });
    Check(serverInstallResult.AppId == 565060 && serverInstallResult.Branch == "public", "Avorion installer must pin the verified dedicated-server app and public branch");
    Check(File.Exists(Path.Combine(serverInstallTarget, "bin", "AvorionServer.exe")), "Avorion installer should atomically publish the validated server executable");
    Check(File.Exists(Path.Combine(serverInstallTarget, "bin", "ServerRunner.exe")), "Avorion installer should verify and publish the Windows server runner");
    Check(serverRunner.SteamCmdPath == installResult.ExecutablePath && serverRunner.WorkingDirectory == installTarget, "Avorion install must run the selected SteamCMD from its own working directory");
    Check(serverRunner.RunCount == 1 && !serverInstallResult.ReusedExisting && serverInstallResult.ExecutableSha256.Length == 64 && serverInstallResult.RunnerSha256.Length == 64, "fresh Avorion installation should persist both executable hashes and run SteamCMD once");
    Check(serverProgress.Count > 0 && serverProgress[^1] == 100, "Avorion installation should report persisted progress through completion");
    Check(!Directory.EnumerateFileSystemEntries(installParent).Any(path => Path.GetFileName(path).StartsWith(".avorion-admin-server-", StringComparison.Ordinal)), "successful Avorion installation should remove staging artifacts");

    var reusedServerResult = await serverInstaller.InstallAsync(
        "op_avorion_reuse_test",
        installResult.ExecutablePath,
        serverInstallTarget,
        (_, _, _) => Task.CompletedTask);
    Check(reusedServerResult.ReusedExisting && reusedServerResult.InstalledAt is null && serverRunner.RunCount == 1, "an existing complete Avorion server should be verified and reused without running SteamCMD or inventing a new install time");

    var unrelatedServerTarget = Directory.CreateDirectory(Path.Combine(installParent, "unrelated-server-existing")).FullName;
    await File.WriteAllTextAsync(Path.Combine(unrelatedServerTarget, "user-file.txt"), "preserve me");
    var unrelatedServerRejected = false;
    try { serverInstaller.Prepare(installResult.ExecutablePath, unrelatedServerTarget); }
    catch (AvorionServerInstallException exception) { unrelatedServerRejected = exception.Code == "INSTALL_TARGET_NOT_AVORION_SERVER"; }
    Check(unrelatedServerRejected && File.Exists(Path.Combine(unrelatedServerTarget, "user-file.txt")), "a non-Avorion server directory must be rejected without changing user files");

    var transientLockTarget = Path.Combine(installParent, "avorion-server-transient-lock");
    var transientLockInstaller = new AvorionServerInstaller(new AcceptValveSignature(), new TransientLockAvorionSteamCmdRunner());
    var transientLockResult = await transientLockInstaller.InstallAsync(
        "op_avorion_transient_lock_test",
        installResult.ExecutablePath,
        transientLockTarget,
        (_, _, _) => Task.CompletedTask);
    Check(File.Exists(transientLockResult.ExecutablePath), "Avorion publication should retry and succeed after a short-lived Windows file lock is released");

    var processInfo = AvorionSteamCmdRunner.CreateStartInfo(installResult.ExecutablePath, installTarget, "D:\\Target With Spaces");
    var expectedArguments = new[] { "+force_install_dir", "D:\\Target With Spaces", "+login", "anonymous", "+app_update", "565060", "-beta", "public", "validate", "+quit" };
    Check(!processInfo.UseShellExecute && processInfo.ArgumentList.SequenceEqual(expectedArguments), "SteamCMD execution must bypass the shell and use the exact fixed App 565060 argument list");
    Check(
        AvorionSteamCmdRunner.ShouldRetryAfterSelfUpdate(7, "ERROR! Missing configuration\nUpdate complete, launching...") &&
        !AvorionSteamCmdRunner.ShouldRetryAfterSelfUpdate(7, "ERROR! Missing configuration") &&
        !AvorionSteamCmdRunner.ShouldRetryAfterSelfUpdate(7, "Update complete, launching...\nERROR! disk write failure") &&
        !AvorionSteamCmdRunner.ShouldRetryAfterSelfUpdate(0, "Missing configuration\nUpdate complete, launching..."),
        "SteamCMD bootstrap retry must be limited to one failed first-run self-update signature");
    var realLayoutRoot = Directory.CreateDirectory(Path.Combine(installParent, "real-server-layout")).FullName;
    var realLayoutBin = Directory.CreateDirectory(Path.Combine(realLayoutRoot, "bin")).FullName;
    Directory.CreateDirectory(Path.Combine(realLayoutRoot, "data"));
    Check(
        ServerSetupApplicationService.ResolveServerWorkingDirectory(Path.Combine(realLayoutBin, "AvorionServer.exe")) == realLayoutRoot,
        "managed launch profiles must run bin\\AvorionServer.exe from the server root so relative data/scripts assets resolve");

    var failedServerTarget = Path.Combine(installParent, "avorion-server-failed");
    var failedServerInstaller = new AvorionServerInstaller(new AcceptValveSignature(), new FixtureAvorionSteamCmdRunner(false));
    var missingSuccessRejected = false;
    try
    {
        await failedServerInstaller.InstallAsync("op_avorion_failed_test", installResult.ExecutablePath, failedServerTarget, (_, _, _) => Task.CompletedTask);
    }
    catch (AvorionServerInstallException exception)
    {
        missingSuccessRejected = exception.Code == "STEAMCMD_SUCCESS_NOT_CONFIRMED" &&
            exception.Details?.TryGetValue("logExcerpt", out var excerpt) == true &&
            excerpt?.ToString()?.Contains("without confirmation", StringComparison.Ordinal) == true;
    }
    Check(missingSuccessRejected && !Directory.Exists(failedServerTarget), "Avorion install must persist a bounded SteamCMD log excerpt, fail closed and clean staging when the success marker is absent");
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    failures.Add($"Unhandled test failure: {exception.GetType().Name}: {exception.Message}");
}
finally
{
    var fixtureProcess = initializationRegistryForCleanup?.GetRunning();
    if (fixtureProcess is { HasExited: false } && fakeServerExecutableForCleanup is not null)
    {
        try
        {
            var actualPath = fixtureProcess.MainModule?.FileName;
            if (actualPath is not null && Path.GetFullPath(actualPath).Equals(Path.GetFullPath(fakeServerExecutableForCleanup), StringComparison.OrdinalIgnoreCase))
            {
                fixtureProcess.Kill(entireProcessTree: true);
                await fixtureProcess.WaitForExitAsync();
            }
        }
        catch { }
    }
    try { Directory.Delete(root, true); } catch { }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("FAIL");
    foreach (var failure in failures) Console.Error.WriteLine($"- {failure}");
    Environment.ExitCode = 1;
}
else
{
    Console.WriteLine("PASS: real-data, write-safety, lifecycle-control, initialization, setup-application, installer and no-fallback assertions completed.");
}

return;

void Check(bool condition, string message)
{
    if (!condition) failures.Add(message);
}

static int ReserveFreeTcpPort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

static int ReserveFreeUdpPort(int excluded = -1)
{
    for (var attempt = 0; attempt < 10; attempt++)
    {
        using var client = new UdpClient(0);
        var port = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
        if (port != excluded) return port;
    }
    throw new InvalidOperationException("unable to reserve a distinct UDP test port");
}

static byte[] CreateSteamCmdArchive()
{
    using var buffer = new MemoryStream();
    using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
    {
        var entry = zip.CreateEntry("steamcmd.exe");
        using var stream = entry.Open();
        var executable = new byte[70 * 1024];
        executable[0] = (byte)'M';
        executable[1] = (byte)'Z';
        stream.Write(executable);
    }
    return buffer.ToArray();
}

static byte[] CreateUnsafeArchive()
{
    using var buffer = new MemoryStream();
    using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
    {
        var entry = zip.CreateEntry("../outside.exe");
        using var stream = entry.Open();
        stream.Write([0x4D, 0x5A]);
    }
    return buffer.ToArray();
}
