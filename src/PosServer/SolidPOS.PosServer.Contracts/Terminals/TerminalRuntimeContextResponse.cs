namespace SolidPOS.PosServer.Contracts.Terminals;

public sealed record TerminalRuntimeContextResponse(
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId);
