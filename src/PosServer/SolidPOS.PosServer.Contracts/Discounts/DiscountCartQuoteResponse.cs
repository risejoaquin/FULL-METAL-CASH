namespace SolidPOS.PosServer.Contracts.Discounts;

public sealed record DiscountCartQuoteLineResponse(
    Guid LineId, long GrossCents, long LineDiscountCents,
    long CartDiscountCents, long NetCents);

public sealed record DiscountCartQuoteAllocationResponse(
    Guid DiscountId, Guid LineId, string Scope, long Cents);

public sealed record DiscountCartQuoteResponse(
    long GrossCents, long DiscountCents, long NetCents,
    IReadOnlyList<DiscountCartQuoteLineResponse> Lines,
    IReadOnlyList<DiscountCartQuoteAllocationResponse> Allocations,
    string ComputationVersion,
    bool PreviewOnly = true);
