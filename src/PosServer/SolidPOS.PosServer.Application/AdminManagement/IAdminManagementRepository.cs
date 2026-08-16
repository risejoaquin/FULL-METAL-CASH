using SolidPOS.PosServer.Contracts.AdminManagement;

namespace SolidPOS.PosServer.Application.AdminManagement;

public interface IAdminManagementRepository
{
    Task<TenantCurrentResponse?> GetCurrentTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<StoreResponse>> ListStoresAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<StoreResponse?> CreateStoreAsync(Guid tenantId, CreateStoreRequest request, CancellationToken cancellationToken);

    Task<StoreResponse?> UpdateStoreAsync(Guid tenantId, Guid storeId, UpdateStoreRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<UserResponse>> ListUsersAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<UserResponse?> CreateUserAsync(Guid tenantId, CreateUserRequest request, string passwordHash, CancellationToken cancellationToken);

    Task<UserResponse?> UpdateUserAsync(Guid tenantId, Guid userId, UpdateUserRequest request, string? passwordHash, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<RoleResponse>> ListRolesAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<PermissionResponse>> ListPermissionsAsync(CancellationToken cancellationToken);
}
