namespace SolidPOS.PosCore.Domain;

public sealed record LocalIntegrityIssue(string Code, string Severity, string Message, int Count);

public sealed record LocalIntegrityReport(
    string DatabasePath,
    bool IsOk,
    string SQLiteIntegrityCheck,
    int PendingOutboxEvents,
    int FailedOutboxEvents,
    int DeadLetterOutboxEvents,
    int PendingPrintJobs,
    int FailedPrintJobs,
    int OpenCashShifts,
    int ActiveSessions,
    int RecoveryJournalEntries,
    IReadOnlyList<LocalIntegrityIssue> Issues);

public sealed record LocalRecoveryJournalEntry(
    Guid Id,
    string Operation,
    string Status,
    string Message,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record LocalRecoveryResult(
    Guid JournalId,
    int OutboxEventsRepaired,
    int PrintJobsRepaired,
    int SessionsClosed,
    int CashShiftsFlagged,
    bool BackupCreated,
    string? BackupPath,
    string Message);

public sealed record LocalBackupResult(string BackupPath, long Bytes, DateTimeOffset CreatedAtUtc);
