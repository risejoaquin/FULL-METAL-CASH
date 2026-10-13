namespace SolidPOS.DiscountEngine;

/// <summary>A gross, pre-discount sale line in exact integer cents.</summary>
public sealed record DiscountLine(Guid LineId, long GrossCents);

/// <summary>
/// One centrally resolved or trusted cached discount. "fixed_amount" uses
/// integer cents stored in Amount; "percentage" uses percent in Amount.
/// Scope is "line" or "cart". Rule order never affects the final result.
/// </summary>
public sealed record DiscountRule(
    Guid Id, string Scope, string Kind, decimal Amount, Guid? LineId = null,
    bool RequiresApproval = false, bool CanCombine = true);

/// <summary>
/// A verified supervisor identity supplied by the trusted application layer,
/// not by an untrusted public quote request or sync payload.
/// </summary>
public sealed record DiscountApproval(Guid DiscountId, Guid ApproverUserId);

public sealed record DiscountQuoteRequest(
    Guid TenantId, Guid StoreId, Guid OperatorUserId,
    IReadOnlyList<DiscountLine> Lines,
    IReadOnlyList<DiscountRule> Rules,
    IReadOnlyList<DiscountApproval> VerifiedApprovals);

public sealed record DiscountLineTotal(
    Guid LineId, long GrossCents, long LineDiscountCents,
    long CartDiscountCents, long NetCents);

public sealed record DiscountAllocation(
    Guid DiscountId, Guid LineId, string Scope, long Cents);

public sealed record DiscountQuoteResult(
    long GrossCents, long DiscountCents, long NetCents,
    IReadOnlyList<DiscountLineTotal> Lines,
    IReadOnlyList<DiscountAllocation> Allocations,
    string ComputationVersion = "money-v1-away-from-zero-largest-remainder");

/// <summary>
/// Pure, deterministic financial calculation shared by online and offline POS.
/// No currency floats, database access, auth queries, I/O or business clocks.
/// </summary>
public static class DiscountCalculator
{
    private const long MaximumLineCents = 900_000_000_000_000;

    public static DiscountQuoteResult Quote(DiscountQuoteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TenantId == Guid.Empty || request.StoreId == Guid.Empty ||
            request.OperatorUserId == Guid.Empty)
            throw new InvalidOperationException("Discount calculation requires an authenticated tenant/store/operator.");
        if (request.Lines is null || request.Lines.Count is < 1 or > 1_000 ||
            request.Rules is null || request.Rules.Count > 200 ||
            request.VerifiedApprovals is null)
            throw new InvalidOperationException("Invalid sale-line, discount-rule or approval collection.");
        if (request.Lines.Any(line => line.LineId == Guid.Empty ||
            line.GrossCents < 0 || line.GrossCents > MaximumLineCents) ||
            request.Lines.Select(line => line.LineId).Distinct().Count() != request.Lines.Count)
            throw new InvalidOperationException("Lines must be uniquely identified with nonnegative bounded gross amounts.");

        var lineIds = request.Lines.Select(line => line.LineId).ToHashSet();
        var ruleIds = new HashSet<Guid>();
        foreach (var rule in request.Rules)
        {
            if (rule.Id == Guid.Empty || !ruleIds.Add(rule.Id) ||
                rule.Scope is not ("line" or "cart") ||
                rule.Kind is not ("percentage" or "fixed_amount") ||
                (rule.Kind == "percentage" && rule.Amount is not (> 0m and <= 100m)) ||
                (rule.Kind == "fixed_amount" &&
                    (rule.Amount <= 0m || rule.Amount != decimal.Truncate(rule.Amount) ||
                     rule.Amount > MaximumLineCents)) ||
                (rule.Scope == "line" && (!rule.LineId.HasValue || !lineIds.Contains(rule.LineId.Value))) ||
                (rule.Scope == "cart" && rule.LineId.HasValue))
                throw new InvalidOperationException("Discount has an invalid ID, scope, value or target line.");
        }

        if (request.Rules.Count(rule => rule.Scope == "cart") > 1)
            throw new InvalidOperationException("Only one cart-wide discount is allowed in this contract.");
        if (request.Rules.Count > 1 && request.Rules.Any(rule => !rule.CanCombine))
            throw new InvalidOperationException("Non-combinable discounts cannot be stacked.");

