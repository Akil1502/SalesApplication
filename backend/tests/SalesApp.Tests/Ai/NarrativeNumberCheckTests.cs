using SalesApp.AI.Insights;

namespace SalesApp.Tests.Ai;

public sealed class NarrativeNumberCheckTests
{
    private const string Metrics = """
        {"sales":{"financialYearToDate":187176025,"samePeriodLastYear":158420000,"growthPercent":18.2,"monthGrowthPercent":-4.6,"invoicesThisYear":412},
         "receivables":{"overdue":2360000,"overdueInvoices":37},
         "forecast":{"next":[{"forecast":21450000,"low":19100000,"high":23800000}],"backtests":[{"meanAbsolutePercentError":6.3}]},
         "anomalies":[{"amount":845000,"typical":136000,"ratio":6.2,"days":142}]}
        """;

    private static readonly List<decimal> Known = NarrativeNumberCheck.NumbersIn(Metrics);

    [Theory]
    [InlineData("Sales this year are ₹18,71,76,025, up 18.2% on last year.")]
    [InlineData("Sales crossed ₹18.72 crore this year, about 18% ahead.")]
    [InlineData("Sales crossed ₹18.7 crore.")]
    [InlineData("Overdue invoices total ₹23.6 lakh across 37 invoices.")]
    [InlineData("Overdue invoices total Rs. 23,60,000.")]
    [InlineData("October is forecast at ₹2.15 crore (₹1.91–2.38 crore), with a backtest error of 6.3%.")]
    [InlineData("INV/26-27/00042 on 17 May 2026 was 6.2 times the usual invoice: ₹8,45,000 against ₹1,36,000.")]
    [InlineData("A big order, about 6 times the usual.")]
    [InlineData("Last month's sales fell 4.6% from the same month last year.")]
    [InlineData("Murugan Garments hasn't bought for 142 days.")]
    [InlineData("Over the last 6 months, CY-60C and GT-TSP stood out; customer C00025 too. FY 2026-27 and Oct 2026.")]
    [InlineData("Reorder within 9 days, and check the top 5 customers.")]
    public void Figures_from_the_metrics_pass_however_they_are_written(string text) =>
        Assert.Empty(NarrativeNumberCheck.Unverified(text, Known));

    [Theory]
    [InlineData("இந்த ஆண்டு விற்பனை ₹18.72 கோடியைத் தாண்டியது, கடந்த ஆண்டைவிட 18.2% அதிகம்.")]
    [InlineData("தவணை கடந்த விலைப்பட்டியல்கள் மொத்தம் ₹23.6 லட்சம், 37 விலைப்பட்டியல்கள்.")]
    [InlineData("அக்டோபர் முன்கணிப்பு ₹2.15 கோடி (₹1.91–2.38 கோடி).")]
    [InlineData("INV/26-27/00042, 17 மே 2026 அன்று, வழக்கத்தைவிட 6.2 மடங்கு: ₹8,45,000.")]
    [InlineData("கடந்த 6 மாதங்களில் CY-60C தனித்து நின்றது; அக்டோபர் 2026.")]
    [InlineData("Murugan Garments 142 நாட்களாக வாங்கவில்லை.")]
    public void Tamil_figures_from_the_metrics_pass(string text) =>
        Assert.Empty(NarrativeNumberCheck.Unverified(text, Known));

    [Theory]
    [InlineData("விற்பனை ₹19.5 கோடியைத் தாண்டியது.", "₹19.5 கோடியைத்")]
    [InlineData("தவணை கடந்தவை ₹31 லட்சம்.", "₹31 லட்சம்.")]
    public void Tamil_figures_not_in_the_metrics_are_caught(string text, string expected) =>
        Assert.Contains(expected, NarrativeNumberCheck.Unverified(text, Known));

    [Theory]
    [InlineData("Sales are up 22% on last year.", "22%")]
    [InlineData("Sales crossed ₹19.5 crore.", "₹19.5 crore")]
    [InlineData("Overdue invoices total ₹31 lakh.", "₹31 lakh")]
    [InlineData("We expect ₹2.5–2.9 crore in October.", "₹2.5")]
    [InlineData("That order was 9 times the usual size.", "9")]
    [InlineData("There are 52 overdue invoices.", "52")]
    [InlineData("The invoice was for ₹8,50,000.", "₹8,50,000")]
    public void Figures_not_in_the_metrics_are_caught(string text, string expected) =>
        Assert.Contains(expected, NarrativeNumberCheck.Unverified(text, Known));

    [Fact]
    public void States_finds_a_figure_written_exactly_or_rounded()
    {
        Assert.True(NarrativeNumberCheck.States("November was best, at ₹4.54 crore.", 45410249.79m));
        Assert.True(NarrativeNumberCheck.States("November was best, at ₹4,54,10,250.", 45410249.79m));
        Assert.False(NarrativeNumberCheck.States("November was best, at ₹3.9 crore.", 45410249.79m));
        Assert.False(NarrativeNumberCheck.States("November 2025 was the best month.", 2025m));
    }

    [Fact]
    public void Collects_every_number_in_the_metrics()
    {
        Assert.Contains(187176025m, Known);
        Assert.Contains(-4.6m, Known);
        Assert.Contains(142m, Known);
        Assert.Equal(15, Known.Count);
    }
}
