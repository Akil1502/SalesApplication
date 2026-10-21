using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.Services;

namespace SalesApp.IntegrationTests;

/// <summary>Document numbers allocated by transactions running at the same time.</summary>
public sealed class DocumentNumbersTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Numbering_one_document_type_never_waits_for_another()
    {
        // A year no other test numbers in, and both transactions roll back, so no real counter is touched.
        var date = new DateOnly(2040, 6, 1);
        using var orderScope = factory.Services.CreateScope();
        using var billScope = factory.Services.CreateScope();
        var orderDb = orderScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var billDb = billScope.ServiceProvider.GetRequiredService<AppDbContext>();

        // A purchase order is being saved: its counter row stays locked until its transaction ends.
        await using var order = await orderDb.Database.BeginTransactionAsync(Ct);
        Assert.Equal("PO/40-41/00001", await orderScope.ServiceProvider.GetRequiredService<IDocumentNumbers>()
            .NextAsync(DocumentTypes.PurchaseOrder, "PO", date, Ct));

        // A supplier bill numbered meanwhile touches only its own counter row. Waiting on the order's row is how two
        // saves of different documents deadlocked, so a wait fails the test.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await using var bill = await billDb.Database.BeginTransactionAsync(Ct);
        Assert.Equal("PB/40-41/00001", await billScope.ServiceProvider.GetRequiredService<IDocumentNumbers>()
            .NextAsync(DocumentTypes.SupplierBill, "PB", date, timeout.Token));
    }
}
