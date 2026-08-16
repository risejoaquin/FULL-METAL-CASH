namespace SolidPOS.PosServer.Contracts.Discounts;

public sealed record ValidateDiscountResponse(
    Guid DiscountId,
    bool IsValid,
    string? RejectionReason,
    long GrossCents,
    long DiscountCents,
    long NetCents,
    string Formula);
