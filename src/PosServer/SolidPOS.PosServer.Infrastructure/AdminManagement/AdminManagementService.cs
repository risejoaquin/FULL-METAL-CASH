using System.Text.Json;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.AdminManagement;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Auth;
using SolidPOS.PosServer.Contracts.AdminManagement;

namespace SolidPOS.PosServer.Infrastructure.AdminManagement;

public sealed class AdminManagementService : IAdminManagementService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> StoreStatuses = ["active", "inactive", "archived"];
    private static readonly HashSet<string> UserStatuses = ["active", "suspended", "invited"];

    private readonly ITenantContext _tenantContext;
    private readonly IAdminManagementRepository _repository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ILogger<AdminManagementService> _logger;

    public AdminManagementService(
        ITenantContext tenantContext,
        IAdminManagementRepository repository,
        IPasswordHasher passwordHasher,
        IAuditEventWriter auditEventWriter,
        ILogger<AdminManagementService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _passwordHasher = passwordHasher;
        _auditEventWriter = auditEventWriter;
        _logger = logger;
    }

    public async Task<TenantCurrentResponse?> GetCurrentTenantAsync(CancellationToken cancellationToken)
    {
        return TryGetTenantId(out Guid tenantId)
            ? await _repository.GetCurrentTenantAsync(tenantId, cancellationToken)
            : null;
    }

    public async Task<IReadOnlyCollection<StoreResponse>> ListStoresAsync(CancellationToken cancellationToken)
    {
        return TryGetTenantId(out Guid tenantId)
            ? await _repository.ListStoresAsync(tenantId, cancellationToken)
            : Array.Empty<StoreResponse>();
    }

    public async Task<StoreResponse?> CreateStoreAsync(CreateStoreRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId)
            || string.IsNullOrWhiteSpace(request.Code)
            || string.IsNullOrWhiteSpace(request.Name)
            || !StoreStatuses.Contains(Normalize(request.Status)))
        {
            _logger.LogWarning("Store creation rejected for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        StoreResponse? response = await _repository.CreateStoreAsync(
            tenantId,
            request with { Code = request.Code.Trim(), Name = request.Name.Trim(), Status = Normalize(request.Status) },
            cancellationToken);

        if (response is not null)
        {
            await WriteAuditAsync(tenantId, "store.created", "store", response.Id, response, cancellationToken);
        }

        return response;
    }

    public async Task<StoreResponse?> UpdateStoreAsync(Guid storeId, UpdateStoreRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId)
            || (request.Code is not null && string.IsNullOrWhiteSpace(request.Code))
            || (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
            || (request.Status is not null && !StoreStatuses.Contains(Normalize(request.Status))))
        {
            _logger.LogWarning("Store update rejected for tenant {TenantId} store {StoreId}", _tenantContext.TenantId, storeId);
            return null;
        }

        StoreResponse? response = await _repository.UpdateStoreAsync(
            tenantId,
            storeId,
            request with
            {
                Code = request.Code?.Trim(),
                Name = request.Name?.Trim(),
                Status = request.Status is null ? null : Normalize(request.Status)
            },
            cancellationToken);

        if (response is not null)
        {
            await WriteAuditAsync(tenantId, "store.updated", "store", response.Id, response, cancellationToken);
        }

        return response;
    }

    public async Task<IReadOnlyCollection<UserResponse>> ListUsersAsync(CancellationToken cancellationToken)
    {
        return TryGetTenantId(out Guid tenantId)
            ? await _repository.ListUsersAsync(tenantId, cancellationToken)
            : Array.Empty<UserResponse>();
    }

    public async Task<UserResponse?> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId)
            || string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.FullName)
            || string.IsNullOrWhiteSpace(request.Password)
            || request.Password.Length < 8
            || !UserStatuses.Contains(Normalize(request.Status)))
        {
            _logger.LogWarning("User creation rejected for tenant {TenantId}", _tenantContext.TenantId);
            return null;
        }

        string passwordHash = _passwordHasher.Hash(request.Password);
        UserResponse? response = await _repository.CreateUserAsync(
            tenantId,
            request with
            {
                Email = request.Email.Trim().ToLowerInvariant(),
                FullName = request.FullName.Trim(),
                Status = Normalize(request.Status),
                RoleCodes = NormalizeCodes(request.RoleCodes)
            },
            passwordHash,
            cancellationToken);

        if (response is not null)
        {
            await WriteAuditAsync(tenantId, "user.created", "user", response.Id, response, cancellationToken);
        }

        return response;
    }

    public async Task<UserResponse?> UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(out Guid tenantId)
            || (request.Email is not null && string.IsNullOrWhiteSpace(request.Email))
            || (request.FullName is not null && string.IsNullOrWhiteSpace(request.FullName))
            || (request.Password is not null && request.Password.Length < 8)
            || (request.Status is not null && !UserStatuses.Contains(Normalize(request.Status))))
        {
            _logger.LogWarning("User update rejected for tenant {TenantId} user {UserId}", _tenantContext.TenantId, userId);
            return null;
        }

        string? passwordHash = request.Password is null ? null : _passwordHasher.Hash(request.Password);
        UserResponse? response = await _repository.UpdateUserAsync(
            tenantId,
            userId,
            request with
            {
                Email = request.Email?.Trim().ToLowerInvariant(),
                FullName = request.FullName?.Trim(),
                Status = request.Status is null ? null : Normalize(request.Status),
                RoleCodes = NormalizeCodes(request.RoleCodes)
            },
            passwordHash,
            cancellationToken);

        if (response is not null)
        {
            await WriteAuditAsync(tenantId, "user.updated", "user", response.Id, response, cancellationToken);
        }

        return response;
    }

    public async Task<IReadOnlyCollection<RoleResponse>> ListRolesAsync(CancellationToken cancellationToken)
    {
        return TryGetTenantId(out Guid tenantId)
            ? await _repository.ListRolesAsync(tenantId, cancellationToken)
            : Array.Empty<RoleResponse>();
    }

    public Task<IReadOnlyCollection<PermissionResponse>> ListPermissionsAsync(CancellationToken cancellationToken)
    {
        return _repository.ListPermissionsAsync(cancellationToken);
    }

    private bool TryGetTenantId(out Guid tenantId)
    {
        tenantId = _tenantContext.TenantId ?? Guid.Empty;
        return tenantId != Guid.Empty;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static IReadOnlyCollection<string>? NormalizeCodes(IReadOnlyCollection<string>? codes)
    {
        return codes?.Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task WriteAuditAsync(Guid tenantId, string action, string entityType, Guid entityId, object payload, CancellationToken cancellationToken)
    {
        await _auditEventWriter.AppendAsync(
            tenantId,
            action,
            entityType,
            entityId,
            null,
            JsonSerializer.SerializeToElement(payload, JsonOptions),
            cancellationToken);
    }
}
