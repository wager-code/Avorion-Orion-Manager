namespace AvorionAdmin.Core.Models;

public sealed record AllianceResourceBalances(
    long Iron,
    long Titanium,
    long Naonite,
    long Trinium,
    long Xanion,
    long Ogonite,
    long Avorion);

public sealed record AllianceAssetsSnapshot(
    int AllianceIndex,
    string AllianceName,
    bool Online,
    long Credits,
    AllianceResourceBalances Resources,
    DateTimeOffset SampledAt,
    string Freshness,
    string Source,
    string ComponentVersion);
