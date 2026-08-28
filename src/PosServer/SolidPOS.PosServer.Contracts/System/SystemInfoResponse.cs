namespace SolidPOS.PosServer.Contracts.System;

public sealed record SystemInfoResponse(
    string Service,
    string Version,
    string Environment,
    DateTimeOffset ServerTimeUtc);
