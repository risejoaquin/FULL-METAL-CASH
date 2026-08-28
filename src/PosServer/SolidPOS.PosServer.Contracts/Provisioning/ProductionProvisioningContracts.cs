using System.Text.Json;

namespace SolidPOS.PosServer.Contracts.Provisioning;

public sealed record ProductionTenantBootstrapRequest(
    string TenantName,
    string AdminEmail,
    string AdminFullName,
    string AdminPassword,
    string StoreCode,
    string StoreName,
    string? LegalName = null,
    string Timezone = "America/Hermosillo",
    string Currency = "MXN",
    string BusinessVertical = "cafeteria",
    string UiLayout = "qsr_touch",
    JsonElement? Branding = null,
    JsonElement? ReceiptSettings = null,
    JsonElement? HardwareProfile = null,
    JsonElement? FeatureFlags = null,
    JsonElement? ModulesEnabled = null,
    string? StoreAddress = null,
    string? StorePhone = null,
    Guid? TenantId = null,
    string? IdempotencyKey = null,
    bool DisableDemoUser = true);

public sealed record ProductionTenantBootstrapResponse(
    Guid TenantId,
    Guid StoreId,
    Guid AdminUserId,
    string TenantName,
    string AdminEmail,
    string StoreCode,
    bool WasExisting,
    bool DemoUserDisabled,
    string Message);

public sealed record ProductionBootstrapStatusResponse(
    bool Enabled,
    bool Configured,
    string RequiredHeader,
    string Message);
