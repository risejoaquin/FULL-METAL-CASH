using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Receipts;
using SolidPOS.PosServer.Application.Sales;
using SolidPOS.PosServer.Contracts.Receipts;

namespace SolidPOS.PosServer.Infrastructure.Receipts;

public sealed class DigitalReceiptService : IDigitalReceiptService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITenantContext _tenantContext;
    private readonly IDigitalReceiptRepository _repository;
    private readonly ISalesRepository _salesRepository;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ILogger<DigitalReceiptService> _logger;

    public DigitalReceiptService(
        ITenantContext tenantContext,
        IDigitalReceiptRepository repository,
        ISalesRepository salesRepository,
        IAuditEventWriter auditEventWriter,
        ILogger<DigitalReceiptService> logger)
    {
        _tenantContext = tenantContext;
        _repository = repository;
        _salesRepository = salesRepository;
        _auditEventWriter = auditEventWriter;
        _logger = logger;
    }

    public async Task<DigitalReceiptResponse?> IssueAsync(
        Guid saleId,
        IssueDigitalReceiptRequest? request,
        string publicBaseUrl,
        CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || saleId == Guid.Empty || !IssueRequestIsValid(request))
        {
            _logger.LogWarning("Digital receipt issue rejected because tenant, sale id, or request is invalid");
            return null;
        }

        Guid tenantId = _tenantContext.TenantId.Value;
        ReceiptResponse? receipt = await _salesRepository.GetReceiptAsync(tenantId, saleId, cancellationToken);
        if (receipt is null)
        {
            _logger.LogWarning("Digital receipt issue rejected because sale {SaleId} was not found for tenant {TenantId}", saleId, tenantId);
            return null;
        }

        string publicToken = CreatePublicToken();
        string publicUrl = BuildPublicUrl(publicBaseUrl, publicToken);
        string receiptNumber = CreateReceiptNumber(receipt);
        DigitalReceiptRecord? record = await _repository.IssueAsync(
            tenantId,
            saleId,
            receiptNumber,
            HashToken(publicToken),
            publicUrl,
            request?.ExpiresAt,
            cancellationToken);

        if (record is null)
        {
            return null;
        }

        DigitalReceiptResponse response = ToResponse(record, receipt);
        await WriteAuditAsync(tenantId, "receipt.issued", "digital_receipt", record.Id, response, cancellationToken);

        _logger.LogInformation(
            "Digital receipt {DigitalReceiptId} issued for sale {SaleId} tenant {TenantId}",
            record.Id,
            saleId,
            tenantId);

        return response;
    }

    public async Task<DigitalReceiptResponse?> GetBySaleIdAsync(Guid saleId, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || saleId == Guid.Empty)
        {
            _logger.LogWarning("Digital receipt read rejected because tenant context or sale id is missing");
            return null;
        }

        Guid tenantId = _tenantContext.TenantId.Value;
        DigitalReceiptRecord? record = await _repository.GetBySaleIdAsync(tenantId, saleId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        ReceiptResponse? receipt = await _salesRepository.GetReceiptAsync(tenantId, record.SaleId, cancellationToken);
        return receipt is null ? null : ToResponse(record, receipt);
    }

    public async Task<DigitalReceiptResponse?> GetPublicAsync(string publicToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publicToken) || publicToken.Length < 32)
        {
            return null;
        }

        DigitalReceiptRecord? record = await _repository.GetByPublicTokenHashAsync(HashToken(publicToken), cancellationToken);
        if (record is null)
        {
            return null;
        }

        ReceiptResponse? receipt = await _salesRepository.GetReceiptAsync(record.TenantId, record.SaleId, cancellationToken);
        return receipt is null ? null : ToResponse(record, receipt);
    }

    public async Task<EmailReceiptResponse?> EmailStubAsync(
        Guid saleId,
        EmailReceiptRequest request,
        string publicBaseUrl,
        CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || saleId == Guid.Empty || !EmailRequestIsValid(request))
        {
            _logger.LogWarning("Digital receipt email stub rejected because tenant, sale id, or recipient is invalid");
            return null;
        }

        DigitalReceiptResponse? issued = await IssueAsync(saleId, new IssueDigitalReceiptRequest(), publicBaseUrl, cancellationToken);
        if (issued is null)
        {
            return null;
        }

        string recipient = request.RecipientEmail.Trim();
        DigitalReceiptRecord? record = await _repository.MarkEmailStubSentAsync(
            _tenantContext.TenantId.Value,
            saleId,
            recipient,
            cancellationToken);
        if (record is null)
        {
            return null;
        }

        EmailReceiptResponse response = new(
            record.Id,
            record.SaleId,
            record.ReceiptNumber,
            recipient,
            "queued_stub",
            record.LastSentAt ?? DateTimeOffset.UtcNow,
            "Email delivery is stubbed in Macro Phase 24; the digital receipt was recorded and marked for resend.");

        await WriteAuditAsync(record.TenantId, "receipt.email_stub_queued", "digital_receipt", record.Id, response, cancellationToken);

        _logger.LogInformation(
            "Digital receipt {DigitalReceiptId} email stub queued for sale {SaleId} tenant {TenantId}",
            record.Id,
            saleId,
            record.TenantId);

        return response;
    }

    private static bool IssueRequestIsValid(IssueDigitalReceiptRequest? request)
    {
        return request?.ExpiresAt is null || request.ExpiresAt.Value > DateTimeOffset.UtcNow;
    }

    private static bool EmailRequestIsValid(EmailReceiptRequest request)
    {
        string recipient = request.RecipientEmail?.Trim() ?? string.Empty;
        return recipient.Length is >= 5 and <= 254 && recipient.Contains("@", StringComparison.Ordinal) && !recipient.Contains(" ", StringComparison.Ordinal);
    }

    private Task WriteAuditAsync<T>(Guid tenantId, string action, string entityType, Guid entityId, T afterData, CancellationToken cancellationToken)
    {
        return _auditEventWriter.AppendAsync(
            tenantId,
            action,
            entityType,
            entityId,
            null,
            JsonSerializer.SerializeToElement(afterData, JsonOptions),
            cancellationToken);
    }

    private static DigitalReceiptResponse ToResponse(DigitalReceiptRecord record, ReceiptResponse receipt)
    {
        return new DigitalReceiptResponse(
            record.Id,
            record.TenantId,
            record.SaleId,
            record.ReceiptNumber,
            ExtractPublicToken(record.PublicUrl),
            record.PublicUrl,
            record.Status,
            record.ExpiresAt,
            record.IssuedAt,
            record.CreatedAt,
            record.RevokedAt,
            record.LastSentAt,
            record.LastSentEmail,
            record.SendCount,
            receipt);
    }

    private static string CreateReceiptNumber(ReceiptResponse receipt)
    {
        string datePart = receipt.OccurredAt.UtcDateTime.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        string salePart = receipt.SaleId.ToString("N")[..8].ToUpperInvariant();
        return $"SP-{datePart}-{salePart}";
    }

    private static string CreatePublicToken()
    {
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return token.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string HashToken(string token)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildPublicUrl(string publicBaseUrl, string publicToken)
    {
        string trimmed = publicBaseUrl.TrimEnd('/');
        return $"{trimmed}/api/v1/receipts/public/{publicToken}";
    }

    private static string ExtractPublicToken(string publicUrl)
    {
        int index = publicUrl.LastIndexOf("/", StringComparison.Ordinal);
        return index < 0 || index == publicUrl.Length - 1 ? string.Empty : publicUrl[(index + 1)..];
    }
}
