using System.Net.Http.Json;
using SalesApp.Core.Contracts.Inventory;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Entities;

namespace SalesApp.IntegrationTests;

/// <summary>Creates records through the API, the same way a user would.</summary>
public static class TestData
{
    /// <summary>Valid GSTINs (correct check characters) for a Tamil Nadu and a Karnataka buyer.</summary>
    public const string TamilNaduGstin = "33ABCDE1234F1Z7";
    public const string KarnatakaGstin = "29AAGCB7383J1Z4";

    public static string UniqueSku(string prefix = "T") => $"{prefix}-{Guid.NewGuid():N}"[..16].ToUpperInvariant();

    /// <summary>Creates a product using the seeded Cotton yarn category and HSN 5205 (5%). Needs a manager or admin client.</summary>
    public static async Task<ProductDto> CreateProductAsync(HttpClient managerClient, decimal reorderLevel = 100m, decimal? costPrice = 250m, decimal listPrice = 300m)
    {
        var response = await managerClient.PostAsJsonAsync("/api/products", new ProductSaveRequest
        {
            Sku = UniqueSku(),
            Name = "Test yarn",
            CategoryId = 1,
            HsnCodeId = 1,
            Unit = ProductUnit.Kg,
            ListPrice = listPrice,
            CostPrice = costPrice,
            ReorderLevel = reorderLevel,
            ReorderQuantity = 500m,
            LeadTimeDays = 7,
        }, TestClients.Json);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<ProductDto>();
    }

    /// <summary>A product with stock already received.</summary>
    public static async Task<ProductDto> CreateStockedProductAsync(HttpClient managerClient, decimal quantity, decimal listPrice = 300m)
    {
        var product = await CreateProductAsync(managerClient, listPrice: listPrice);
        var receipt = await managerClient.PostAsJsonAsync("/api/stock/receipts", new StockReceiptCreateRequest
        {
            Lines = [new StockReceiptLineInput { ProductId = product.Id, Quantity = quantity, UnitCost = 250m }],
        }, TestClients.Json);
        receipt.EnsureSuccessStatusCode();
        return product;
    }

    public static async Task<CustomerDto> CreateCustomerAsync(HttpClient client, string stateCode = "33", string? gstin = TamilNaduGstin,
        decimal? creditLimit = null, int paymentTermsDays = 30)
    {
        var response = await client.PostAsJsonAsync("/api/customers", new CustomerSaveRequest
        {
            Name = $"Buyer {Guid.NewGuid():N}"[..20],
            Gstin = gstin,
            BillingAddress = new AddressInput { Line1 = "1 Market Street", City = "Test City", StateCode = stateCode, Pincode = "641601" },
            CreditLimit = creditLimit,
            PaymentTermsDays = paymentTermsDays,
        }, TestClients.Json);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<CustomerDto>();
    }

    public static async Task<OrderDto> CreateOrderAsync(HttpClient client, int customerId, params (int ProductId, decimal Quantity)[] lines)
    {
        var response = await client.PostAsJsonAsync("/api/orders", new OrderSaveRequest
        {
            CustomerId = customerId,
            Lines = [.. lines.Select(l => new OrderLineInput { ProductId = l.ProductId, Quantity = l.Quantity })],
        }, TestClients.Json);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<OrderDto>();
    }

    public static async Task<OrderDto> ConfirmAsync(HttpClient client, OrderDto order, bool overrideCreditLimit = false)
    {
        var response = await client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm",
            new OrderConfirmRequest { RowVersion = order.RowVersion, OverrideCreditLimit = overrideCreditLimit }, TestClients.Json);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<OrderDto>();
    }

    public static async Task<InvoiceDto> InvoiceAsync(HttpClient managerClient, int orderId, DateOnly? invoiceDate = null)
    {
        var response = await managerClient.PostAsJsonAsync("/api/invoices", new InvoiceCreateRequest { SalesOrderId = orderId, InvoiceDate = invoiceDate }, TestClients.Json);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<InvoiceDto>();
    }
}
