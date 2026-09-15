using System.Text.RegularExpressions;

namespace SolidPOS.PosCore.Application.Updates;

public static class UpdateHealthState
{
    public const string Checked = "checked";
    public const string Downloaded = "downloaded";
    public const string Applied = "applied";
    public const string Failed = "failed";
    public const string RollbackTriggered = "rollback_triggered";
    public const string RollbackCompleted = "rollback_completed";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Checked,
        Downloaded,
        Applied,
        Failed,
        RollbackTriggered,
        RollbackCompleted
    };

    public static bool IsValid(string? state) =>
        !string.IsNullOrWhiteSpace(state) && All.Contains(state.Trim());
}

public sealed record UpdateHealthEvidence(
    string State,
    string CurrentVersion,
    string? TargetVersion = null,
    string? Channel = null,
    string? PackageFileName = null,
    string? PackageSha256 = null,
    bool? IsSigned = null,
    string? SigningThumbprint = null,
    string? ErrorMessage = null,
    DateTimeOffset? AttemptedAtUtc = null,
    DateTimeOffset? CompletedAtUtc = null,
    string? RollbackVersion = null,
    string? RollbackReason = null);

public static class UpdateHealthSanitizer
{
    private static readonly Regex BearerTokenRegex = new(
        @"Bearer\s+[A-Za-z0-9\-_\.=]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex JwtPatternRegex = new(
        @"\beyJ[A-Za-z0-9-_]+\.eyJ[A-Za-z0-9-_]+\.[A-Za-z0-9-_]+\b",
        RegexOptions.Compiled);

    private static readonly Regex KeyValueSecretRegex = new(
        @"(?i)\b(password|pwd|secret|token|apikey|api_key|access_token|private_key|authorization)\s*([:=]|is)\s*([^\s;,]+)",
        RegexOptions.Compiled);

    private static readonly Regex ConnectionStringRegex = new(
        @"(?i)(Server|Data Source|Host|Database|Initial Catalog|User Id|Uid|Password|Pwd)\s*=\s*[^;]+",
        RegexOptions.Compiled);

    private static readonly Regex CreditCardRegex = new(
        @"\b(?:\d[ -]*?){13,19}\b",
        RegexOptions.Compiled);

    public static string? Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var result = text;

        // Mask connection string parts
        result = ConnectionStringRegex.Replace(result, m =>
        {
            var key = m.Groups[1].Value;
            return $"{key}=[REDACTED]";
        });

        // Mask Bearer tokens
        result = BearerTokenRegex.Replace(result, "Bearer [REDACTED]");

        // Mask raw JWTs
        result = JwtPatternRegex.Replace(result, "[REDACTED_JWT]");

        // Mask generic key-value secrets
        result = KeyValueSecretRegex.Replace(result, m =>
        {
            var key = m.Groups[1].Value;
            var sep = m.Groups[2].Value;
            return $"{key}{sep}[REDACTED]";
        });

        // Mask credit cards / PANs (13-19 digits)
        result = CreditCardRegex.Replace(result, m =>
        {
            var digitsOnly = Regex.Replace(m.Value, @"\D", "");
            if (digitsOnly.Length >= 13 && digitsOnly.Length <= 19)
            {
                return "[REDACTED_PAN]";
            }
            return m.Value;
        });

        return result;
    }

    public static UpdateHealthEvidence SanitizeEvidence(UpdateHealthEvidence evidence)
    {
        return evidence with
        {
            ErrorMessage = Sanitize(evidence.ErrorMessage),
            RollbackReason = Sanitize(evidence.RollbackReason)
        };
    }

    public static bool ValidateEvidence(UpdateHealthEvidence evidence, out List<string> errors)
    {
        errors = new List<string>();

        if (string.IsNullOrWhiteSpace(evidence.State) || !UpdateHealthState.IsValid(evidence.State))
        {
            errors.Add($"Invalid or missing update health state: '{evidence.State}'.");
        }

        if (string.IsNullOrWhiteSpace(evidence.CurrentVersion))
        {
            errors.Add("currentVersion is required.");
        }

        if (!string.IsNullOrWhiteSpace(evidence.Channel))
        {
            var channel = evidence.Channel.Trim().ToLowerInvariant();
            if (channel != "stable" && channel != "beta" && channel != "dev")
            {
                errors.Add($"Invalid channel '{evidence.Channel}'. Supported channels are stable, beta, dev.");
            }
        }

        if (string.Equals(evidence.State, UpdateHealthState.Failed, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(evidence.ErrorMessage))
            {
                errors.Add("errorMessage is required when state is 'failed'.");
            }
        }

        if (string.Equals(evidence.State, UpdateHealthState.RollbackTriggered, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(evidence.State, UpdateHealthState.RollbackCompleted, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(evidence.RollbackVersion))
            {
                errors.Add("rollbackVersion is required when state is rollback_triggered or rollback_completed.");
            }
        }

        return errors.Count == 0;
    }
}
