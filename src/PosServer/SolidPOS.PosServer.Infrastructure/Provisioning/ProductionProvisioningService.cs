using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SolidPOS.PosServer.Application.Auth;
using SolidPOS.PosServer.Application.Provisioning;
using SolidPOS.PosServer.Contracts.Provisioning;

namespace SolidPOS.PosServer.Infrastructure.Provisioning;

public sealed class ProductionProvisioningService : IProductionProvisioningService
{
    private const string RequiredHeader = "X-SolidPOS-Provision-Key";

    private readonly IProductionProvisioningRepository _repository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPasswordPolicyValidator _passwordPolicyValidator;
    private readonly IConfiguration _configuration;
    private readonly ProductionProvisioningOptions _options;
    private readonly ILogger<ProductionProvisioningService> _logger;

    public ProductionProvisioningService(
        IProductionProvisioningRepository repository,
        IPasswordHasher passwordHasher,
        IPasswordPolicyValidator passwordPolicyValidator,
        IConfiguration configuration,
        IOptions<ProductionProvisioningOptions> options,
        ILogger<ProductionProvisioningService> logger)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
        _passwordPolicyValidator = passwordPolicyValidator;
        _configuration = configuration;
        _options = options.Value;
        _logger = logger;
    }

    public Task<ProductionBootstrapStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        string? bootstrapKey = ResolveBootstrapKey();
        bool configured = !string.IsNullOrWhiteSpace(bootstrapKey);
        bool enabled = _options.AllowProductionBootstrap && configured;
        string message = enabled
            ? "Production provisioning is configured. POST requires the bootstrap header."
            : "Production provisioning is disabled or missing bootstrap key configuration.";

        return Task.FromResult(new ProductionBootstrapStatusResponse(enabled, configured, RequiredHeader, message));
    }

    public async Task<ProductionTenantBootstrapResponse?> BootstrapTenantAsync(
        ProductionTenantBootstrapRequest request,
        string? providedBootstrapKey,
        CancellationToken cancellationToken)
    {
        string? expectedBootstrapKey = ResolveBootstrapKey();
        if (!_options.AllowProductionBootstrap || string.IsNullOrWhiteSpace(expectedBootstrapKey) || !FixedTimeEquals(expectedBootstrapKey, providedBootstrapKey))
        {
            _logger.LogWarning("Production tenant bootstrap rejected because the provision key is missing or invalid");
            return null;
        }

        if (!IsValidRequest(request))
        {
            _logger.LogWarning("Production tenant bootstrap rejected because the request is invalid");
            return null;
        }

        PasswordPolicyResult passwordPolicy = _passwordPolicyValidator.Validate(request.AdminPassword);
        if (!passwordPolicy.IsValid)
        {
            _logger.LogWarning("Production tenant bootstrap rejected by password policy: {PolicyErrors}", string.Join(",", passwordPolicy.Errors));
            return null;
        }

        string passwordHash = _passwordHasher.Hash(request.AdminPassword);
        bool disableDemoUser = request.DisableDemoUser && _options.DisableDemoUserByDefault;
        return await _repository.BootstrapTenantAsync(request, passwordHash, disableDemoUser, cancellationToken);
    }

    private string? ResolveBootstrapKey()
    {
        return _options.BootstrapKey
            ?? _configuration["Provisioning:BootstrapKey"]
            ?? _configuration["PROVISION_KEY"];
    }

    private static bool IsValidRequest(ProductionTenantBootstrapRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.TenantName)
            && !string.IsNullOrWhiteSpace(request.AdminEmail)
            && request.AdminEmail.Contains('@', StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(request.AdminFullName)
            && !string.IsNullOrWhiteSpace(request.AdminPassword)
            && !string.IsNullOrWhiteSpace(request.StoreCode)
            && !string.IsNullOrWhiteSpace(request.StoreName)
            && request.Currency.Trim().Length == 3
            && !string.IsNullOrWhiteSpace(request.Timezone)
            && !string.IsNullOrWhiteSpace(request.BusinessVertical)
            && !string.IsNullOrWhiteSpace(request.UiLayout);
    }

    private static bool FixedTimeEquals(string expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        byte[] expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        byte[] actualBytes = System.Text.Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
