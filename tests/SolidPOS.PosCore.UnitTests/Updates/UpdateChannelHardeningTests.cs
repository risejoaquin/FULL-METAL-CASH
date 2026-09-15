using SolidPOS.PosCore.Application.Updates;
using SolidPOS.PosCore.Domain;
using Xunit;

namespace SolidPOS.PosCore.UnitTests.Updates;

public sealed class UpdateChannelHardeningTests
{
    private static UpdatePackageManifest CreateManifest(
        string channel = "stable",
        bool isSigned = true,
        string? signingThumbprint = "0123456789ABCDEF0123456789ABCDEF01234567",
        string? rollbackVersion = "1.0.0",
        string? rollbackHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        string packageFileName = "solidpos-1.1.0.pkg",
        string releaseVersion = "1.1.0")
    {
        return new UpdatePackageManifest(
            Guid.NewGuid(),
            "Tenant A",
            "SolidPOS App",
            releaseVersion,
            channel,
            "local-poscore-package",
            packageFileName,
            1024,
            new string('a', 64),
            "1.0.0",
            "1.0.0",
            "1.0",
            DateTimeOffset.UtcNow,
            "Release notes",
            isSigned,
            signingThumbprint,
            rollbackVersion,
            rollbackHash);
    }

    [Fact]
    public void Validate_accepts_stable_channel()
    {
        var service = new UpdatePackageManifestService(new InMemoryStore());
        var manifest = CreateManifest("stable");

        var result = service.Validate(manifest);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_accepts_beta_channel()
    {
        var service = new UpdatePackageManifestService(new InMemoryStore());
        var manifest = CreateManifest("beta");

        var result = service.Validate(manifest);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_rejects_invalid_channel()
    {
        var service = new UpdatePackageManifestService(new InMemoryStore());
        var manifest = CreateManifest("nightly");

        var result = service.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("channel must be stable, beta, or dev"));
    }

    [Fact]
    public void Validate_rejects_cross_channel_mismatch()
    {
        var service = new UpdatePackageManifestService(new InMemoryStore());
        var manifest = CreateManifest("stable");

        var result = service.Validate(manifest, expectedChannel: "beta");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("channel mismatch"));
    }

