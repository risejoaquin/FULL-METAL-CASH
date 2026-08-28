using System.Text.Json;

namespace SolidPOS.PosServer.Contracts.Tenants;

public sealed record UpdateTenantConfigRequest(
    string BusinessVertical,
    string UiLayout,
    JsonElement ModulesEnabled,
    JsonElement Branding,
    JsonElement ReceiptSettings,
    JsonElement HardwareProfile,
    JsonElement FeatureFlags,
    long? ExpectedVersion = null);
