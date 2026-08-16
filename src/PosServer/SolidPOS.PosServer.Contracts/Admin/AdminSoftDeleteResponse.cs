namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record AdminSoftDeleteResponse(
    Guid TenantId,
    string EntityType,
    Guid EntityId,
    string SyncEntityType,
    long Version,
    DateTimeOffset DeletedAt);
