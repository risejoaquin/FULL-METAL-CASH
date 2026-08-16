using SolidPOS.PosServer.Contracts.AdminManagement;

namespace SolidPOS.PosServer.Application.AdminManagement;

public interface IAdminManagementService
{
    Task<TenantCurrentResponse?> GetCurrentTenantAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<StoreResponse>> ListStoresAsync(CancellationToken cancellationToken);

    Task<StoreResponse?> CreateStoreAsync(CreateStoreRequest request, CancellationToken cancellationToken);

    Task<StoreResponse?> UpdateStoreAsync(Guid storeId, UpdateStoreRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<UserResponse>> ListUsersAsync(CancellationToken cancellationToken);

    Task<UserResponse?> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken);

    Task<UserResponse?> UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<RoleResponse>> ListRolesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<PermissionResponse>> ListPermissionsAsync(CancellationToken cancellationToken);
}
