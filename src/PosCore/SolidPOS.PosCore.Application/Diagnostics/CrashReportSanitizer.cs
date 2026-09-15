using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SolidPOS.PosCore.Application.Diagnostics;

public static class CrashReportSanitizer
{
    private const int MaxMessageLength = 2048;
    private const int MaxStackTraceLength = 8192;

    private static readonly Regex PostgresUrlRegex = new(
        @"postgres(?:ql)?://[^\s:@/]+:[^\s:@/]+@[^\s]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NpgsqlPasswordRegex = new(
        @"(?i)(Host|Server)=[^;]+;(Password|Pwd)=[^;\s]+",
        RegexOptions.Compiled);

    private static readonly Regex GenericConnectionStringRegex = new(
        @"(?i)(Data Source|Server|Host|Initial Catalog|Database)=[^;]+;.*?(Password|Pwd|User Id|Uid)=[^;\s]+",
        RegexOptions.Compiled);

    private static readonly Regex BearerTokenRegex = new(
        @"Bearer\s+[A-Za-z0-9_\-\.\+/=]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex JwtRegex = new(
        @"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]+\b",
        RegexOptions.Compiled);

    private static readonly Regex RailwayTokenRegex = new(
        @"(?i)railway[_-]?(token|key|secret)\s*[:=]\s*[^\s,;""'\n]+",
        RegexOptions.Compiled);

    private static readonly Regex AwsKeyRegex = new(
        @"\bAKIA[0-9A-Z]{16}\b",
        RegexOptions.Compiled);

    private static readonly Regex PrivateKeyBlockRegex = new(
        @"-----BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----[\s\S]*?-----END (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----",
        RegexOptions.Compiled);

    private static readonly Regex PrivateKeyHeaderRegex = new(
        @"-----BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----",
        RegexOptions.Compiled);

    private static readonly Regex JwtSigningKeyLiteralRegex = new(
        @"(?i)(Jwt__SigningKey|Jwt:SigningKey|SigningKey)\s*[:=]\s*[A-Za-z0-9+/=_-]{16,}",
        RegexOptions.Compiled);

    private static readonly Regex ProvisionKeyRegex = new(
        @"(?i)(PROVISION_KEY|Provisioning__BootstrapKey|BootstrapKey|provision[_-]?key)\s*[:=]\s*[A-Za-z0-9+/=_-]{16,}",
        RegexOptions.Compiled);

    private static readonly Regex ApiKeyRegex = new(
        @"(?i)(api[_-]?key|apikey|private[_-]?key)\s*[:=]\s*[A-Za-z0-9+/=_-]{16,}",
        RegexOptions.Compiled);

    private static readonly Regex PasswordAssignmentRegex = new(
        @"(?i)(password|passwd|pwd|client_secret)\s*[:=]\s*([""']?)[^\s,;""']+\2",
        RegexOptions.Compiled);

    private static readonly Regex JsonPasswordPropertyRegex = new(
        @"""(password|passwd|pwd|secret|token|signingKey|bootstrapKey|provisionKey)""\s*:\s*""[^""]*""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex EmailRegex = new(
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled);

    private static readonly Regex CreditCardPanRegex = new(
        @"\b(?:\d[ -]*?){13,19}\b",
        RegexOptions.Compiled);

    private static readonly Regex WindowsUserPathRegex = new(
        @"([\\/][Uu]sers[\\/])[^\\/]+",
        RegexOptions.Compiled);

    private static readonly Regex UnixUserPathRegex = new(
        @"([\\/]home[\\/])[^\\/]+",
        RegexOptions.Compiled);

    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var result = text;

        // 1. Private keys
        result = PrivateKeyBlockRegex.Replace(result, "[REDACTED_PRIVATE_KEY_BLOCK]");
        result = PrivateKeyHeaderRegex.Replace(result, "[REDACTED_PRIVATE_KEY]");

        // 2. Database connections
        result = PostgresUrlRegex.Replace(result, "[REDACTED_DATABASE_URL]");
        result = NpgsqlPasswordRegex.Replace(result, "[REDACTED_CONNECTION_STRING]");
        result = GenericConnectionStringRegex.Replace(result, "[REDACTED_CONNECTION_STRING]");

        // 3. Tokens and secrets
        result = BearerTokenRegex.Replace(result, "Bearer [REDACTED_TOKEN]");
        result = RailwayTokenRegex.Replace(result, "[REDACTED_RAILWAY_TOKEN]");
        result = JwtRegex.Replace(result, "[REDACTED_JWT]");
        result = AwsKeyRegex.Replace(result, "[REDACTED_AWS_KEY]");
        result = JwtSigningKeyLiteralRegex.Replace(result, "[REDACTED_JWT_SIGNING_KEY]");
        result = ProvisionKeyRegex.Replace(result, "[REDACTED_PROVISION_KEY]");
        result = ApiKeyRegex.Replace(result, "[REDACTED_API_KEY]");

        // 4. Passwords
        result = PasswordAssignmentRegex.Replace(result, "$1=[REDACTED_PASSWORD]");
        result = JsonPasswordPropertyRegex.Replace(result, "\"$1\": \"[REDACTED]\"");

        // 5. Payment PANs (13-19 digits)
        result = CreditCardPanRegex.Replace(result, m =>
        {
            var digitsOnly = Regex.Replace(m.Value, @"\D", "");
            if (digitsOnly.Length >= 13 && digitsOnly.Length <= 19)
            {
                return "[REDACTED_PAN]";
            }
            return m.Value;
        });

        // 6. Emails
        result = EmailRegex.Replace(result, "[REDACTED_EMAIL]");

        // 7. Filesystem usernames in paths
        result = WindowsUserPathRegex.Replace(result, "$1[REDACTED_USER]");
        result = UnixUserPathRegex.Replace(result, "$1[REDACTED_USER]");

        return result;
    }

    public static string SanitizeMessage(string? message)
    {
        if (string.IsNullOrEmpty(message)) return "Unknown error occurred.";
        var sanitized = Sanitize(message);
        return sanitized.Length <= MaxMessageLength ? sanitized : sanitized[..MaxMessageLength] + "...[TRUNCATED]";
    }

    public static string SanitizeStackTrace(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace)) return string.Empty;
        var sanitized = Sanitize(stackTrace);
        return sanitized.Length <= MaxStackTraceLength ? sanitized : sanitized[..MaxStackTraceLength] + "...[TRUNCATED]";
    }

    public static IReadOnlyDictionary<string, string>? SanitizeMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null) return null;
        var sanitized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in metadata)
        {
            sanitized[Sanitize(k)] = Sanitize(v);
        }
        return sanitized;
    }
}
