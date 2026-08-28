namespace SolidPOS.PosServer.Contracts.System;

public sealed record HealthResponse(
    string Status,
    string Service,
    string Version,
    DateTimeOffset ServerTimeUtc);
