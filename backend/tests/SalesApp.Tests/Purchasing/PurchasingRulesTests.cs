using SalesApp.Core.Purchasing;

namespace SalesApp.Tests.Purchasing;

public sealed class PurchaseCostingTests
{
    [Fact]
    public void A_receipt_blends_its_price_into_the_cost_of_what_is_on_the_shelf()
    {
        // 100 kg at ₹200 plus 50 kg at ₹230 is 150 kg at ₹210.
        Assert.Equal(210m, PurchaseCosting.AfterReceipt(200m, 100m, 50m, 230m));
    }

    [Theory]
    [InlineData(null, 100.0)]
    [InlineData(200.0, 0.0)]
    [InlineData(200.0, -5.0)]
    public void With_nothing_on_the_shelf_or_no_cost_yet_the_new_goods_set_the_cost(double? current, double onHand)
    {
        Assert.Equal(245.5m, PurchaseCosting.AfterReceipt((decimal?)current, (decimal)onHand, 10m, 245.5m));
    }

    [Fact]
    public void Costs_are_rounded_to_the_paisa()
    {
        // (3 × 100 + 1 × 101) / 4 = 100.25; (3 × 100 + 2 × 101) / 5 = 100.4; (7 × 10 + 2 × 11) / 9 = 10.2222…
        Assert.Equal(100.25m, PurchaseCosting.AfterReceipt(100m, 3m, 1m, 101m));
        Assert.Equal(10.22m, PurchaseCosting.AfterReceipt(10m, 7m, 2m, 11m));
    }

    [Fact]
    public void A_bill_adds_its_extra_cost_to_the_stock_still_on_hand()
    {
        // 100 kg on hand at ₹200, all of it from this delivery: ₹1,000 freight adds ₹10 a kg.
        Assert.Equal(210m, PurchaseCosting.AfterBillAdjustment(200m, 100m, 100m, 1000m));
    }

    [Fact]
    public void Only_the_part_of_the_delivery_still_in_stock_takes_the_extra_cost()
    {
        // 100 kg billed, 40 kg left (60 kg already sold at the old cost): 40% of ₹1,000 = ₹400 over 40 kg = ₹10 a kg.
        Assert.Equal(210m, PurchaseCosting.AfterBillAdjustment(200m, 40m, 100m, 1000m));
        // 100 kg billed but 400 kg on hand (older stock too): the whole ₹1,000 is spread over 400 kg.
        Assert.Equal(202.5m, PurchaseCosting.AfterBillAdjustment(200m, 400m, 100m, 1000m));
    }

    [Fact]
    public void A_cheaper_bill_lowers_the_cost_but_never_below_zero()
    {
        Assert.Equal(195m, PurchaseCosting.AfterBillAdjustment(200m, 100m, 100m, -500m));
        Assert.Equal(0m, PurchaseCosting.AfterBillAdjustment(2m, 10m, 10m, -500m));
    }

    [Theory]
    [InlineData(null, 100.0, 100.0, 1000.0)]
    [InlineData(200.0, 0.0, 100.0, 1000.0)]
    [InlineData(200.0, 100.0, 0.0, 1000.0)]
    [InlineData(200.0, 100.0, 100.0, 0.0)]
    public void Nothing_changes_without_a_cost_stock_a_quantity_or_an_amount(double? current, double onHand, double billed, double extra)
    {
        Assert.Equal((decimal?)current, PurchaseCosting.AfterBillAdjustment((decimal?)current, (decimal)onHand, (decimal)billed, (decimal)extra));
    }

    [Fact]
    public void Charges_are_split_by_line_value_and_always_add_up()
    {
        var parts = PurchaseCosting.Allocate(1000m, [100m, 200m, 700m]);
        Assert.Equal([100m, 200m, 700m], parts);

        // ₹100 over three equal lines: the paisa left over goes to one of them.
        var thirds = PurchaseCosting.Allocate(100m, [1m, 1m, 1m]);
        Assert.Equal(100m, thirds.Sum());
        Assert.All(thirds, p => Assert.InRange(p, 33.33m, 33.34m));

        Assert.Equal([50m, 50m], PurchaseCosting.Allocate(100m, [0m, 0m]));
        Assert.Empty(PurchaseCosting.Allocate(100m, []));
    }
}

public sealed class BillMatchingTests
{
    private static BillLineCheck Line(decimal quantity = 100m, decimal price = 250m, decimal gstRate = 5m, decimal received = 100m, decimal billedBefore = 0m) =>
        new(quantity, price, gstRate, OrderedPrice: 250m, ProductGstRate: 5m, received, billedBefore, "Kg");

    [Fact]
    public void A_bill_that_matches_its_order_and_receipt_has_no_issue()
    {
        Assert.Null(BillMatching.LineIssue(Line(), 2m));
        // Within the 2% tolerance either way.
        Assert.Null(BillMatching.LineIssue(Line(price: 255m), 2m));
        Assert.Null(BillMatching.LineIssue(Line(price: 245m), 2m));
    }

    [Fact]
    public void A_price_outside_the_tolerance_is_flagged_with_how_far_out_it_is()
    {
        var issue = BillMatching.LineIssue(Line(price: 262.5m), 2m);
        Assert.Equal("Price ₹262.50 is 5% above the order's ₹250.00.", issue);
        Assert.Contains("below", BillMatching.LineIssue(Line(price: 240m), 2m), StringComparison.Ordinal);
    }

    [Fact]
    public void Charging_for_more_than_has_arrived_is_flagged()
    {
        Assert.Equal("Charges for 120 Kg, but only 100 has been received.", BillMatching.LineIssue(Line(quantity: 120m), 2m));
        Assert.Equal("Bills charge for 1,10,000 Kg in all, but only 1,00,000 has been received.",
            BillMatching.LineIssue(Line(quantity: 60_000m, received: 100_000m, billedBefore: 50_000m), 2m));
        // Within what has arrived across several bills: fine.
        Assert.Null(BillMatching.LineIssue(Line(quantity: 40m, billedBefore: 60m), 2m));
    }

    [Fact]
    public void A_GST_rate_other_than_the_products_is_flagged()
    {
        Assert.Equal("GST at 12%, but the product's HSN rate is 5%.", BillMatching.LineIssue(Line(gstRate: 12m), 2m));
    }

    [Fact]
    public void Several_problems_on_one_line_are_all_listed()
    {
        var issue = BillMatching.LineIssue(Line(quantity: 120m, price: 300m, gstRate: 12m), 2m)!;
        Assert.Contains("received", issue, StringComparison.Ordinal);
        Assert.Contains("above", issue, StringComparison.Ordinal);
        Assert.Contains("GST", issue, StringComparison.Ordinal);
    }

    [Fact]
    public void A_total_off_by_more_than_a_rupee_is_flagged()
    {
        Assert.Null(BillMatching.TotalIssue(10_500m, null));
        Assert.Null(BillMatching.TotalIssue(10_500m, 10_501m));
        Assert.Equal("The bill works out to ₹10,500.00, but the supplier's invoice says ₹10,550.00.", BillMatching.TotalIssue(10_500m, 10_550m));
    }
}