        var approvals = new Dictionary<Guid, Guid>();
        foreach (var approval in request.VerifiedApprovals)
        {
            if (approval.DiscountId == Guid.Empty || approval.ApproverUserId == Guid.Empty ||
                approval.ApproverUserId == request.OperatorUserId ||
                !ruleIds.Contains(approval.DiscountId) ||
                !approvals.TryAdd(approval.DiscountId, approval.ApproverUserId))
                throw new InvalidOperationException("Invalid, foreign, repeated or self-issued discount approval.");
        }
        if (request.Rules.Any(rule => rule.RequiresApproval && !approvals.ContainsKey(rule.Id)))
            throw new UnauthorizedAccessException("Supervisor approval is required before applying this discount.");

        long gross = 0;
        foreach (var line in request.Lines) gross = checked(gross + line.GrossCents);
        var lineDiscounts = request.Lines.ToDictionary(line => line.LineId, _ => 0L);
        var allocations = new List<DiscountAllocation>();

        foreach (var rule in request.Rules.Where(x => x.Scope == "line").OrderBy(x => x.Id))
        {
            var line = request.Lines.Single(item => item.LineId == rule.LineId!.Value);
            long remaining = checked(line.GrossCents - lineDiscounts[line.LineId]);
            long applied = CalculateCents(remaining, rule);
            lineDiscounts[line.LineId] = checked(lineDiscounts[line.LineId] + applied);
            allocations.Add(new DiscountAllocation(rule.Id, line.LineId, "line", applied));
        }

        var cartDiscounts = request.Lines.ToDictionary(line => line.LineId, _ => 0L);
        var cart = request.Rules.SingleOrDefault(x => x.Scope == "cart");
        if (cart is not null)
        {
            long eligible = checked(gross - lineDiscounts.Values.Sum());
            long applied = CalculateCents(eligible, cart);
            if (eligible > 0 && applied > 0)
            {
                // Hamilton/largest-remainder allocation, with stable GUID
                // tie-breaking. The allocations sum EXACTLY to cart savings.
                var parts = request.Lines.Select(line =>
                {
                    long weight = checked(line.GrossCents - lineDiscounts[line.LineId]);
                    decimal share = (decimal)applied * weight / eligible;
                    long baseCents = (long)decimal.Floor(share);
                    return new Remainder(line.LineId, baseCents, share - baseCents);
                }).ToArray();
                long assigned = parts.Sum(x => x.BaseCents);
                long remaining = applied - assigned;
                foreach (var part in parts.OrderByDescending(x => x.Fraction).ThenBy(x => x.LineId).Take(checked((int)remaining)))
                    cartDiscounts[part.LineId] = checked(cartDiscounts[part.LineId] + 1);
                foreach (var part in parts)
                    cartDiscounts[part.LineId] = checked(cartDiscounts[part.LineId] + part.BaseCents);
            }
            foreach (var item in request.Lines.OrderBy(x => x.LineId))
                allocations.Add(new DiscountAllocation(cart.Id, item.LineId, "cart", cartDiscounts[item.LineId]));
        }

        var totals = request.Lines.Select(line => new DiscountLineTotal(
            line.LineId, line.GrossCents, lineDiscounts[line.LineId],
            cartDiscounts[line.LineId],
            checked(line.GrossCents - lineDiscounts[line.LineId] - cartDiscounts[line.LineId]))).ToArray();
        long net = totals.Sum(x => x.NetCents);
        long discount = checked(gross - net);
        if (net < 0 || totals.Any(x => x.NetCents < 0) ||
            discount != allocations.Sum(x => x.Cents))
            throw new InvalidOperationException("Discount monetary conservation invariant failed.");

        return new DiscountQuoteResult(gross, discount, net, totals, allocations);
    }

    private static long CalculateCents(long available, DiscountRule rule)
    {
        if (available <= 0) return 0;
        decimal amount = rule.Kind == "percentage"
            ? Math.Round((decimal)available * rule.Amount / 100m, 0,
                MidpointRounding.AwayFromZero)
            : rule.Amount;
        return Math.Min(available, checked((long)amount));
    }

    private sealed record Remainder(Guid LineId, long BaseCents, decimal Fraction);
}
