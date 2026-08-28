namespace SolidPOS.PosCore.Domain;

public sealed record LocalAppliedSyncChange(
    Guid ChangeId,
    string EntityType,
    Guid EntityId,
    string Operation,
    long EntityVersion,
    DateTimeOffset ChangedAtUtc,
    string PayloadJson,
    DateTimeOffset AppliedAtUtc);

public sealed record LocalSyncPullState(
    string? Cursor,
    DateTimeOffset? LastPulledAtUtc,
    int LastChangeCount,
    int TotalAppliedChangeCount);

public sealed record LocalRemoteSaleReadModel(
    Guid RemoteSaleId,
    Guid? LocalSaleId,
    Guid TenantId,
    Guid StoreId,
    Guid TerminalId,
    string Status,
    int TotalCents,
    DateTimeOffset? OccurredAtUtc,
    string RawJson,
    DateTimeOffset AppliedAtUtc);

public sealed record LocalRemoteReceiptReadModel(
    Guid ReceiptId,
    Guid SaleId,
    string ReceiptNumber,
    string? PublicToken,
    string RawJson,
    DateTimeOffset AppliedAtUtc);
