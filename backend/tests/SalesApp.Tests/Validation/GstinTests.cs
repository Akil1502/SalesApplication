using SalesApp.Core.Validation;

namespace SalesApp.Tests.Validation;

public sealed class GstinTests
{
    [Theory]
    [InlineData("27AAPFU0939F1ZV")] // Maharashtra; widely published example
    [InlineData("29AAGCB7383J1Z4")] // Karnataka
    [InlineData("33ABCDE1234F1Z7")] // Tamil Nadu; the seeded demo company
    public void Valid_gstins_pass(string gstin)
    {
        Assert.Null(Gstin.Validate(gstin));
        Assert.True(Gstin.IsValid(gstin));
    }

    [Fact]
    public void Lower_case_and_spaces_are_normalised_before_checking()
    {
        Assert.True(Gstin.IsValid(" 33abcde 1234f1z7 "));
        Assert.Equal("33ABCDE1234F1Z7", Gstin.Normalize(" 33abcde 1234f1z7 "));
    }

    [Theory]
    [InlineData("33ABCDE1234F1Z8")] // last character changed
    [InlineData("33ABCDE1243F1Z7")] // two digits transposed
    [InlineData("27AAPFU0939F2ZV")] // entity number changed
    public void Typos_are_caught_by_the_check_character(string gstin)
    {
        Assert.Contains("check character", Gstin.Validate(gstin), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("33ABCDE1234F1Z", "exactly 15")]
    [InlineData("33ABCDE1234F1Z77", "exactly 15")]
    [InlineData("3XABCDE1234F1Z7", "format")]
    [InlineData("33ABCD11234F1Z7", "format")]
    [InlineData("33ABCDE1234F1A7", "format")] // 14th character must be Z
    [InlineData("33ABCDE1234F0Z7", "format")] // entity number can't be 0
    public void Malformed_gstins_get_a_specific_message(string gstin, string expectedFragment)
    {
        Assert.Contains(expectedFragment, Gstin.Validate(gstin), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void State_code_and_pan_are_extracted()
    {
        Assert.Equal("33", Gstin.StateCode("33ABCDE1234F1Z7"));
        Assert.Equal("ABCDE1234F", Gstin.Pan("33ABCDE1234F1Z7"));
    }

    [Fact]
    public void Check_character_can_be_computed_for_test_data()
    {
        Assert.Equal('V', Gstin.ComputeCheckCharacter("27AAPFU0939F1Z"));
    }
}
