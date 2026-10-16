using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesApp.AI.Claude;
using SalesApp.AI.OrderEntry;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Ai;
using SalesApp.Core.Contracts.Purchasing;
using SalesApp.Core.Entities;
using SalesApp.Core.Tax;
using SalesApp.Core.Validation;
using SalesApp.Infrastructure.Data;

namespace SalesApp.AI.Purchasing;

public interface IBillReadingService
{
    /// <summary>Reads a supplier's bill for a purchase order. Nothing is saved apart from the usage log.</summary>
    Task<BillExtractionDto> ReadAsync(int purchaseOrderId, byte[] file, CancellationToken cancellationToken);
}

/// <summary>
/// Reads a supplier's bill from a PDF or photo with one Claude call (no tools, structured output), matched against
/// the lines of the purchase order it's for. Then everything is checked against the database: order line ids must be
/// on the order, the GSTIN should be the supplier's, the invoice must not be entered already, and the lines must add
/// up to the printed total. Problems become warnings for the clerk, who saves the bill through the normal form.
/// </summary>
public sealed class BillReadingService(
    AppDbContext db,
    IClaudeGateway claude,
    IBusinessClock clock,
    IOptionsMonitor<AiOptions> options,
    ILogger<BillReadingService> logger) : IBillReadingService
{
    private static readonly JsonSerializerOptions ReplyJson = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    private static readonly PurchaseOrderStatus[] Billable =
        [PurchaseOrderStatus.Ordered, PurchaseOrderStatus.PartlyReceived, PurchaseOrderStatus.Received, PurchaseOrderStatus.Closed];

    public async Task<BillExtractionDto> ReadAsync(int purchaseOrderId, byte[] file, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders.AsNoTracking()
            .Include(o => o.Supplier)
            .Include(o => o.Lines).ThenInclude(l => l.Product!).ThenInclude(p => p.HsnCode)
            .SingleOrDefaultAsync(o => o.Id == purchaseOrderId, cancellationToken)
            ?? throw new ValidationException("purchaseOrderId", "Choose the purchase order the bill is for.");
        if (!Billable.Contains(order.Status))
        {
            throw new ValidationException("purchaseOrderId", $"{order.Number} hasn't been placed or is cancelled, so no bill can be entered against it.");
        }

        var (content, source) = ReadFile(file);
        var supplier = order.Supplier!;
        var lines = order.Lines.OrderBy(l => l.LineNumber).ToList();
        var entries = lines.Select(l => new OrderLineEntry(l.Id, l.Product!.Sku, l.Product.Name, l.Product.Unit.ToString(), l.Product.HsnCode!.Code,
            l.Quantity, l.QuantityReceived, l.QuantityBilled, l.UnitPrice, l.GstRate));

        var response = await claude.SendAsync(new ClaudeRequest(
            AiFeatures.BillReading,
            [new ClaudeSystemBlock(BillReadingPrompt.Instructions, CacheAfter: true)],
            [content, new ClaudeText(BillReadingPrompt.UserText(clock.Today, order.Number, supplier.Name, entries))],
            BillReadingPrompt.Schema), cancellationToken);

        var reply = Parse(response.Text);
        var warnings = new List<string>();
        var used = new HashSet<int>();
        var extracted = new List<BillExtractedLineDto>();
        foreach (var line in reply.IsInvoice ? reply.Lines.Take(200) : [])
        {
            var orderLine = lines.FirstOrDefault(l => l.Id == line.PoLineId);
            var note = Blank(line.Note);
            if (orderLine is not null && !used.Add(orderLine.Id))
            {
                // Two bill lines on one order line can't be saved; the clerk merges or reassigns them.
                orderLine = null;
                note = "Another line of the bill is already matched to this product; check which is right.";
            }
            else if (orderLine is null && line.PoLineId != 0)
            {
                note = "The suggested order line isn't on this order; choose one.";
            }

            var confidence = orderLine is null ? ExtractionConfidence.Low : Confidence(line.Confidence);
            extracted.Add(new BillExtractedLineDto(
                Clip(line.SourceText?.Trim() ?? "", 200) ?? "",
                orderLine?.Id, orderLine?.ProductId, orderLine?.Product!.Sku, orderLine?.Product!.Name, orderLine?.Product!.Unit,
                line.Quantity > 0 ? Math.Round(line.Quantity, 3) : null,
                line.UnitPrice > 0 ? Math.Round(line.UnitPrice, 2) : null,
                line.GstRate is >= 0 and <= 28 ? Math.Round(line.GstRate, 2) : null,
                orderLine?.UnitPrice, confidence, note));
        }

        var unmatchedLines = extracted.Count(l => l.PurchaseOrderLineId is null);
        if (unmatchedLines > 0)
        {
            warnings.Add(unmatchedLines == 1
                ? "One line of the bill couldn't be matched to the order. Choose the order line or leave it off."
                : $"{unmatchedLines} lines of the bill couldn't be matched to the order. Choose the order lines or leave them off.");
        }

        // The supplier and the invoice.
        var gstin = Blank(reply.SupplierGstin)?.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        if (gstin is not null && supplier.Gstin is not null && gstin != supplier.Gstin)
        {
            warnings.Add($"The bill's GSTIN {gstin} isn't {supplier.Name}'s ({supplier.Gstin}). Check it's really their invoice.");
        }
        else if (gstin is not null && !Gstin.IsValid(gstin))
        {
            warnings.Add($"The GSTIN on the bill, {gstin}, isn't a valid GSTIN; it may have been misread.");
        }

        var invoiceNumber = Clip(Blank(reply.InvoiceNumber), 30);
        if (invoiceNumber is not null)
        {
            var existing = await db.SupplierBills.AsNoTracking()
                .Where(b => b.SupplierId == supplier.Id && b.SupplierInvoiceNumber == invoiceNumber && b.Status != SupplierBillStatus.Cancelled)
                .Select(b => b.Number).FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                warnings.Add($"Invoice {invoiceNumber} from {supplier.Name} is already entered as {existing}.");
            }
        }

        var poNumber = Blank(reply.PoNumber);
        if (poNumber is not null && !string.Equals(poNumber, order.Number, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"The bill quotes order {poNumber}, not {order.Number}. Check it's for this order.");
        }

        var billDate = Date(reply.InvoiceDate);
        if (billDate > clock.Today)
        {
            warnings.Add("The invoice date is in the future; it may have been misread.");
        }

        // Charges become one amount on the bill, at the rate of the largest charge.
        var charges = reply.Charges.Where(c => c.Amount > 0).ToList();
        var chargesAmount = Math.Round(charges.Sum(c => c.Amount), 2);
        var chargesRate = charges.Count == 0 ? 0m : Math.Round(Math.Clamp(charges.MaxBy(c => c.Amount)!.GstRate, 0m, 28m), 2);
        var chargesText = charges.Count == 0 ? null : Clip(string.Join(", ", charges.Select(c => Blank(c.Description) ?? "Charges").Distinct()), 100);
        if (charges.Select(c => c.GstRate).Distinct().Count() > 1)
        {
            warnings.Add("The bill's charges are taxed at different rates; they're entered together at the rate of the largest. Check the GST.");
        }

        // Do the lines add up to what the supplier printed? Worked out the way the bill will be saved.
        var statedTotal = reply.GrandTotal > 0 ? Math.Round(reply.GrandTotal, 2) : (decimal?)null;
        var priced = extracted.Where(l => l.Quantity is not null && l.UnitPrice is not null).ToList();
        if (statedTotal is { } stated && priced.Count == extracted.Count && priced.Count > 0)
        {
            var companyState = await db.CompanySettings.AsNoTracking().Select(s => s.StateCode).SingleAsync(cancellationToken);
            var gstLines = priced.Select(l => new GstLineInput(l.Quantity!.Value, l.UnitPrice!.Value, 0m, l.GstRate ?? 0m)).ToList();
            if (chargesAmount > 0)
            {
                gstLines.Add(new GstLineInput(1m, chargesAmount, 0m, chargesRate));
            }

            var ours = GstCalculator.Calculate(gstLines, supplier.Address.StateCode != companyState).GrandTotal;
            if (Math.Abs(ours - stated) > 1m)
            {
                warnings.Add($"The lines and charges come to ₹{AmountInWords.IndianGrouping(ours)} with GST, but the bill's total is ₹{AmountInWords.IndianGrouping(stated)}. Check the quantities, rates and charges.");
            }
        }

        if (!reply.IsInvoice)
        {
            warnings.Insert(0, "This doesn't look like a supplier's tax invoice.");
        }

        var usage = response.Usage;
        return new BillExtractionDto(
            source,
            reply.IsInvoice,
            Clip(Blank(reply.SupplierName), 150),
            gstin,
            invoiceNumber,
            billDate,
            Date(reply.DueDate),
            poNumber,
            extracted,
            chargesAmount,
            chargesRate,
            chargesText,
            reply.TaxableTotal > 0 ? Math.Round(reply.TaxableTotal, 2) : null,
            reply.TaxTotal > 0 ? Math.Round(reply.TaxTotal, 2) : null,
            statedTotal,
            warnings,
            reply.Unmatched.Where(u => !string.IsNullOrWhiteSpace(u)).Take(50).ToList(),
            Clip(Blank(reply.Notes), 1000),
            new AiUsageDto(usage.Model, usage.InputTokens, usage.OutputTokens, usage.CacheReadTokens, usage.CacheWriteTokens,
                usage.CostUsd, Math.Round(usage.CostUsd * options.CurrentValue.UsdToInr, 2), usage.DurationMs));
    }

    private static (ClaudeContent Content, BillSource Source) ReadFile(byte[] file)
    {
        if (file is not { Length: > 0 })
        {
            throw new ValidationException("file", "Attach the supplier's bill: a PDF or a photo.");
        }

        var type = OrderFile.Detect(file)
            ?? throw new ValidationException("file", "Attach a PDF or an image (JPEG, PNG, GIF or WebP).");

        if (type.Kind == OrderFileKind.Pdf)
        {
            return file.Length > OrderFile.MaxPdfBytes
                ? throw new ValidationException("file", "PDFs can be at most 10 MB.")
                : (new ClaudePdf(file), BillSource.AiPdf);
        }

        return file.Length > OrderFile.MaxImageBytes
            ? throw new ValidationException("file", "Images can be at most 5 MB.")
            : (new ClaudeImage(file, type.MediaType), BillSource.AiImage);
    }

    private Reply Parse(string json)
    {
        try
        {
            var reply = JsonSerializer.Deserialize<ModelReply>(json, ReplyJson) ?? throw new JsonException("Empty reply");
            return new Reply(reply.IsInvoice, reply.SupplierName, reply.SupplierGstin, reply.InvoiceNumber, reply.InvoiceDate, reply.DueDate, reply.PoNumber,
                reply.Lines ?? [], reply.Charges ?? [], reply.TaxableTotal, reply.TaxTotal, reply.GrandTotal, reply.Unmatched ?? [], reply.Notes);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Bill reading reply wasn't valid JSON");
            throw new AiServiceException("Claude's reply couldn't be read. Try again, or enter the bill by hand.", 502, exception);
        }
    }

    private static ExtractionConfidence Confidence(string? value) => value?.ToUpperInvariant() switch
    {
        "HIGH" => ExtractionConfidence.High,
        "MEDIUM" => ExtractionConfidence.Medium,
        _ => ExtractionConfidence.Low,
    };

    private static DateOnly? Date(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", India, DateTimeStyles.None, out var date) ? date : null;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Clip(string? value, int max) => value is null || value.Length <= max ? value : value[..max];

    private sealed record ModelReply(
        bool IsInvoice,
        string? SupplierName,
        string? SupplierGstin,
        string? InvoiceNumber,
        string? InvoiceDate,
        string? DueDate,
        string? PoNumber,
        List<ModelLine>? Lines,
        List<ModelCharge>? Charges,
        decimal TaxableTotal,
        decimal TaxTotal,
        decimal GrandTotal,
        List<string>? Unmatched,
        string? Notes);

    /// <summary>The reply with every list present, whatever the model left out.</summary>
    private sealed record Reply(
        bool IsInvoice,
        string? SupplierName,
        string? SupplierGstin,
        string? InvoiceNumber,
        string? InvoiceDate,
        string? DueDate,
        string? PoNumber,
        List<ModelLine> Lines,
        List<ModelCharge> Charges,
        decimal TaxableTotal,
        decimal TaxTotal,
        decimal GrandTotal,
        List<string> Unmatched,
        string? Notes);

    private sealed record ModelLine(string? SourceText, int PoLineId, decimal Quantity, decimal UnitPrice, decimal GstRate, string? Confidence, string? Note);

    private sealed record ModelCharge(string? Description, decimal Amount, decimal GstRate);
}
