namespace SolidPOS.PosServer.Contracts.Receipts;

public sealed record EmailReceiptRequest(
    string RecipientEmail);
