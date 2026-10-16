using System.Globalization;
using SalesApp.Core.Purchasing;
using SalesApp.Core.Returns;

namespace SalesApp.Tests.Returns;

public sealed class ReturnRulesTests
{
    // 100 kg at ₹250 less 10% = ₹22,500 taxable; 5% GST within the state is ₹562.50 + ₹562.50.
    private static readonly CreditableLine Yarn = new(100m, 22_500m, 562.50m, 562.50m, 0m, 5m);

    [Theory]
    [InlineData("2026-04-01", "2027-11-30")] // first day of FY 2026-27
    [InlineData("2027-03-31", "2027-11-30")] // last day of FY 2026-27
    [InlineData("2027-04-01", "2028-11-30")]
    public void The_GST_deadline_is_30_November_after_the_financial_year(string supplied, string deadline)
    {
        Assert.Equal(DateOnly.Parse(deadline, CultureInfo.InvariantCulture), ReturnRules.GstDeadline(DateOnly.Parse(supplied, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void A_credit_after_the_deadline_carries_no_GST()
    {
        var invoiced = new DateOnly(2026, 6, 10);
        Assert.True(ReturnRules.CarriesGst(invoiced, new DateOnly(2027, 11, 30)));
        Assert.False(ReturnRules.CarriesGst(invoiced, new DateOnly(2027, 12, 1)));
    }

    [Fact]
    public void An_invoice_can_only_be_cancelled_in_its_own_month()
    {
        Assert.True(ReturnRules.InSameMonth(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));
        Assert.False(ReturnRules.InSameMonth(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 1)));
        Assert.False(ReturnRules.InSameMonth(new DateOnly(2025, 10, 16), new DateOnly(2026, 10, 16)));
    }

    [Fact]
    public void Returned_goods_are_credited_at_the_lines_value_per_unit()
    {
        var credit = ReturnRules.ForGoods(Yarn, CreditedSoFar.None, 10m, isInterState: false, withGst: true);

        Assert.Equal(new CreditAmounts(2_250m, 56.25m, 56.25m, 0m), credit);
        Assert.Equal(2_362.50m, credit.LineTotal);
    }

    [Fact]
    public void Returning_a_line_in_pieces_never_gains_or_loses_a_paisa()
    {
        // 3 kg of 7 at ₹10.01 a kg: each piece rounds, the last one takes exactly what's left.
        var line = new CreditableLine(7m, 70.07m, 1.75m, 1.75m, 0m, 5m);
        var first = ReturnRules.ForGoods(line, CreditedSoFar.None, 3m, false, true);
        var soFar = new CreditedSoFar(3m, first.TaxableValue, first.CgstAmount, first.SgstAmount, first.IgstAmount);
        var second = ReturnRules.ForGoods(line, soFar, 4m, false, true);

        Assert.Equal(line.TaxableValue, first.TaxableValue + second.TaxableValue);
        Assert.Equal(line.CgstAmount, first.CgstAmount + second.CgstAmount);
        Assert.Equal(line.SgstAmount, first.SgstAmount + second.SgstAmount);
    }

    [Fact]
    public void After_a_price_correction_each_returned_unit_is_worth_less()
    {
        // ₹2,500 taken off for a rate difference leaves ₹20,000 over 100 kg: ₹200 a kg.
        var correction = ReturnRules.ForValue(Yarn, CreditedSoFar.None, 2_500m, false, true);
        var soFar = new CreditedSoFar(0m, correction.TaxableValue, correction.CgstAmount, correction.SgstAmount, correction.IgstAmount);

        var returned = ReturnRules.ForGoods(Yarn, soFar, 10m, false, true);

        Assert.Equal(2_000m, returned.TaxableValue);
    }

    [Fact]
    public void Inter_state_credit_is_all_IGST()
    {
        var line = new CreditableLine(50m, 10_000m, 0m, 0m, 500m, 5m);
        Assert.Equal(new CreditAmounts(2_000m, 0m, 0m, 100m), ReturnRules.ForGoods(line, CreditedSoFar.None, 10m, isInterState: true, withGst: true));
    }

    [Fact]
    public void A_financial_credit_note_credits_the_value_only()
    {
        var credit = ReturnRules.ForGoods(Yarn, CreditedSoFar.None, 10m, false, withGst: false);
        Assert.Equal(new CreditAmounts(2_250m, 0m, 0m, 0m), credit);
    }

    [Fact]
    public void Nothing_beyond_what_is_left_on_the_line_can_be_credited()
    {
        var soFar = new CreditedSoFar(95m, 21_375m, 534.38m, 534.38m, 0m);
        Assert.Throws<ArgumentOutOfRangeException>(() => ReturnRules.ForGoods(Yarn, soFar, 6m, false, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReturnRules.ForValue(Yarn, soFar, 1_125.01m, false, true));
        Assert.Equal(1_125m, ReturnRules.ForValue(Yarn, soFar, 1_125m, false, true).TaxableValue);
    }

    [Fact]
    public void The_total_is_rounded_to_the_rupee_like_an_invoice()
    {
        var (roundOff, total) = ReturnRules.Total([new CreditAmounts(2_250.40m, 56.26m, 56.26m, 0m)]);
        Assert.Equal(2_363m, total);
        Assert.Equal(0.08m, roundOff);
    }
}

public sealed class PurchaseReturnCostingTests
{
    [Fact]
    public void Goods_sent_back_leave_at_their_price_and_the_rest_keeps_its_true_cost()
    {
        // 150 kg at ₹210 less 50 kg returned at ₹230 leaves 100 kg at ₹200.
        Assert.Equal(200m, PurchaseCosting.AfterReturn(210m, 150m, 50m, 230m));
    }

    [Theory]
    [InlineData(50.0)] // nothing would be left
    [InlineData(40.0)] // more than on hand
    public void Nothing_changes_when_no_stock_would_be_left(double onHand)
    {
        Assert.Equal(210m, PurchaseCosting.AfterReturn(210m, (decimal)onHand, 50m, 230m));
    }

    [Fact]
    public void A_cost_that_would_go_negative_is_left_alone()
    {
        // 60 kg at ₹10 less 50 kg returned at ₹100 would leave the rest worth less than nothing.
        Assert.Equal(10m, PurchaseCosting.AfterReturn(10m, 60m, 50m, 100m));
        Assert.Null(PurchaseCosting.AfterReturn(null, 60m, 10m, 100m));
    }
}
