using System.Net.Http.Json;
using SalesApp.Core.Contracts.Inventory;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Contracts.Purchasing;
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

    /// <summary>A product with stock already on the shelf (an opening balance, so sales tests don't depend on purchasing).</summary>
    public static async Task<ProductDto> CreateStockedProductAsync(HttpClient managerClient, decimal quantity, decimal listPrice = 300m)
    {
        var product = await CreateProductAsync(managerClient, listPrice: listPrice);
        await AddOpeningStockAsync(managerClient, product.Id, quantity);
        return product;
    }

    public static async Task AddOpeningStockAsync(HttpClient managerClient, int productId, decimal quantity)
    {
        var response = await managerClient.PostAsJsonAsync("/api/stock/adjustments", new StockAdjustmentRequest
        {
            ProductId = productId,
            Mode = AdjustmentMode.Change,
            Quantity = quantity,
            Reason = AdjustmentReason.OpeningBalance,
            Notes = "Opening stock for a test",
        }, TestClients.Json);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<SupplierDto> CreateSupplierAsync(HttpClient client, string stateCode = "33", string? gstin = TamilNaduGstin, int paymentTermsDays = 30)
    {
        var response = await client.PostAsJsonAsync("/api/suppliers", new SupplierSaveRequest
        {
            Name = $"Mill {Guid.NewGuid():N}"[..20],
            Gstin = gstin,
            Address = new AddressInput { Line1 = "5 Mill Road", City = "Mill Town", StateCode = stateCode, Pincode = "641014" },
            PaymentTermsDays = paymentTermsDays,
        }, TestClients.Json);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<SupplierDto>();
    }

    /// <summary>A draft purchase order. Needs a client that may edit purchase orders.</summary>
    public static async Task<PurchaseOrderDto> CreatePurchaseOrderAsync(HttpClient client, int supplierId, params (int ProductId, decimal Quantity, decimal Price)[] lines)
    {
        var response = await client.PostAsJsonAsync("/api/purchase-orders", new PurchaseOrderSaveRequest
        {
            SupplierId = supplierId,
            Lines = [.. lines.Select(l => new PurchaseOrderLineInput { ProductId = l.ProductId, Quantity = l.Quantity, UnitPrice = l.Price })],
        }, TestClients.Json);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<PurchaseOrderDto>();
    }

    public static async Task<PurchaseOrderDto> PlaceAsync(HttpClient client, PurchaseOrderDto order)
    {
        var response = await client.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/place", new PurchaseOrderActionRequest { RowVersion = order.RowVersion }, TestClients.Json);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<PurchaseOrderDto>();
    }

    /// <summary>Receives the given quantities against the order's lines, in line order.</summary>
    public static async Task<StockReceiptDto> ReceiveAsync(HttpClient client, PurchaseOrderDto order, params decimal[] quantities)
    {
        var response = await client.PostAsJsonAsync($"/api/purchase-orders/{order.Id}/receipts", new GoodsReceiptCreateRequest
        {
            RowVersion = order.RowVersion,
            Lines = [.. order.Lines.Zip(quantities).Where(x => x.Second > 0).Select(x => new GoodsReceiptLineInput { PurchaseOrderLineId = x.First.Id, Quantity = x.Second })],
        }, TestClients.Json);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<StockReceiptDto>();
    }

    /// <summary>
    /// Stock in the way the app now requires it: a supplier, a purchase order placed with them, and the goods received
    /// in full against it. Keep the value under the approval limit (₹5,00,000 with GST) or use an admin client.
    /// </summary>
    public static async Task<(PurchaseOrderDto Order, StockReceiptDto Receipt)> BuyAsync(HttpClient client, params (int ProductId, decimal Quantity, decimal Price)[] lines)
    {
        var supplier = await CreateSupplierAsync(client);
        var order = await PlaceAsync(client, await CreatePurchaseOrderAsync(client, supplier.Id, lines));
        var receipt = await ReceiveAsync(client, order, [.. lines.Select(l => l.Quantity)]);
        return (order, receipt);
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
