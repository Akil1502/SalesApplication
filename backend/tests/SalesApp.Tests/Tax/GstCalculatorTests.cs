using SalesApp.Core.Tax;

namespace SalesApp.Tests.Tax;

public sealed class GstCalculatorTests
{
    [Fact]
    public void Intra_state_supply_splits_the_rate_into_equal_cgst_and_sgst()
    {
        var result = GstCalculator.CalculateLine(new GstLineInput(100m, 312.50m, 0m, 5m), isInterState: false);

        Assert.Equal(31250.00m, result.TaxableValue);
        Assert.Equal(2.5m, result.CgstRate);
        Assert.Equal(781.25m, result.CgstAmount);
        Assert.Equal(781.25m, result.SgstAmount);
        Assert.Equal(0m, result.IgstAmount);
        Assert.Equal(32812.50m, result.LineTotal);
    }

    [Fact]
    public void Inter_state_supply_charges_igst_only()
    {
        var result = GstCalculator.CalculateLine(new GstLineInput(100m, 312.50m, 0m, 5m), isInterState: true);

        Assert.Equal(0m, result.CgstAmount + result.SgstAmount);
        Assert.Equal(5m, result.IgstRate);
        Assert.Equal(1562.50m, result.IgstAmount);
    }

    [Fact]
    public void Discount_is_taken_before_tax_and_rounded_to_the_paisa()
    {
        // 12.5 kg x 249.99 = 3124.875 -> 3124.88 gross; 7.5% discount = 234.366 -> 234.37.
        var result = GstCalculator.CalculateLine(new GstLineInput(12.5m, 249.99m, 7.5m, 18m), isInterState: false);

        Assert.Equal(3124.88m, result.GrossAmount);
        Assert.Equal(234.37m, result.DiscountAmount);
        Assert.Equal(2890.51m, result.TaxableValue);
        Assert.Equal(260.15m, result.CgstAmount); // 2890.51 x 9% = 260.1459
        Assert.Equal(result.CgstAmount, result.SgstAmount);
    }

    [Fact]
    public void Half_paisa_rounds_away_from_zero()
    {
        // 1 x 0.10 at 5% IGST = exactly 0.005 -> 0.01. (Banker's rounding, .NET's default, would give 0.00.)
        var result = GstCalculator.CalculateLine(new GstLineInput(1m, 0.10m, 0m, 5m), isInterState: true);

        Assert.Equal(0.01m, result.IgstAmount);
    }

    [Fact]
    public void Invoice_total_is_rounded_to_the_rupee_with_the_difference_as_round_off()
    {
        var totals = GstCalculator.Calculate(
        [
            new GstLineInput(10m, 99.95m, 0m, 5m),  // 999.50 + 24.99 + 24.99 = 1049.48
            new GstLineInput(3m, 120.00m, 0m, 18m), // 360.00 + 32.40 + 32.40 = 424.80
        ], isInterState: false);

        Assert.Equal(1359.50m, totals.TaxableValue);
        Assert.Equal(57.39m, totals.CgstAmount);
        Assert.Equal(57.39m, totals.SgstAmount);
        Assert.Equal(1474.28m, totals.TaxableValue + totals.TaxAmount);
        Assert.Equal(-0.28m, totals.RoundOff);
        Assert.Equal(1474m, totals.GrandTotal);
    }

    [Fact]
    public void Round_off_can_go_up()
    {
        var totals = GstCalculator.Calculate([new GstLineInput(1m, 99.50m, 0m, 0m)], isInterState: true);

        Assert.Equal(0.50m, totals.RoundOff);
        Assert.Equal(100m, totals.GrandTotal);
    }

    [Fact]
    public void Nil_rated_goods_carry_no_tax()
    {
        var totals = GstCalculator.Calculate([new GstLineInput(2m, 500m, 0m, 0m)], isInterState: false);

        Assert.Equal(0m, totals.TaxAmount);
        Assert.Equal(1000m, totals.GrandTotal);
    }

    [Theory]
    [InlineData(0, 10, 0, 5)]
    [InlineData(1, -1, 0, 5)]
    [InlineData(1, 10, 101, 5)]
    public void Invalid_inputs_are_rejected(decimal quantity, decimal price, decimal discount, decimal rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GstCalculator.CalculateLine(new GstLineInput(quantity, price, discount, rate), isInterState: false));
    }
}

public sealed class AmountInWordsTests
{
    [Theory]
    [InlineData(0, "Rupees Zero Only")]
    [InlineData(1, "Rupees One Only")]
    [InlineData(15, "Rupees Fifteen Only")]
    [InlineData(105, "Rupees One Hundred Five Only")]
    [InlineData(1474, "Rupees One Thousand Four Hundred Seventy Four Only")]
    [InlineData(100000, "Rupees One Lakh Only")]
    [InlineData(123456.50, "Rupees One Lakh Twenty Three Thousand Four Hundred Fifty Six and Fifty Paise Only")]
    [InlineData(32763100, "Rupees Three Crore Twenty Seven Lakh Sixty Three Thousand One Hundred Only")]
    [InlineData(1000000000, "Rupees One Hundred Crore Only")]
    [InlineData(0.05, "Rupees Zero and Five Paise Only")]
    public void Amounts_use_the_indian_numbering_system(decimal amount, string expected)
    {
        Assert.Equal(expected, AmountInWords.Rupees(amount));
    }
}
