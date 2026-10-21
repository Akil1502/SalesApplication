using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using SalesApp.Api.Errors;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Contracts.Notifications;
using SalesApp.Core.Contracts.Reports;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;

namespace SalesApp.IntegrationTests;

/// <summary>
/// The API answers in the language the app is showing (Accept-Language), and printed documents follow the customer's
/// own language: Tamil errors, notifications and reports for a Tamil user, Tamil amounts in words for a Tamil customer.
/// </summary>
public sealed partial class LanguageTests(SalesAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpClient InTamil(HttpClient client)
    {
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ta-IN");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-IN;q=0.5");
        return client;
    }

    private static async Task<ApiError> ErrorAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        return await response.ReadAsync<ApiError>();
    }

    [GeneratedRegex(@"\p{IsTamil}")]
    private static partial Regex TamilLetters();

    [Fact]
    public async Task Errors_come_back_in_the_language_the_app_shows()
    {
        var (english, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (tamil, _) = await factory.CreateClientAsync(Roles.SalesManager);
        InTamil(tamil);

        var inEnglish = await ErrorAsync(await english.GetAsync("/api/invoices/987654", Ct), HttpStatusCode.NotFound);
        var inTamil = await ErrorAsync(await tamil.GetAsync("/api/invoices/987654", Ct), HttpStatusCode.NotFound);

        Assert.DoesNotMatch(TamilLetters(), inEnglish.Message);
        Assert.Matches(TamilLetters(), inTamil.Message);
        Assert.Contains("987654", inTamil.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Field_errors_are_translated_too()
    {
        var (tamil, _) = await factory.CreateClientAsync(Roles.SalesManager);
        InTamil(tamil);

        var response = await tamil.PostAsJsonAsync("/api/customers", new CustomerSaveRequest
        {
            Name = "",
            BillingAddress = new AddressInput { Line1 = "1 Market Street", City = "Test City", StateCode = "33", Pincode = "012345" },
        }, TestClients.Json, Ct);
        var error = await ErrorAsync(response, HttpStatusCode.BadRequest);

        Assert.Matches(TamilLetters(), error.Message);
        Assert.NotNull(error.Errors);
        Assert.All(error.Errors!.Values.SelectMany(messages => messages), message => Assert.Matches(TamilLetters(), message));
    }

    [Fact]
    public async Task A_customers_invoice_prints_in_their_language_with_the_amount_in_Tamil_words()
    {
        var (manager, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var product = await TestData.CreateStockedProductAsync(manager, 50m, listPrice: 100m);

        async Task<InvoiceDto> InvoiceForAsync(DocumentLanguage? language)
        {
            var response = await manager.PostAsJsonAsync("/api/customers", new CustomerSaveRequest
            {
                Name = $"Language {Guid.NewGuid():N}"[..20],
                Gstin = TestData.TamilNaduGstin,
                BillingAddress = new AddressInput { Line1 = "1 Market Street", City = "Test City", StateCode = "33", Pincode = "641601" },
                DocumentLanguage = language,
            }, TestClients.Json, Ct);
            response.EnsureSuccessStatusCode();
            var customer = await response.ReadAsync<CustomerDto>();
            Assert.Equal(language, customer.DocumentLanguage);
            var order = await TestData.ConfirmAsync(manager, await TestData.CreateOrderAsync(manager, customer.Id, (product.Id, 1m)));
            return await TestData.InvoiceAsync(manager, order.Id);
        }

        var tamil = await InvoiceForAsync(DocumentLanguage.Tamil);
        var both = await InvoiceForAsync(DocumentLanguage.Both);
        var unset = await InvoiceForAsync(null);

        Assert.Equal(DocumentLanguage.Tamil, tamil.DocumentLanguage);
        Assert.Equal(DocumentLanguage.Both, both.DocumentLanguage);
        // No choice of their own: the company's default, English unless changed in Company settings.
        Assert.Equal(DocumentLanguage.English, unset.DocumentLanguage);
        // ₹100 + 5% GST = ₹105.
        Assert.Equal("Rupees One Hundred Five Only", tamil.AmountInWords);
        Assert.Equal("ரூபாய் நூற்று ஐந்து மட்டும்", tamil.AmountInWordsTamil);
    }

    [Fact]
    public async Task Notifications_read_in_each_persons_own_language()
    {
        var (recorder, _) = await factory.CreateClientAsync(Roles.SalesManager);
        var (reader, _) = await factory.CreateClientAsync(Roles.SalesManager);
        InTamil(reader);
        var customer = await TestData.CreateCustomerAsync(recorder);
        var product = await TestData.CreateStockedProductAsync(recorder, 10m);
        var order = await TestData.ConfirmAsync(recorder, await TestData.CreateOrderAsync(recorder, customer.Id, (product.Id, 1m)));
        var invoice = await TestData.InvoiceAsync(recorder, order.Id);
        (await recorder.PostAsJsonAsync("/api/payments", new PaymentCreateRequest { InvoiceId = invoice.Id, Amount = 10m, Mode = PaymentMode.Upi }, TestClients.Json, Ct))
            .EnsureSuccessStatusCode();

        var list = await (await reader.GetAsync("/api/notifications?take=100", Ct)).ReadAsync<NotificationListDto>();
        var payment = Assert.Single(list.Items, n => n.Kind == NotificationKind.PaymentReceived && n.Link == $"/invoices/{invoice.Id}");

        Assert.Matches(TamilLetters(), payment.Title);
        Assert.Contains(customer.Name, payment.Title, StringComparison.Ordinal);
        Assert.Contains(invoice.Number, payment.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_and_their_headings_read_in_Tamil()
    {
        var (tamil, _) = await factory.CreateClientAsync(Roles.SalesManager);
        InTamil(tamil);

        var report = await (await tamil.GetAsync("/api/reports/sales-by-period", Ct)).ReadAsync<ReportResult>();

        Assert.Matches(TamilLetters(), report.Title);
        // Headings in Tamil, except tax names that stay as they are (GST).
        Assert.All(report.Columns.Where(c => c.Header != "GST"), column => Assert.Matches(TamilLetters(), column.Header));
    }
}
