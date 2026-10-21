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

    [Theory]
    [InlineData(0, "ரூபாய் பூஜ்ஜியம் மட்டும்")]
    [InlineData(1, "ரூபாய் ஒன்று மட்டும்")]
    [InlineData(15, "ரூபாய் பதினைந்து மட்டும்")]
    [InlineData(21, "ரூபாய் இருபத்தொன்று மட்டும்")]
    [InlineData(43, "ரூபாய் நாற்பத்து மூன்று மட்டும்")]
    [InlineData(99, "ரூபாய் தொண்ணூற்றொன்பது மட்டும்")]
    [InlineData(100, "ரூபாய் நூறு மட்டும்")]
    [InlineData(105, "ரூபாய் நூற்று ஐந்து மட்டும்")]
    [InlineData(950, "ரூபாய் தொள்ளாயிரத்து ஐம்பது மட்டும்")]
    [InlineData(1000, "ரூபாய் ஆயிரம் மட்டும்")]
    [InlineData(1474, "ரூபாய் ஆயிரத்து நானூற்று எழுபத்து நான்கு மட்டும்")]
    [InlineData(2000, "ரூபாய் இரண்டாயிரம் மட்டும்")]
    [InlineData(11000, "ரூபாய் பதினோராயிரம் மட்டும்")]
    [InlineData(12450, "ரூபாய் பன்னிரண்டாயிரத்து நானூற்று ஐம்பது மட்டும்")]
    [InlineData(21500, "ரூபாய் இருபத்தோராயிரத்து ஐந்நூறு மட்டும்")]
    [InlineData(100000, "ரூபாய் ஒரு லட்சம் மட்டும்")]
    [InlineData(123456.50, "ரூபாய் ஒரு லட்சத்து இருபத்து மூன்றாயிரத்து நானூற்று ஐம்பத்தாறு மற்றும் ஐம்பது பைசா மட்டும்")]
    [InlineData(2500000, "ரூபாய் இருபத்தைந்து லட்சம் மட்டும்")]
    [InlineData(32763100, "ரூபாய் மூன்று கோடியே இருபத்தேழு லட்சத்து அறுபத்து மூன்றாயிரத்து நூறு மட்டும்")]
    [InlineData(10000000, "ரூபாய் ஒரு கோடி மட்டும்")]
    [InlineData(0.05, "ரூபாய் பூஜ்ஜியம் மற்றும் ஐந்து பைசா மட்டும்")]
    public void Tamil_amounts_use_the_indian_numbering_system(decimal amount, string expected)
    {
        Assert.Equal(expected, AmountInWords.TamilRupees(amount));
    }
}
