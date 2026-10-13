using System.Net.Http.Json;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Entities;

namespace SalesApp.IntegrationTests;

/// <summary>Creates records through the API, the same way a user would.</summary>
public static class TestData
{
    public static string UniqueSku(string prefix = "T") => $"{prefix}-{Guid.NewGuid():N}"[..16].ToUpperInvariant();

    /// <summary>Creates a product using the seeded Cotton yarn category and HSN 5205 (5%). Needs a manager or admin client.</summary>
    public static async Task<ProductDto> CreateProductAsync(HttpClient managerClient, decimal reorderLevel = 100m, decimal? costPrice = 250m)
    {
        var response = await managerClient.PostAsJsonAsync("/api/products", new ProductSaveRequest
        {
            Sku = UniqueSku(),
            Name = "Test yarn",
            CategoryId = 1,
            HsnCodeId = 1,
            Unit = ProductUnit.Kg,
            ListPrice = 300m,
            CostPrice = costPrice,
            ReorderLevel = reorderLevel,
            ReorderQuantity = 500m,
            LeadTimeDays = 7,
        }, TestClients.Json);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<ProductDto>();
    }
}
