using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SolidPOS.DiscountEngine;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.OfflineSales;

/// <summary>
/// A cart discount issued by a separate authenticated central authority.
/// The signature covers ALL fields except SignatureBase64. Pinned public keys
/// are distributed out of band; do not accept keys from a client-supplied rule.
/// </summary>
public sealed record SignedOfflineCartDiscountRule(
    Guid TenantId, Guid StoreId, Guid TerminalId, Guid DiscountId,
    long Revision, DateTimeOffset IssuedAtUtc, DateTimeOffset ValidFromUtc,
    DateTimeOffset ExpiresAtUtc, string Kind, decimal Amount,
    bool RequiresApproval, bool CanCombine, string KeyId, string SignatureBase64);

public interface ISignedOfflineCartDiscountRuleCache
{
    /// <summary>Persist atomically with a strictly increasing revision per scoped rule.</summary>
    Task SaveAsync(SignedOfflineCartDiscountRule rule, CancellationToken cancellationToken = default);

    Task<SignedOfflineCartDiscountRule?> LoadAsync(
        Guid tenantId, Guid storeId, Guid terminalId, Guid discountId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A read-only verifier. No private signing keys may exist on an offline POS.
/// Currently supplied with pinned ephemeral public keys in tests only.
/// </summary>
public sealed class PinnedOfflineDiscountSignatureVerifier
{
    private readonly Dictionary<string, byte[]> _trustedSubjectPublicKeys;

    public PinnedOfflineDiscountSignatureVerifier(IReadOnlyDictionary<string, byte[]> trustedSubjectPublicKeys)
    {
        ArgumentNullException.ThrowIfNull(trustedSubjectPublicKeys);
        if (trustedSubjectPublicKeys.Count is < 1 or > 8)
            throw new ArgumentException("A small nonempty allowlist of trusted authority keys is required.");
        _trustedSubjectPublicKeys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (keyId, key) in trustedSubjectPublicKeys)
        {
            if (!IsValidKeyId(keyId) || key is null || key.Length is < 64 or > 256)
                throw new ArgumentException("Invalid trusted authority key entry.");
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(key, out var read);
            if (read != key.Length || ecdsa.KeySize != 256)
                throw new ArgumentException("Only complete ECDSA P-256 public keys are supported.");
            _trustedSubjectPublicKeys.Add(keyId, key.ToArray());
        }
    }

    public void Verify(SignedOfflineCartDiscountRule rule, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.TenantId == Guid.Empty || rule.StoreId == Guid.Empty ||
            rule.TerminalId == Guid.Empty || rule.DiscountId == Guid.Empty ||
            rule.Revision <= 0 || !IsValidKeyId(rule.KeyId) ||
            rule.Kind is not ("percentage" or "fixed_amount") ||
            rule.ValidFromUtc >= rule.ExpiresAtUtc ||
            rule.IssuedAtUtc > nowUtc.AddMinutes(2) ||
            rule.ExpiresAtUtc <= rule.IssuedAtUtc ||
            rule.ExpiresAtUtc - rule.IssuedAtUtc > TimeSpan.FromDays(7) ||
            rule.Amount <= 0 ||
            (rule.Kind == "percentage" && rule.Amount > 100m) ||
            (rule.Kind == "fixed_amount" &&
                (rule.Amount > 900_000_000_000_000m ||
                 rule.Amount != decimal.Truncate(rule.Amount))))
            throw new UnauthorizedAccessException("Signed cart rule has invalid identity, validity or financial bounds.");
        if (!_trustedSubjectPublicKeys.TryGetValue(rule.KeyId, out var publicKey))
            throw new UnauthorizedAccessException("Unknown offline discount authority key.");
        byte[] signature;
        try
        {
            if (string.IsNullOrWhiteSpace(rule.SignatureBase64) || rule.SignatureBase64.Length > 256)
                throw new FormatException("Invalid signature");
            signature = Convert.FromBase64String(rule.SignatureBase64);
        }
        catch (FormatException)
        {
            throw new UnauthorizedAccessException("Malformed offline discount signature.");
        }
        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(publicKey, out _);
        if (!verifier.VerifyData(EncodeUnsigned(rule), signature, HashAlgorithmName.SHA256))
            throw new UnauthorizedAccessException("Offline discount rule signature is invalid.");
    }

