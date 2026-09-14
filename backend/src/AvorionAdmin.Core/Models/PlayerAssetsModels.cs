namespace AvorionAdmin.Core.Models;

public sealed record PlayerResourceBalances(
    long Iron,
    long Titanium,
    long Naonite,
    long Trinium,
    long Xanion,
    long Ogonite,
    long Avorion);

public sealed record PlayerAssetsSnapshot(
    int PlayerIndex,
    string PlayerName,
    bool Online,
    long Credits,
    PlayerResourceBalances Resources,
    DateTimeOffset SampledAt,
    string Freshness,
    string Source,
    string ComponentVersion);
