using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SolidPOS.PosCore.Application.Updates;

public sealed record RollbackPackageInfo(
    string TargetReleaseVersion,
    string RollbackVersion,
    string Channel,
    string PackageFileName,
    string PackagePath,
    string Sha256,
    long PackageSizeBytes,
    bool IsSigned,
    string? SigningThumbprint,
    int SchemaVersion = 4,
    string SyncContract = "schema_version_4");

public sealed record RollbackPackageValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors);

public sealed class RollbackPackageService
{
    private static readonly Regex Sha256Hex = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);
    private static readonly HashSet<string> SupportedChannels = new(StringComparer.OrdinalIgnoreCase) { "stable", "beta", "dev" };
    private static readonly HashSet<string> ProductionChannels = new(StringComparer.OrdinalIgnoreCase) { "stable", "beta" };

    public RollbackPackageValidationResult ValidateRollbackPackage(
        RollbackPackageInfo rollback,
        string? expectedChannel = null,
        bool requireSignature = true)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(rollback.TargetReleaseVersion))
        {
            errors.Add("targetReleaseVersion is required.");
        }

        if (string.IsNullOrWhiteSpace(rollback.RollbackVersion))
        {
            errors.Add("rollbackVersion is required.");
        }

        if (string.Equals(rollback.TargetReleaseVersion?.Trim(), rollback.RollbackVersion?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("rollbackVersion cannot be identical to targetReleaseVersion.");
        }

        if (string.IsNullOrWhiteSpace(rollback.Channel) || !SupportedChannels.Contains(rollback.Channel.Trim()))
        {
            errors.Add("channel must be stable, beta, or dev.");
        }

        if (!string.IsNullOrWhiteSpace(expectedChannel))
        {
            var normalizedExpected = expectedChannel.Trim().ToLowerInvariant();
            if (!string.Equals(rollback.Channel?.Trim(), normalizedExpected, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"channel mismatch: rollback channel '{rollback.Channel}' does not match expected channel '{normalizedExpected}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(rollback.PackageFileName))
        {
            errors.Add("packageFileName is required.");
        }

        if (rollback.PackageSizeBytes <= 0)
        {
            errors.Add("packageSizeBytes must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(rollback.Sha256) || !Sha256Hex.IsMatch(rollback.Sha256))
        {
            errors.Add("sha256 must be a 64 character hexadecimal string.");
        }

        if (rollback.SchemaVersion != 4)
        {
            errors.Add($"schemaVersion invariant violation: rollback package requires schemaVersion 4, found {rollback.SchemaVersion}.");
        }

        if (!string.Equals(rollback.SyncContract, "schema_version_4", StringComparison.Ordinal))
        {
            errors.Add($"syncContract invariant violation: rollback package requires schema_version_4, found '{rollback.SyncContract}'.");
        }

        if (requireSignature && ProductionChannels.Contains(rollback.Channel ?? string.Empty))
        {
            if (!rollback.IsSigned || string.IsNullOrWhiteSpace(rollback.SigningThumbprint))
            {
                errors.Add($"production channel '{rollback.Channel}' rollback package requires a signed artifact with valid thumbprint.");
            }
        }

        if (!string.IsNullOrWhiteSpace(rollback.PackagePath))
        {
            if (!File.Exists(rollback.PackagePath))
            {
                errors.Add("rollback package file referenced does not exist.");
            }
            else
            {
                var fileInfo = new FileInfo(rollback.PackagePath);
                if (fileInfo.Length != rollback.PackageSizeBytes)
                {
                    errors.Add("rollback package size does not match expected size.");
                }

                var actualSha256 = ComputeSha256(rollback.PackagePath);
                if (!string.Equals(actualSha256, rollback.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("rollback package sha256 does not match expected hash.");
                }
            }
        }

        return new RollbackPackageValidationResult(errors.Count == 0, errors);
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
