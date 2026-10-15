using SalesApp.Api.Errors;

namespace SalesApp.Tests.Errors;

public sealed class ValidationErrorResponseTests
{
    [Theory]
    [InlineData("Email", "email")]
    [InlineData("$.email", "email")]
    [InlineData("Lines[0].Quantity", "lines[0].quantity")]
    [InlineData("BillingAddress.StateCode", "billingAddress.stateCode")]
    [InlineData("$", "body")]
    [InlineData("", "body")]
    public void Model_state_keys_become_camel_case_field_paths(string key, string expected)
    {
        Assert.Equal(expected, ValidationErrorResponse.ToFieldName(key));
    }
}
