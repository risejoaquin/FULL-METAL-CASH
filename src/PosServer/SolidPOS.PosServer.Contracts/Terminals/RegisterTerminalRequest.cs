namespace SolidPOS.PosServer.Contracts.Terminals;

public sealed record RegisterTerminalRequest(
    string EnrollmentToken,
    string Name,
    string Fingerprint,
    string? AppVersion = null);
