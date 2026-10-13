using SolidPOS.DiscountEngine;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class DiscountEngineFinancialTests
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Store = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Cashier = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Supervisor = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid LineA = Guid.Parse("00000000-0000-0000-0000-000000000011");
    private static readonly Guid LineB = Guid.Parse("00000000-0000-0000-0000-000000000012");
    private static readonly Guid LineC = Guid.Parse("00000000-0000-0000-0000-000000000013");

    private static DiscountQuoteRequest Request(
        IReadOnlyList<DiscountLine> lines, IReadOnlyList<DiscountRule> rules,
        IReadOnlyList<DiscountApproval>? approvals = null) =>
        new(Tenant, Store, Cashier, lines, rules, approvals ?? []);

    [Fact]
    public void Percentage_rounds_half_away_from_zero_and_never_exceeds_gross()
    {
        var id = Guid.NewGuid();
        var quote = DiscountCalculator.Quote(Request(
            [new(LineA, 5)],
            [new(id, "line", "percentage", 50, LineA)]));
        Assert.Equal(5, quote.GrossCents);
        Assert.Equal(3, quote.DiscountCents);
        Assert.Equal(2, quote.NetCents);
        Assert.Equal(3, quote.Allocations.Single().Cents);
    }

    [Fact]
    public void Fixed_amount_is_integer_cents_and_capped_at_remaining_line()
    {
        var q = DiscountCalculator.Quote(Request([new(LineA, 98)],
            [new(Guid.NewGuid(), "line", "fixed_amount", 500, LineA)]));
        Assert.Equal(98, q.DiscountCents);
        Assert.Equal(0, q.NetCents);
    }

    [Fact]
    public void Multiple_line_rules_are_order_independent_and_conserve_cents()
    {
        var first = Guid.Parse("00000000-0000-0000-0000-000000000101");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000102");
        var rules = new[]
        {
            new DiscountRule(first, "line", "fixed_amount", 11, LineA),
            new DiscountRule(second, "line", "percentage", 50, LineA)
        };
        var q = DiscountCalculator.Quote(Request([new(LineA, 101)], rules));
        var reversed = DiscountCalculator.Quote(Request([new(LineA, 101)], rules.Reverse().ToArray()));
        Assert.Equal(101, q.GrossCents);
        Assert.Equal(56, q.DiscountCents);
        Assert.Equal(45, q.NetCents);
        Assert.Equal(q.GrossCents, reversed.GrossCents);
        Assert.Equal(q.DiscountCents, reversed.DiscountCents);
        Assert.Equal(q.NetCents, reversed.NetCents);
        Assert.Equal(q.Allocations.ToArray(), reversed.Allocations.ToArray());
    }

    [Fact]
    public void Cart_percentage_is_apportioned_over_post_line_discount_using_largest_remainder()
    {
        var lineRule = Guid.NewGuid();
        var cartRule = Guid.NewGuid();
        var q = DiscountCalculator.Quote(Request(
            [new(LineA, 100), new(LineB, 100), new(LineC, 100)],
            [new(lineRule, "line", "fixed_amount", 50, LineA),
             new(cartRule, "cart", "percentage", 50)]));
        Assert.Equal(300, q.GrossCents);
        Assert.Equal(50, q.Lines.Single(x => x.LineId == LineA).LineDiscountCents);
        Assert.Equal(125, q.Lines.Sum(x => x.CartDiscountCents));
        Assert.Equal(175, q.DiscountCents);
        Assert.Equal(125, q.NetCents);
        Assert.Equal(q.DiscountCents, q.Allocations.Sum(x => x.Cents));
    }

    [Fact]
    public void Remainder_tie_break_is_deterministic_even_when_line_order_changes()
    {
        Guid id = Guid.NewGuid();
        var rules = new[] { new DiscountRule(id, "cart", "fixed_amount", 1) };
        var forward = DiscountCalculator.Quote(Request(
            [new(LineC, 1), new(LineA, 1), new(LineB, 1)], rules));
        var reverse = DiscountCalculator.Quote(Request(
            [new(LineB, 1), new(LineA, 1), new(LineC, 1)], rules));
        Assert.Equal(1, forward.DiscountCents);
        Assert.Equal(1, forward.Lines.Single(x => x.LineId == LineA).CartDiscountCents);
        Assert.Equal(1, reverse.Lines.Single(x => x.LineId == LineA).CartDiscountCents);
        Assert.Equal(forward.Allocations.Sum(x => x.Cents), reverse.Allocations.Sum(x => x.Cents));
    }

    [Fact]
    public void Approved_high_risk_rule_needs_independent_supervisor()
    {
        var id = Guid.NewGuid();
        var discount = new DiscountRule(id, "cart", "percentage", 90, RequiresApproval: true);
        var lines = new[] { new DiscountLine(LineA, 500) };
        Assert.Throws<UnauthorizedAccessException>(() => DiscountCalculator.Quote(Request(lines, [discount])));
        Assert.Throws<InvalidOperationException>(() => DiscountCalculator.Quote(
            Request(lines, [discount], [new(id, Cashier)])));
        var approved = DiscountCalculator.Quote(Request(
            lines, [discount], [new(id, Supervisor)]));
        Assert.Equal(450, approved.DiscountCents);
    }

    [Fact]
    public void Non_combinable_rules_cannot_be_stacked()
    {
        Assert.Throws<InvalidOperationException>(() => DiscountCalculator.Quote(Request(
            [new(LineA, 1000)], [
                new(Guid.NewGuid(), "line", "fixed_amount", 10, LineA, CanCombine: false),
                new(Guid.NewGuid(), "cart", "percentage", 5)
            ])));
    }

    [Theory]
    [InlineData("-1", "fixed_amount")]
    [InlineData("1.1", "fixed_amount")]
    [InlineData("101", "percentage")]
    [InlineData("0", "percentage")]
    public void Invalid_financial_values_are_rejected(string amount, string kind)
    {
        Assert.Throws<InvalidOperationException>(() => DiscountCalculator.Quote(Request(
            [new(LineA, 100)], [new(Guid.NewGuid(), "line", kind, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), LineA)])));
    }

    [Fact]
    public void Empty_and_duplicate_line_ids_or_rules_are_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => DiscountCalculator.Quote(Request(
            [new(LineA, 10), new(LineA, 10)], [])));
        var discountId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => DiscountCalculator.Quote(Request(
            [new(LineA, 10)], [
                new(discountId, "cart", "percentage", 10),
                new(discountId, "line", "percentage", 10, LineA)
            ])));
    }

    [Fact]
    public void Zero_gross_never_generates_negative_net_or_phantom_savings()
    {
        var q = DiscountCalculator.Quote(Request(
            [new(LineA, 0), new(LineB, 0)],
            [new(Guid.NewGuid(), "cart", "fixed_amount", 300)]));
        Assert.Equal(0, q.GrossCents);
        Assert.Equal(0, q.DiscountCents);
        Assert.Equal(0, q.NetCents);
    }

    [Fact]
    public void Capped_cart_discount_must_keep_all_financial_invariants()
    {
        var q = DiscountCalculator.Quote(Request(
            [new(LineA, 1), new(LineB, 2), new(LineC, 3)],
            [new(Guid.NewGuid(), "cart", "fixed_amount", 1000)]));
        Assert.Equal(6, q.DiscountCents);
        Assert.Equal(0, q.NetCents);
        Assert.Equal(6, q.Lines.Sum(x => x.CartDiscountCents));
        Assert.Equal(0, q.Lines.Sum(x => x.NetCents));
    }

    [Fact]
    public void Refund_allocation_basis_can_be_reconstructed_without_a_cent_discrepancy()
    {
        var q = DiscountCalculator.Quote(Request(
            [new(LineA, 500), new(LineB, 300), new(LineC, 200)],
            [new(Guid.NewGuid(), "cart", "percentage", 15)]));
        Assert.Equal(1000, q.GrossCents);
        Assert.Equal(150, q.DiscountCents);
        Assert.Equal(850, q.NetCents);
        Assert.Equal(q.NetCents, q.Lines.Sum(x => x.NetCents));
        Assert.Equal(q.DiscountCents, q.Lines.Sum(x => x.LineDiscountCents + x.CartDiscountCents));
    }
}
