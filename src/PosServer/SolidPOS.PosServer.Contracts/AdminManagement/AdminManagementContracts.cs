using System.Text.Json;
using SolidPOS.PosServer.Contracts.Tenants;

namespace SolidPOS.PosServer.Contracts.AdminManagement;

public sealed record TenantCurrentResponse(
    Guid Id,
    string Name,
    string? LegalName,
    string Status,
    string Timezone,
    string Currency,
    TenantConfigResponse Settings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record StoreResponse(
    Guid Id,
    Guid TenantId,
    string Code,
    string Name,
    string? Address,
    string? Phone,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateStoreRequest(
    string Code,
    string Name,
    string? Address = null,
    string? Phone = null,
    string Status = "active");

public sealed record UpdateStoreRequest(
    string? Code = null,
    string? Name = null,
    string? Address = null,
    string? Phone = null,
    string? Status = null);

public sealed record UserResponse(
    Guid Id,
    Guid TenantId,
    string Email,
    string FullName,
    string Status,
    IReadOnlyCollection<Guid> RoleIds,
    IReadOnlyCollection<string> RoleCodes,
    IReadOnlyCollection<Guid> StoreIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateUserRequest(
    string Email,
    string FullName,
    string Password,
    string Status = "active",
    IReadOnlyCollection<Guid>? RoleIds = null,
    IReadOnlyCollection<string>? RoleCodes = null,
    IReadOnlyCollection<Guid>? StoreIds = null);

public sealed record UpdateUserRequest(
    string? Email = null,
    string? FullName = null,
    string? Password = null,
    string? Status = null,
    IReadOnlyCollection<Guid>? RoleIds = null,
    IReadOnlyCollection<string>? RoleCodes = null,
    IReadOnlyCollection<Guid>? StoreIds = null);

public sealed record RoleResponse(
    Guid Id,
    Guid TenantId,
    string Code,
    string Name,
    bool IsSystem,
    IReadOnlyCollection<string> Permissions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PermissionResponse(
    string Code,
    string Description);
