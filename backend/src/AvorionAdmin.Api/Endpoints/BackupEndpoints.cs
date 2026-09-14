#nullable disable
#pragma warning disable AD0001

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AvorionAdmin.Agent;
using AvorionAdmin.Core;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AvorionAdmin.Api.Endpoints;

public static class BackupEndpoints
{
    public static WebApplication MapBackupEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/servers/{serverId}/backups", (Func<string, int?, HttpContext, IBackupScanner, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, int? limit, HttpContext context, IBackupScanner scanner, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	if (!node.HasReadableBackupPath())
        	{
        		return ApiErrors.Create(context, 503, "CAPABILITY_UNAVAILABLE", "备份路径未配置或不可访问");
        	}
        	IReadOnlyList<BackupRecord> records = await scanner.ScanAsync(cancellationToken);
        	return Results.Ok(new
        	{
        		items = records.Take(Math.Clamp(limit ?? 50, 1, 100)),
        		total = records.Count,
        		totalSizeBytes = records.Sum((BackupRecord record) => record.SizeBytes),
        		nextCursor = (string)null
        	});
        });
        app.MapPost("/api/v1/servers/{serverId}/backups/refresh", (Func<string, HttpContext, IBackupScanner, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, HttpContext context, IBackupScanner scanner, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	if (!node.HasReadableBackupPath())
        	{
        		return ApiErrors.Create(context, 503, "CAPABILITY_UNAVAILABLE", "备份路径未配置或不可访问");
        	}
        	IReadOnlyList<BackupRecord> records = await scanner.ScanAsync(cancellationToken);
        	return Results.Ok(new
        	{
        		items = records.Take(100),
        		total = records.Count,
        		totalSizeBytes = records.Sum((BackupRecord record) => record.SizeBytes),
        		refreshedAt = DateTimeOffset.UtcNow
        	});
        });
        app.MapPost("/api/v1/servers/{serverId}/backups/{backupId}/restore", (Func<string, string, BackupRestoreRequest, HttpContext, BackupScanner, IOperationStore, BackupRestoreQueue, VerifiedCommandRegistry, ServerNodeOptionsAccessor, CancellationToken, Task<IResult>>)async delegate(string serverId, string backupId, BackupRestoreRequest request, HttpContext context, BackupScanner scanner, IOperationStore store, BackupRestoreQueue queue, VerifiedCommandRegistry commands, ServerNodeOptionsAccessor node, CancellationToken cancellationToken)
        {
        	if (!node.Matches(serverId))
        	{
        		return ApiErrors.Create(context, 404, "SERVER_NOT_FOUND", "未找到服务器节点");
        	}
        	if (!commands.IsVerified("backup.restore"))
        	{
        		return ApiErrors.Locked(context);
        	}
        	BackupScanner.ResolvedBackupFile resolved = await scanner.ResolveAvailableAsync(backupId, cancellationToken);
        	if ((object)resolved == null)
        	{
        		return ApiErrors.Create(context, 404, "BACKUP_NOT_FOUND", "所选备份已不存在、不可读取或不属于当前 Galaxy");
        	}
        	if (!string.Equals(request.Confirmation, "RESTORE " + backupId, StringComparison.Ordinal))
        	{
        		return ApiErrors.Create(context, 400, "DANGER_CONFIRMATION_REQUIRED", "恢复确认文本无效，请刷新备份记录后重新确认");
        	}
        	string idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        	int length = idempotencyKey.Length;
        	if ((length < 8 || length > 128) ? true : false)
        	{
        		return ApiErrors.Create(context, 400, "IDEMPOTENCY_KEY_REQUIRED", "备份恢复需要 8–128 字符的 Idempotency-Key");
        	}
        	string requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("backup.restore:" + serverId.ToLowerInvariant() + ":" + backupId)));
        	string operationId = $"op_{Guid.NewGuid():N}";
        	try
        	{
        		JsonElement evidence = JsonSerializer.SerializeToElement(new
        		{
        			backupId = backupId,
        			fileName = resolved.Record.FileName,
        			confirmationMatched = true
        		});
        		OperationCreateResult created = await store.CreateIdempotentAsync(operationId, "backup.restore", idempotencyKey, requestHash, cancellationToken, evidence);
        		if (created.Created)
        		{
        			await queue.EnqueueAsync(new BackupRestoreWorkItem(created.Operation.OperationId, backupId), cancellationToken);
        		}
        		OperationRecord operation = created.Operation;
        		return Results.Accepted("/api/v1/operations/" + operation.OperationId, new OperationAccepted(operation.OperationId, operation.Status, operation.AcceptedAt));
        	}
        	catch (IdempotencyConflictException)
        	{
        		return ApiErrors.Create(context, 409, "IDEMPOTENCY_CONFLICT", "该 Idempotency-Key 已用于不同的备份恢复请求");
        	}
        });

        return app;
    }
}
