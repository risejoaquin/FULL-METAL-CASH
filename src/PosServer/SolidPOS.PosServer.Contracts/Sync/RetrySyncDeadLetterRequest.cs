namespace SolidPOS.PosServer.Contracts.Sync;

public sealed record RetrySyncDeadLetterRequest(
    string Reason);
