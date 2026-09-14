using System.Net;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AvorionAdmin.Agent;
using AvorionAdmin.Core;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Configuration;
using AvorionAdmin.Core.Models;
using Microsoft.Extensions.Options;

namespace AvorionAdmin.Api;

public sealed record DiagnosticWorkItem(string OperationId);
public sealed record SteamCmdInstallWorkItem(string OperationId, string InstallDirectory);
public sealed record AvorionServerInstallWorkItem(string OperationId, string SteamCmdPath, string InstallDirectory);
public sealed record ServerSetupApplicationWorkItem(string OperationId, ServerSetupPreflightRequest Request);
public sealed record ServerInitializationWorkItem(string OperationId, ServerSetupPreflightRequest Request);
public sealed record ServerControlWorkItem(string OperationId, string Action, int? ExpectedProcessId = null);
public sealed record UpdateInspectionWorkItem(string OperationId, string Action);
public sealed record UpdateRollbackPointWorkItem(string OperationId);
public sealed record BackupRestoreWorkItem(string OperationId, string BackupId);
public sealed record SectorUnloadWorkItem(string OperationId, int X, int Y);
public sealed record PlayerRewardWorkItem(string OperationId, int PlayerIndex, PlayerRewardGrant Grant);
public sealed record AllianceRewardWorkItem(string OperationId, int AllianceIndex, AllianceRewardGrant Grant);
public sealed record RewardBatchWorkItem(string OperationId, RewardBatchRequest Request);
public sealed record InventoryGrantWorkItem(
    string OperationId,
    string OwnerKind,
    int OwnerIndex,
    string UpgradeKey,
    string Rarity,
    string Seed);
