namespace AvorionAdmin.Core.Models;

public static class InventoryGrantPolicies
{
    public const string VerifiedGrantable = "verified-grantable";
    public const string CatalogOnly = "catalog-only";
    public const string StoryBlocked = "story-blocked";
}

public sealed record InventoryCatalogEntry(
    string Id,
    string ItemType,
    string Key,
    string DisplayName,
    string EnglishName,
    string Category,
    string? IconPath,
    bool IconAvailable,
    string GrantPolicy,
    string? Script,
    string? WeaponType,
    string Description);

public sealed record InventoryCatalogPage(
    int Total,
    int Offset,
    int Limit,
    IReadOnlyList<InventoryCatalogEntry> Items,
    int IndexedIconCount,
    string IconSource,
    DateTimeOffset SampledAt,
    string Source);

public sealed record InventoryCatalogQuery(
    string? Query,
    string? ItemType,
    string? GrantPolicy,
    int Offset,
    int Limit);

public sealed record InventoryIconFile(
    byte[] Content,
    string ContentType,
    string ETag);

public static class InventoryCatalogPolicy
{
    public const int MaximumPageSize = 100;
    public const int MaximumQueryLength = 128;

    private static readonly HashSet<string> ItemTypes =
        new(StringComparer.Ordinal) { "turret", "system-upgrade" };

    private static readonly HashSet<string> GrantPolicies =
        new(StringComparer.Ordinal)
        {
            InventoryGrantPolicies.VerifiedGrantable,
            InventoryGrantPolicies.CatalogOnly,
            InventoryGrantPolicies.StoryBlocked
        };

    public static InventoryValidationError? Validate(InventoryCatalogQuery query)
    {
        if (query.Query?.Length > MaximumQueryLength)
            return new("INVENTORY_CATALOG_QUERY_INVALID", "物品目录搜索内容过长");
        if (query.ItemType is not null && !ItemTypes.Contains(query.ItemType))
            return new("INVENTORY_CATALOG_TYPE_INVALID", "物品目录类型无效");
        if (query.GrantPolicy is not null && !GrantPolicies.Contains(query.GrantPolicy))
            return new("INVENTORY_CATALOG_POLICY_INVALID", "物品目录发放策略无效");
        if (query.Offset is < 0 or > 100000 || query.Limit is < 1 or > MaximumPageSize)
            return new("INVENTORY_CATALOG_PAGE_INVALID", "物品目录分页参数无效");
        return null;
    }

    public static string? NormalizeIconPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 256) return null;
        var normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith("data/textures/icons/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("//", StringComparison.Ordinal) ||
            normalized.Contains(':', StringComparison.Ordinal)) return null;
        var segments = normalized.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or "..")) return null;
        if (segments.Any(segment => segment.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))) return null;
        var extension = Path.GetExtension(normalized);
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp") &&
            extension.ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".webp")) return null;
        return string.Join('/', segments).ToLowerInvariant();
    }
}