    private static bool IsValidKeyId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 64 &&
        value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    /// <summary>
    /// Versioned, delimiter-safe binary encoding. Exposing the exact message
    /// is safe; ONLY the central authority signs it with its private key.
    /// </summary>
    public static byte[] EncodeUnsigned(SignedOfflineCartDiscountRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true);
        writer.Write("solidpos.offline.cart-discount.p256.v1");
        writer.Write(rule.TenantId.ToByteArray());
        writer.Write(rule.StoreId.ToByteArray());
        writer.Write(rule.TerminalId.ToByteArray());
        writer.Write(rule.DiscountId.ToByteArray());
        writer.Write(rule.Revision);
        writer.Write(rule.IssuedAtUtc.ToUniversalTime().Ticks);
        writer.Write(rule.ValidFromUtc.ToUniversalTime().Ticks);
        writer.Write(rule.ExpiresAtUtc.ToUniversalTime().Ticks);
        writer.Write(rule.Kind ?? string.Empty);
        foreach (int piece in decimal.GetBits(rule.Amount)) writer.Write(piece);
        writer.Write(rule.RequiresApproval);
        writer.Write(rule.CanCombine);
        writer.Write(rule.KeyId ?? string.Empty);
        writer.Flush();
        return memory.ToArray();
    }
}

/// <summary>
/// The only public finalization route for a signed, non-approval-required
/// offline cart rule. Supervisor-only discounts remain fail-closed.
/// It never changes the remote v4 event contract.
/// </summary>
public sealed class SignedOfflineCartDiscountCheckout
{
    private readonly ISignedOfflineCartDiscountRuleCache _cache;
    private readonly PinnedOfflineDiscountSignatureVerifier _verifier;
    private readonly IClock _clock;

    public SignedOfflineCartDiscountCheckout(
        ISignedOfflineCartDiscountRuleCache cache,
        PinnedOfflineDiscountSignatureVerifier verifier,
        IClock clock)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task PublishAsync(
        SignedOfflineCartDiscountRule signedRule,
        CancellationToken cancellationToken = default)
    {
        _verifier.Verify(signedRule, _clock.UtcNow.ToUniversalTime());
        await _cache.SaveAsync(signedRule, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OfflineSaleDraft> ApplyAsync(
        OfflineSaleDraft sale, Guid discountId, IReadOnlyList<OfflineSalePaymentDraft> payments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sale);
        if (sale.TenantId == Guid.Empty || sale.StoreId == Guid.Empty ||
            sale.TerminalId == Guid.Empty || discountId == Guid.Empty)
            throw new UnauthorizedAccessException("A complete sale and discount scope is required.");
        var rule = await _cache.LoadAsync(
            sale.TenantId, sale.StoreId, sale.TerminalId, discountId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No signed discount cached for this terminal.");

        DateTimeOffset now = _clock.UtcNow.ToUniversalTime();
        _verifier.Verify(rule, now); // detects altered or corrupted rows on EVERY checkout
        if (rule.TenantId != sale.TenantId || rule.StoreId != sale.StoreId ||
            rule.TerminalId != sale.TerminalId || rule.DiscountId != discountId ||
            now < rule.ValidFromUtc || now >= rule.ExpiresAtUtc)
            throw new UnauthorizedAccessException("Offline discount is not valid for this terminal or time.");
        if (rule.RequiresApproval)
            throw new UnauthorizedAccessException("Offline supervisor discounts are disabled pending independent authorization.");
        return OfflineDiscountQuoteService.ApplyCartDiscount(
            sale, new DiscountRule(rule.DiscountId, "cart", rule.Kind, rule.Amount,
                RequiresApproval: rule.RequiresApproval, CanCombine: rule.CanCombine),
            payments);
    }
}