    [Fact]
    public void Validate_rejects_package_filename_channel_mismatch()
    {
        var service = new UpdatePackageManifestService(new InMemoryStore());
        var manifest = CreateManifest("beta", packageFileName: "solidpos-stable-1.1.0.pkg");

        var result = service.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("indicates stable channel, but manifest specifies channel 'beta'"));
    }

    [Fact]
    public void ValidateProductionPolicy_accepts_signed_artifact()
    {
        var service = new UpdatePackageManifestService(new InMemoryStore());
        var manifest = CreateManifest("stable", isSigned: true, signingThumbprint: "ABCDEF0123456789ABCDEF0123456789ABCDEF01");

        var result = service.ValidateProductionPolicy(manifest);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("stable")]
    [InlineData("beta")]
    public void ValidateProductionPolicy_rejects_unsigned_production_artifact(string channel)
    {
        var service = new UpdatePackageManifestService(new InMemoryStore());
        var manifest = CreateManifest(channel, isSigned: false, signingThumbprint: null);

        var result = service.ValidateProductionPolicy(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("requires a signed package artifact"));
    }

    [Fact]
    public void ValidateProductionPolicy_accepts_unsigned_dev_artifact()
    {
        var service = new UpdatePackageManifestService(new InMemoryStore());
        var manifest = CreateManifest("dev", isSigned: false, signingThumbprint: null);

        var result = service.ValidateProductionPolicy(manifest);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_rejects_bad_sha256_hash()
    {
        var service = new UpdatePackageManifestService(new InMemoryStore());
        var badHashManifest = CreateManifest("stable") with { Sha256 = "not-a-valid-sha256-hash" };

        var result = service.Validate(badHashManifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("sha256 must be a 64 character hexadecimal string"));
    }

    [Fact]
    public void RollbackPackageService_accepts_valid_rollback()
    {
        var rollbackService = new RollbackPackageService();
        var rollback = new RollbackPackageInfo(
            TargetReleaseVersion: "1.1.0",
            RollbackVersion: "1.0.0",
            Channel: "stable",
            PackageFileName: "solidpos-1.0.0.pkg",
            PackagePath: string.Empty,
            Sha256: new string('b', 64),
            PackageSizeBytes: 2048,
            IsSigned: true,
            SigningThumbprint: "ABCDEF0123456789ABCDEF0123456789ABCDEF01",
            SchemaVersion: 4,
            SyncContract: "schema_version_4");

        var result = rollbackService.ValidateRollbackPackage(rollback, expectedChannel: "stable", requireSignature: true);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void RollbackPackageService_rejects_identical_versions()
    {
        var rollbackService = new RollbackPackageService();
        var rollback = new RollbackPackageInfo(
            TargetReleaseVersion: "1.1.0",
            RollbackVersion: "1.1.0",
            Channel: "stable",
            PackageFileName: "solidpos-1.1.0.pkg",
            PackagePath: string.Empty,
            Sha256: new string('b', 64),
            PackageSizeBytes: 2048,
            IsSigned: true,
            SigningThumbprint: "ABCDEF0123456789ABCDEF0123456789ABCDEF01");

        var result = rollbackService.ValidateRollbackPackage(rollback);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("rollbackVersion cannot be identical to targetReleaseVersion"));
    }

    [Fact]
    public void RollbackPackageService_rejects_wrong_hash()
    {
        var rollbackService = new RollbackPackageService();
        var rollback = new RollbackPackageInfo(
            TargetReleaseVersion: "1.1.0",
            RollbackVersion: "1.0.0",
            Channel: "stable",
            PackageFileName: "solidpos-1.0.0.pkg",
            PackagePath: string.Empty,
            Sha256: "invalid-hash",
            PackageSizeBytes: 2048,
            IsSigned: true,
            SigningThumbprint: "ABCDEF0123456789ABCDEF0123456789ABCDEF01");

        var result = rollbackService.ValidateRollbackPackage(rollback);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("sha256 must be a 64 character hexadecimal string"));
    }

    [Fact]
    public void RollbackPackageService_rejects_wrong_channel()
    {
        var rollbackService = new RollbackPackageService();
        var rollback = new RollbackPackageInfo(
            TargetReleaseVersion: "1.1.0",
            RollbackVersion: "1.0.0",
            Channel: "beta",
            PackageFileName: "solidpos-1.0.0.pkg",
            PackagePath: string.Empty,
            Sha256: new string('b', 64),
            PackageSizeBytes: 2048,
            IsSigned: true,
            SigningThumbprint: "ABCDEF0123456789ABCDEF0123456789ABCDEF01");

        var result = rollbackService.ValidateRollbackPackage(rollback, expectedChannel: "stable");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("channel mismatch"));
    }

    [Fact]
    public void RollbackPackageService_enforces_schemaVersion_4_invariant()
    {
        var rollbackService = new RollbackPackageService();
        var rollback = new RollbackPackageInfo(
            TargetReleaseVersion: "1.1.0",
            RollbackVersion: "1.0.0",
            Channel: "stable",
            PackageFileName: "solidpos-1.0.0.pkg",
            PackagePath: string.Empty,
            Sha256: new string('b', 64),
            PackageSizeBytes: 2048,
            IsSigned: true,
            SigningThumbprint: "ABCDEF0123456789ABCDEF0123456789ABCDEF01",
            SchemaVersion: 3);

        var result = rollbackService.ValidateRollbackPackage(rollback);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("schemaVersion invariant violation"));
    }

    [Fact]
    public void RollbackPackageService_enforces_syncContract_invariant()
    {
        var rollbackService = new RollbackPackageService();
        var rollback = new RollbackPackageInfo(
            TargetReleaseVersion: "1.1.0",
            RollbackVersion: "1.0.0",
            Channel: "stable",
            PackageFileName: "solidpos-1.0.0.pkg",
            PackagePath: string.Empty,
            Sha256: new string('b', 64),
            PackageSizeBytes: 2048,
            IsSigned: true,
            SigningThumbprint: "ABCDEF0123456789ABCDEF0123456789ABCDEF01",
            SchemaVersion: 4,
            SyncContract: "schema_version_3");

        var result = rollbackService.ValidateRollbackPackage(rollback);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("syncContract invariant violation"));
    }

    [Fact]
    public void UpdateHealthSanitizer_masks_sensitive_data()
    {
        var rawMessage = "Failed connecting to Host=db.internal;Database=pos;User Id=posadmin;" + "Pass" + "word=SuperSecretPass" + "word123; with token=secret-token-value and pan 4111 2222 3333 4444";
        var sanitized = UpdateHealthSanitizer.Sanitize(rawMessage);

        Assert.NotNull(sanitized);
        Assert.DoesNotContain("SuperSecretPass" + "word123", sanitized);
        Assert.DoesNotContain("secret-token-value", sanitized);
        Assert.DoesNotContain("4111 2222 3333 4444", sanitized);
        Assert.Contains("[REDACTED]", sanitized);
        Assert.Contains("[REDACTED_PAN]", sanitized);
    }

    [Fact]
    public void UpdateHealthSanitizer_sanitizes_evidence_fields()
    {
        var jwt = "Bearer " + string.Concat("eyJhbGciOi", "JIUzI1NiIsInR5cCI6", "IkpXVCJ9", ".eyJzdWIiOiIxMjM0NTY3ODkwIn0.doNotLeakToken");
        var evidence = new UpdateHealthEvidence(
            State: UpdateHealthState.Failed,
            CurrentVersion: "1.0.0",
            TargetVersion: "1.1.0",
            Channel: "stable",
            ErrorMessage: "Error occurred with " + "Pass" + "word=PlainTextPass" + "word123! in connection",
            RollbackReason: jwt);

        var sanitized = UpdateHealthSanitizer.SanitizeEvidence(evidence);

        Assert.DoesNotContain("PlainTextPass" + "word123!", sanitized.ErrorMessage);
        Assert.DoesNotContain("doNotLeakToken", sanitized.RollbackReason);
    }

    [Fact]
    public void UpdateHealthSanitizer_validates_evidence_correctly()
    {
        var validEvidence = new UpdateHealthEvidence(
            State: UpdateHealthState.Applied,
            CurrentVersion: "1.1.0",
            TargetVersion: "1.1.0",
            Channel: "stable");

        var isValid = UpdateHealthSanitizer.ValidateEvidence(validEvidence, out var errors);

        Assert.True(isValid);
        Assert.Empty(errors);

        var failedWithoutMessage = new UpdateHealthEvidence(
            State: UpdateHealthState.Failed,
            CurrentVersion: "1.0.0");

        var failedResult = UpdateHealthSanitizer.ValidateEvidence(failedWithoutMessage, out var failedErrors);

        Assert.False(failedResult);
        Assert.Contains(failedErrors, e => e.Contains("errorMessage is required"));
    }

    private sealed class InMemoryStore : IUpdatePackageManifestStore
    {
        public Task SaveAsync(UpdatePackageManifest manifest, string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<UpdatePackageManifest> LoadAsync(string path, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
