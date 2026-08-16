namespace SolidPOS.PosServer.Contracts.Terminals;

public sealed record TerminalSessionResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    TerminalResponse Terminal);
