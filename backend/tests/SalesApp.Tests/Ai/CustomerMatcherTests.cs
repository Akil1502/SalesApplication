using SalesApp.AI.OrderEntry;
using SalesApp.Core.Contracts.Ai;

namespace SalesApp.Tests.Ai;

public sealed class CustomerMatcherTests
{
    private static readonly CustomerRecord[] Customers =
    [
        new(1, "C00001", "Murugan Garments Pvt Ltd", null, "Tiruppur", "+91 98400 12345", "33ABCDE1234F1Z7"),
        new(2, "C00002", "Murugan Textiles", null, "Erode", "0422-2345678", null),
        new(3, "C00003", "Sri Lakshmi Weaving Mills", "Lakshmi Mills", "Salem", "9876543210", "29AAGCB7383J1Z4"),
        new(4, "C00004", "Kaveri Distributors LLP", null, "Coimbatore", null, null),
    ];

    private static CustomerCluesDto Clues(string? name = null, string? gstin = null, string? phone = null, string? city = null) =>
        new(name, gstin, phone, city, null);

    [Fact]
    public void Gstin_wins_over_a_misleading_name()
    {
        var result = CustomerMatcher.Match(Customers, Clues(name: "Kaveri Distributors", gstin: "29aagcb7383j1z4"), null);

        Assert.Equal(3, result.CustomerId);
        Assert.Equal(CustomerMatchSource.Gstin, result.MatchedBy);
    }

    [Fact]
    public void An_invalid_gstin_is_ignored_and_matching_falls_back_to_the_phone()
    {
        var result = CustomerMatcher.Match(Customers, Clues(gstin: "33ABCDE1234F1Z8", phone: "98400-12345"), null);

        Assert.Equal(1, result.CustomerId);
        Assert.Equal(CustomerMatchSource.Phone, result.MatchedBy);
    }

    [Theory]
    [InlineData("Murugan Garments")]
    [InlineData("M/s. MURUGAN GARMENTS PRIVATE LIMITED")]
    [InlineData("Lakshmi Mills")]
    public void A_clear_name_match_is_accepted(string name)
    {
        var result = CustomerMatcher.Match(Customers, Clues(name: name), null);

        Assert.NotNull(result.CustomerId);
        Assert.Equal(CustomerMatchSource.Name, result.MatchedBy);
    }

    [Fact]
    public void An_ambiguous_name_offers_candidates_instead_of_guessing()
    {
        var result = CustomerMatcher.Match(Customers, Clues(name: "Murugan"), null);

        Assert.Null(result.CustomerId);
        Assert.Equal([1, 2], result.Candidates.Select(c => c.Id).Order());
    }

    [Fact]
    public void The_chosen_customer_stands_but_a_gstin_belonging_to_someone_else_is_flagged()
    {
        var result = CustomerMatcher.Match(Customers, Clues(name: "Murugan Garments", gstin: "33ABCDE1234F1Z7"), selectedCustomerId: 4);

        Assert.Equal(4, result.CustomerId);
        Assert.Equal(CustomerMatchSource.Selected, result.MatchedBy);
        Assert.Equal(1, result.Suggested?.Id);
        Assert.Equal("The order quotes GSTIN 33ABCDE1234F1Z7, which belongs to Murugan Garments Pvt Ltd, not Kaveri Distributors LLP.", result.Warning);
    }

    [Fact]
    public void No_warning_when_the_order_agrees_with_the_chosen_customer()
    {
        var result = CustomerMatcher.Match(Customers, Clues(name: "Murugan Garments", phone: "98400 12345", city: "Tiruppur"), selectedCustomerId: 1);

        Assert.Equal(1, result.CustomerId);
        Assert.Null(result.Warning);
        Assert.Null(result.Suggested);
    }

    [Fact]
    public void An_order_naming_another_customer_is_flagged()
    {
        var result = CustomerMatcher.Match(Customers, Clues(name: "Kaveri Distributors", city: "Coimbatore"), selectedCustomerId: 3);

        Assert.Equal(3, result.CustomerId);
        Assert.Equal(4, result.Suggested?.Id);
        Assert.Equal("The order looks like it's from Kaveri Distributors LLP, Coimbatore, not Sri Lakshmi Weaving Mills.", result.Warning);
    }

    [Fact]
    public void An_unknown_gstin_that_differs_from_the_chosen_customers_is_flagged()
    {
        var result = CustomerMatcher.Match(Customers, Clues(gstin: "27AAPFU0939F1ZV"), selectedCustomerId: 3);

        Assert.Null(result.Suggested);
        Assert.Equal("The order quotes GSTIN 27AAPFU0939F1ZV, but Sri Lakshmi Weaving Mills's GSTIN is 29AAGCB7383J1Z4.", result.Warning);
    }

    [Fact]
    public void A_vague_name_isnt_enough_to_contradict_the_user()
    {
        var result = CustomerMatcher.Match(Customers, Clues(name: "Murugan"), selectedCustomerId: 4);

        Assert.Null(result.Warning);
    }

    [Fact]
    public void No_clues_means_no_match()
    {
        var result = CustomerMatcher.Match(Customers, Clues(), null);

        Assert.Null(result.CustomerId);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void A_phone_shared_by_two_customers_isnt_used()
    {
        CustomerRecord[] customers = [Customers[0], Customers[1] with { Phone = "9840012345" }];

        var result = CustomerMatcher.Match(customers, Clues(phone: "9840012345"), null);

        Assert.Null(result.CustomerId);
    }

    [Theory]
    [InlineData("+91 98400 12345", "9840012345")]
    [InlineData("098400-12345", "9840012345")]
    [InlineData("12345", null)]
    [InlineData(null, null)]
    public void Phone_numbers_compare_on_their_last_ten_digits(string? phone, string? expected)
    {
        Assert.Equal(expected, CustomerMatcher.PhoneKey(phone));
    }
}
