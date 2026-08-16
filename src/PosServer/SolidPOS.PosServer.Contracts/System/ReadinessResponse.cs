namespace SolidPOS.PosServer.Contracts.System;

public sealed record ReadinessResponse(
    string Status,
    string Database,
    DateTimeOffset ServerTimeUtc,
    string? Detail = null);
