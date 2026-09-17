using System.Text.Json;
using System.Text.Json.Serialization;
using AvorionAdmin.Api;
using AvorionAdmin.Api.DependencyInjection;
using AvorionAdmin.Api.Endpoints;
using AvorionAdmin.Core.Abstractions;
using AvorionAdmin.Core.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddWindowsService(options => options.ServiceName = "OrionAdmin");
builder.Services.AddOrionAdminModules(builder.Configuration);

var app = builder.Build();
app.UseMiddleware<LocalOnlyMiddleware>();
app.UseMiddleware<AdminWriteProtectionMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();

var performanceStore = app.Services.GetRequiredService<IPerformanceStore>();
var operationStore = app.Services.GetRequiredService<IOperationStore>();
var automationTaskStore = app.Services.GetRequiredService<IAutomationTaskStore>();
await performanceStore.InitializeAsync();
await operationStore.InitializeAsync();
await automationTaskStore.InitializeAsync();
await operationStore.FailInterruptedAsync(new ApiErrorDetail(
    "AGENT_RESTARTED",
    "Server Agent 重启，未完成操作已安全终止，请确认当前状态后重试",
    "startup-recovery",
    true));

app.MapSecurityEndpoints();
app.MapManagementBridgeEndpoints();
app.MapPlayerEndpoints();
app.MapAllianceEndpoints();
app.MapGameManagementEndpoints();
app.MapInventoryEndpoints();
app.MapInventoryCatalogEndpoints();
app.MapServerOverviewEndpoints();
app.MapPerformanceEndpoints();
app.MapSectorEndpoints();
app.MapProvisioningEndpoints();
app.MapAutomationEndpoints();
app.MapBackupEndpoints();
app.MapLogEndpoints();
app.MapDiagnosticEndpoints();
app.MapServerControlEndpoints();
app.MapUpdateCommandEndpoints();
app.Map("/api/{**unmatched}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
