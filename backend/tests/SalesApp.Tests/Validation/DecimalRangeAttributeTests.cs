using System.ComponentModel.DataAnnotations;
using System.Reflection;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Validation;

namespace SalesApp.Tests.Validation;

public sealed class DecimalRangeAttributeTests
{
    private sealed record Line
    {
        [DecimalRange(0.001, 99999999, ErrorMessage = "Quantity must be more than zero.")]
        public decimal Quantity { get; init; }

        [DecimalRange(-99999999, 99999999)]
        public decimal? Adjustment { get; init; }
    }

    private static List<string?> Errors(Line line)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(line, new ValidationContext(line), results, validateAllProperties: true);
        return [.. results.Select(r => r.ErrorMessage)];
    }

    [Fact]
    public void Limits_are_compared_as_decimals_and_include_both_ends()
    {
        var attribute = new DecimalRangeAttribute(0.001, 99999999);

        Assert.True(attribute.IsValid(0.001m));
        Assert.True(attribute.IsValid(99999999m));
        Assert.False(attribute.IsValid(0.0009m));
        Assert.False(attribute.IsValid(99999999.001m));
        Assert.True(attribute.IsValid(null));
        Assert.True(new DecimalRangeAttribute(-99999999, 99999999).IsValid(-99999999m));
    }

    [Fact]
    public void Messages_are_the_custom_one_or_the_standard_range_message()
    {
        Assert.Empty(Errors(new Line { Quantity = 5m, Adjustment = -3m }));
        Assert.Equal(["Quantity must be more than zero."], Errors(new Line { Quantity = 0m }));
        Assert.Equal(["The field Adjustment must be between -99999999 and 99999999."], Errors(new Line { Quantity = 1m, Adjustment = 100000000m }));
    }

    [Fact]
    public void One_attribute_can_be_checked_from_many_threads_at_once()
    {
        // Each round takes a fresh attribute and has several threads use it for the first time together: the moment
        // the old Range(typeof(decimal), ...) form could throw InvalidCastException. The race is rare (about once in
        // a few hundred such rounds on a developer machine), so this runs many rounds.
        const int rounds = 100, threadsPerRound = 8;
        var failures = new List<string>();
        for (var round = 0; round < rounds; round++)
        {
            var attribute = new DecimalRangeAttribute(0.01, 999999999);
            using var start = new Barrier(threadsPerRound);
            var threads = Enumerable.Range(0, threadsPerRound).Select(i => new Thread(() =>
            {
                try
                {
                    start.SignalAndWait();
                    var result = attribute.GetValidationResult(i % 2 == 0 ? 10m : 0m, new ValidationContext(new object()) { MemberName = "Amount" });
                    var expected = i % 2 == 0 ? null : "The field Amount must be between 0.01 and 999999999.";
                    if (result?.ErrorMessage != expected)
                    {
                        lock (failures) failures.Add($"round {round}: {result?.ErrorMessage ?? "valid"}");
                    }
                }
                catch (Exception exception)
                {
                    lock (failures) failures.Add($"round {round}: {exception.GetType().Name}");
                }
            })).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void No_request_uses_the_thread_unsafe_decimal_range()
    {
        var unsafeRanges =
            from type in typeof(OrderLineInput).Assembly.GetTypes()
            from property in type.GetProperties()
            from attribute in property.GetCustomAttributes<RangeAttribute>()
            where attribute.GetType() == typeof(RangeAttribute) && attribute.OperandType == typeof(decimal)
            select $"{type.Name}.{property.Name}";

        Assert.Empty(unsafeRanges);
    }
}
