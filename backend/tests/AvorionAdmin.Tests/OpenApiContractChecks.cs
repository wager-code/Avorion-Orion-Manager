using System.Text.RegularExpressions;

internal static class OpenApiContractChecks
{
    public static void Run(Action<bool, string> check)
    {
        try
        {
            var contractPath = FindContract();
            check(contractPath is not null, "OpenAPI contract must be discoverable from the test working directory");
            if (contractPath is null) return;

            var source = File.ReadAllText(contractPath);
            var paths = Regex.Matches(source, @"(?m)^  (?<path>/[^:\r\n]+):\s*$")
                .Select(match => match.Groups["path"].Value)
                .ToArray();
            var pathSet = paths.ToHashSet(StringComparer.Ordinal);
            var normalizedPathSet = paths.Select(NormalizePath).ToHashSet(StringComparer.Ordinal);

            check(source.Contains("  version: 0.13.0", StringComparison.Ordinal),
                "OpenAPI version must advance with the endpoint and schema alignment");
            check(paths.Length == pathSet.Count, "OpenAPI paths must be unique");

            var requiredPaths = new[]
            {
                "/health",
                "/servers/{serverId}/management-bridge/hello",
                "/servers/{serverId}/management-bridge/players",
                "/servers/{serverId}/management-bridge/players-known",
                "/servers/{serverId}/management-bridge/alliances",
                "/servers/{serverId}/management-bridge/alliance",
                "/servers/{serverId}/inventory-catalog",
                "/servers/{serverId}/inventory-icons",
                "/servers/{serverId}/updates/checks",
                "/servers/{serverId}/updates/verifications",
                "/servers/{serverId}/updates/rollback-points/latest",
                "/servers/{serverId}/updates/rollback-points",
                "/servers/{serverId}/actions/start",
                "/servers/{serverId}/actions/save",
                "/servers/{serverId}/actions/shutdown",
                "/servers/{serverId}/actions/restart",
                "/servers/{serverId}/actions/force-stop",
                "/operations"
            };
            foreach (var path in requiredPaths)
                check(pathSet.Contains(path), $"OpenAPI contract must include {path}");

            var repositoryRoot = Directory.GetParent(Path.GetDirectoryName(contractPath)!)!.FullName;
            var endpointDirectory = Path.Combine(repositoryRoot, "backend", "src", "AvorionAdmin.Api", "Endpoints");
            foreach (var endpointFile in Directory.EnumerateFiles(endpointDirectory, "*.cs"))
            {
                var endpointSource = File.ReadAllText(endpointFile);
                foreach (Match match in Regex.Matches(endpointSource,
                             @"Map(?:Get|Post|Put|Patch|Delete)\(\s*""(?<path>/api/v1/[^""]+)"""))
                {
                    var path = Regex.Replace(match.Groups["path"].Value["/api/v1".Length..],
                        @"\{(?<name>[^}:]+):[^}]+\}",
                        value => $"{{{value.Groups["name"].Value}}}");
                    if (path.EndsWith("/", StringComparison.Ordinal) ||
                        path.Contains("/management-bridge/{query}", StringComparison.Ordinal))
                        continue;
                    check(normalizedPathSet.Contains(NormalizePath(path)),
                        $"OpenAPI contract is missing literal API route {path} from {Path.GetFileName(endpointFile)}");
                }
            }

            var schemaNames = Regex.Matches(source, @"(?m)^    (?<name>[A-Za-z][A-Za-z0-9]+):\s*$")
                .Select(match => match.Groups["name"].Value)
                .ToHashSet(StringComparer.Ordinal);
            var schemaReferences = Regex.Matches(source,
                    @"#/components/schemas/(?<name>[A-Za-z][A-Za-z0-9]+)")
                .Select(match => match.Groups["name"].Value)
                .Distinct(StringComparer.Ordinal);
            foreach (var reference in schemaReferences)
                check(schemaNames.Contains(reference), $"OpenAPI schema reference {reference} must resolve");

            foreach (var schema in new[]
                     {
                         "HealthStatus", "BridgeHelloEnvelope", "PlayerQueryEnvelope",
                         "KnownPlayerQueryEnvelope", "AllianceQueryEnvelope", "AllianceDetailEnvelope",
                         "InventoryCatalogEntry", "InventoryCatalogPage",
                         "UpdateRollbackPointResult", "UpdateRollbackPointAvailability"
                     })
                check(schemaNames.Contains(schema), $"OpenAPI contract must define {schema}");

            var operationIds = Regex.Matches(source, @"(?m)^      operationId:\s*(?<id>[A-Za-z][A-Za-z0-9]+)\s*$")
                .Select(match => match.Groups["id"].Value)
                .ToArray();
            check(operationIds.Length == operationIds.Distinct(StringComparer.Ordinal).Count(),
                "OpenAPI operationId values must be unique");
            check(source.Contains("0.10.0", StringComparison.Ordinal),
                "OpenAPI management bridge schemas must include component version 0.10.0");
        }
        catch (Exception exception)
        {
            check(false, $"OpenAPI contract verification failed: {exception.Message}");
        }
    }

    private static string NormalizePath(string path) =>
        Regex.Replace(path, @"\{[^}]+\}", "{}");

    private static string? FindContract()
    {
        foreach (var seed in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(seed); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "contracts", "server-management.openapi.yaml");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
