namespace SolidPOS.PosServer.Contracts.Terminals;

public sealed record UpdateTerminalRemoteConfigRequest(
    TerminalRemoteConfigMetadata RemoteConfig);
