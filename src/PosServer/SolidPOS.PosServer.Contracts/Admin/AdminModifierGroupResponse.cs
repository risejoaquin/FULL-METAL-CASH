namespace SolidPOS.PosServer.Contracts.Admin;

public sealed record AdminModifierGroupResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    int MinSelected,
    int MaxSelected,
    bool Required,
    long Version,
    DateTimeOffset UpdatedAt);
