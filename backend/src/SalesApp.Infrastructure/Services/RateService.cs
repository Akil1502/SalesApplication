using System.Data;
using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Masters;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Core.Tax;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.Reporting;

namespace SalesApp.Infrastructure.Services;

public interface IPriceService
{
    /// <summary>Each product's price on a day: the rate whose period covers it, else the list price.</summary>
    Task<IReadOnlyDictionary<int, decimal>> PricesOnAsync(IReadOnlyCollection<int> productIds, DateOnly day, CancellationToken cancellationToken);
}

public sealed class PriceService(AppDbContext db) : IPriceService
{
    public async Task<IReadOnlyDictionary<int, decimal>> PricesOnAsync(IReadOnlyCollection<int> productIds, DateOnly day, CancellationToken cancellationToken) =>
        await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                Price = db.ProductRates
                    .Where(r => r.ProductId == p.Id && r.EffectiveFrom <= day && (r.EffectiveTo == null || r.EffectiveTo >= day))
                    .Select(r => (decimal?)r.Rate)
                    .FirstOrDefault() ?? p.ListPrice,
            })
            .ToDictionaryAsync(p => p.Id, p => p.Price, cancellationToken);
}

public interface IRateService
{
    Task<PagedResult<ProductRateDto>> ListAsync(RateListRequest request, CancellationToken cancellationToken);
    Task<RateBatchResult> SaveBatchAsync(RateBatchRequest request, CancellationToken cancellationToken);
    Task<ProductRateDto> UpdateAsync(int id, RateUpdateRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(int id, CancellationToken cancellationToken);

    /// <summary>A sheet of active products with their price on <paramref name="day"/> and an empty "New rate" column.</summary>
    Task<byte[]> ExportAsync(DateOnly day, int? categoryId, string? q, CancellationToken cancellationToken);

    /// <summary>Reads a filled-in rate sheet. Nothing is saved: the rows go back to the screen to review.</summary>
    Task<RateImportResult> ImportAsync(Stream workbook, CancellationToken cancellationToken);
}

/// <summary>
/// Rate lists: each product's rate for a period. A product's periods never overlap, so there is never doubt about
/// which rate applies on a day. Saving a period that exactly matches an existing one changes its rate (re-entering
/// a month's list); any other overlap is refused, naming the product and the period in the way.
/// </summary>
public sealed class RateService(AppDbContext db, ICurrentUser currentUser, IBusinessClock clock) : IRateService
{
    internal const string SkuHeader = "SKU";
    internal const string NewRateHeader = "New rate";
    private const int MaxImportRows = 5000;

    public async Task<PagedResult<ProductRateDto>> ListAsync(RateListRequest request, CancellationToken cancellationToken)
    {
        var query = db.ProductRates.AsNoTracking();
        if (request.ProductId is { } productId)
        {
            query = query.Where(r => r.ProductId == productId);
        }

        if (request.ActiveOn is { } day)
        {
            query = query.Where(r => r.EffectiveFrom <= day && (r.EffectiveTo == null || r.EffectiveTo >= day));
        }

        if (Text.NullIfBlank(request.Q) is { } q)
        {
            query = query.Where(r => r.Product!.Sku.Contains(q) || r.Product.Name.Contains(q));
        }

        return await query
            .OrderByDescending(r => r.EffectiveFrom)
            .ThenBy(r => r.Product!.Name)
            .Select(r => new ProductRateDto(
                r.Id, r.ProductId, r.Product!.Sku, r.Product.Name, r.Product.Unit, r.Rate, r.EffectiveFrom, r.EffectiveTo, r.Note,
                r.UpdatedBy != null ? r.UpdatedBy.FullName : null, r.UpdatedAtUtc))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<RateBatchResult> SaveBatchAsync(RateBatchRequest request, CancellationToken cancellationToken)
    {
        EnsureValidPeriod(request.EffectiveFrom, request.EffectiveTo);
        var duplicate = request.Lines.GroupBy(l => l.ProductId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ValidationException("lines", "Each product can appear only once.");
        }

        var ids = request.Lines.Select(l => l.ProductId).ToList();

        // Serializable, so two people saving overlapping periods at once can't both succeed.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Sku, cancellationToken);
        var missing = ids.Where(id => !products.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new ValidationException("lines", $"{missing.Count} of the products no longer exist. Reload and try again.");
        }

        var existing = (await db.ProductRates.Where(r => ids.Contains(r.ProductId)).ToListAsync(cancellationToken)).ToLookup(r => r.ProductId);
        var problems = new List<string>();
        var created = 0;
        var updated = 0;
        foreach (var line in request.Lines)
        {
            var overlapping = existing[line.ProductId].Where(r => Overlaps(r.EffectiveFrom, r.EffectiveTo, request.EffectiveFrom, request.EffectiveTo)).ToList();
            if (overlapping is [var same] && same.EffectiveFrom == request.EffectiveFrom && same.EffectiveTo == request.EffectiveTo)
            {
                same.Rate = line.Rate;
                same.Note = Text.NullIfBlank(request.Note) ?? same.Note;
                same.UpdatedByUserId = currentUser.UserId;
                updated++;
            }
            else if (overlapping.Count > 0)
            {
                problems.Add($"{products[line.ProductId]} already has ₹{AmountInWords.IndianGrouping(overlapping[0].Rate)} for {Describe(overlapping[0].EffectiveFrom, overlapping[0].EffectiveTo)}");
            }
            else
            {
                db.ProductRates.Add(new ProductRate
                {
                    ProductId = line.ProductId,
                    Rate = line.Rate,
                    EffectiveFrom = request.EffectiveFrom,
                    EffectiveTo = request.EffectiveTo,
                    Note = Text.NullIfBlank(request.Note),
                    UpdatedByUserId = currentUser.UserId,
                });
                created++;
            }
        }

        if (problems.Count > 0)
        {
            var shown = string.Join("; ", problems.Take(5)) + (problems.Count > 5 ? $"; and {problems.Count - 5} more" : string.Empty);
            throw new ValidationException("lines", $"These periods overlap rates already set: {shown}. Change the dates, or edit or remove the existing rates.");
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RateBatchResult(created, updated);
    }

    public async Task<ProductRateDto> UpdateAsync(int id, RateUpdateRequest request, CancellationToken cancellationToken)
    {
        EnsureValidPeriod(request.EffectiveFrom, request.EffectiveTo);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var rate = await db.ProductRates.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Rate {id} was not found.");

        var other = await db.ProductRates.AsNoTracking()
            .Where(r => r.ProductId == rate.ProductId && r.Id != id)
            .ToListAsync(cancellationToken);
        var clash = other.FirstOrDefault(r => Overlaps(r.EffectiveFrom, r.EffectiveTo, request.EffectiveFrom, request.EffectiveTo));
        if (clash is not null)
        {
            throw new ValidationException("effectiveFrom", $"This overlaps the rate of ₹{AmountInWords.IndianGrouping(clash.Rate)} for {Describe(clash.EffectiveFrom, clash.EffectiveTo)}.");
        }

        rate.Rate = request.Rate;
        rate.EffectiveFrom = request.EffectiveFrom;
        rate.EffectiveTo = request.EffectiveTo;
        rate.Note = Text.NullIfBlank(request.Note);
        rate.UpdatedByUserId = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (await ListAsync(new RateListRequest { ProductId = rate.ProductId, PageSize = 200 }, cancellationToken)).Items.Single(r => r.Id == id);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        // Loaded and removed (not a bulk delete) so the audit trail records what was removed.
        var rate = await db.ProductRates.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Rate {id} was not found.");
        db.ProductRates.Remove(rate);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<byte[]> ExportAsync(DateOnly day, int? categoryId, string? q, CancellationToken cancellationToken)
    {
        var query = db.Products.AsNoTracking().Where(p => p.IsActive);
        if (categoryId is { } category)
        {
            query = query.Where(p => p.CategoryId == category);
        }

        if (Text.NullIfBlank(q) is { } search)
        {
            query = query.Where(p => p.Sku.Contains(search) || p.Name.Contains(search));
        }

        var products = await query
            .OrderBy(p => p.Category!.Name).ThenBy(p => p.Name)
            .Select(p => new
            {
                p.Sku,
                p.Name,
                Category = p.Category!.Name,
                p.Unit,
                Price = db.ProductRates
                    .Where(r => r.ProductId == p.Id && r.EffectiveFrom <= day && (r.EffectiveTo == null || r.EffectiveTo >= day))
                    .Select(r => (decimal?)r.Rate)
                    .FirstOrDefault() ?? p.ListPrice,
            })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Rates");
        sheet.Cell(1, 1).Value = $"Rate sheet: prices on {day.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        sheet.Cell(2, 1).Value = $"Fill in \"{NewRateHeader}\" (per unit, excluding GST) for the products to change, then upload this file on the Rate updates page. Leave a row blank to keep its rate.";
        sheet.Cell(2, 1).Style.Font.SetItalic();

        const int headerRow = 4;
        string[] headers = [SkuHeader, "Product", "Category", "Unit", "Current rate", NewRateHeader];
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = sheet.Cell(headerRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.SetBold();
            cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F1F5F9"));
            cell.Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
        }

        var row = headerRow + 1;
        foreach (var p in products)
        {
            sheet.Cell(row, 1).Value = p.Sku;
            sheet.Cell(row, 2).Value = p.Name;
            sheet.Cell(row, 3).Value = p.Category;
            sheet.Cell(row, 4).Value = p.Unit.ToString();
            sheet.Cell(row, 5).Value = p.Price;
            sheet.Cell(row, 5).Style.NumberFormat.Format = ExcelReportWriter.IndianMoney;
            sheet.Cell(row, 6).Style.NumberFormat.Format = ExcelReportWriter.IndianMoney;
            sheet.Cell(row, 6).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FEF9C3"));
            row++;
        }

        sheet.SheetView.FreezeRows(headerRow);
        sheet.Columns(1, headers.Length).AdjustToContents(headerRow, Math.Max(headerRow, row - 1));
        sheet.Column(6).Width = Math.Max(sheet.Column(6).Width, 14);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<RateImportResult> ImportAsync(Stream workbook, CancellationToken cancellationToken)
    {
        XLWorkbook book;
        try
        {
            book = new XLWorkbook(workbook);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException or IOException)
        {
            throw new ValidationException("file", "That isn't an Excel workbook (.xlsx). Download the rate sheet, fill it in and upload it.");
        }

        using (book)
        {
            var sheet = book.Worksheets.First();
            var (headerRow, skuColumn, rateColumn) = FindHeader(sheet)
                ?? throw new ValidationException("file", $"Couldn't find the \"{SkuHeader}\" and \"{NewRateHeader}\" columns. Use the downloaded rate sheet.");

            var rows = new List<(int Row, string Sku, decimal Rate)>();
            var problems = new List<string>();
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
            for (var r = headerRow + 1; r <= lastRow && rows.Count + problems.Count < MaxImportRows; r++)
            {
                var sku = sheet.Cell(r, skuColumn).GetFormattedString().Trim().ToUpperInvariant();
                var rateCell = sheet.Cell(r, rateColumn);
                if (rateCell.IsEmpty() || string.IsNullOrWhiteSpace(rateCell.GetFormattedString()))
                {
                    continue;
                }

                if (sku.Length == 0)
                {
                    problems.Add($"Row {r}: there's a rate but no SKU.");
                }
                else if (ReadRate(rateCell) is not { } rate)
                {
                    problems.Add($"Row {r} ({sku}): \"{rateCell.GetFormattedString()}\" isn't a rate.");
                }
                else if (rate < 0 || rate > 99999999)
                {
                    problems.Add($"Row {r} ({sku}): rate must be between 0 and 9,99,99,999.");
                }
                else if (decimal.Round(rate, 2) != rate)
                {
                    problems.Add($"Row {r} ({sku}): use at most 2 decimal places.");
                }
                else
                {
                    rows.Add((r, sku, rate));
                }
            }

            var skus = rows.Select(x => x.Sku).Distinct().ToList();
            var products = await db.Products.AsNoTracking()
                .Where(p => skus.Contains(p.Sku))
                .Select(p => new { p.Id, p.Sku, p.Name })
                .ToDictionaryAsync(p => p.Sku.ToUpperInvariant(), cancellationToken);

            var lines = new List<RateImportLineDto>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (row, sku, rate) in rows)
            {
                if (!products.TryGetValue(sku, out var product))
                {
                    problems.Add($"Row {row}: there's no product with SKU {sku}.");
                }
                else if (!seen.Add(sku))
                {
                    problems.Add($"Row {row}: {sku} appears more than once; the first row is used.");
                }
                else
                {
                    lines.Add(new RateImportLineDto(product.Id, product.Sku, product.Name, rate));
                }
            }

            return new RateImportResult(lines, problems);
        }
    }

    /// <summary>Whether two periods share a day; a missing end runs forever.</summary>
    public static bool Overlaps(DateOnly fromA, DateOnly? toA, DateOnly fromB, DateOnly? toB) =>
        fromA <= (toB ?? DateOnly.MaxValue) && fromB <= (toA ?? DateOnly.MaxValue);

    private static string Describe(DateOnly from, DateOnly? to) =>
        to is { } end
            ? $"{from.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} to {end.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}"
            : $"from {from.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} on";

    private void EnsureValidPeriod(DateOnly from, DateOnly? to)
    {
        if (from == default)
        {
            throw new ValidationException("effectiveFrom", "Choose the first day the rates apply.");
        }

        if (to is { } end && end < from)
        {
            throw new ValidationException("effectiveTo", "The last day can't be before the first day.");
        }

        if (from < clock.Today.AddYears(-5) || (to ?? from) > clock.Today.AddYears(5))
        {
            throw new ValidationException("effectiveFrom", "Choose dates within five years of today.");
        }
    }

    private static (int Row, int Sku, int Rate)? FindHeader(IXLWorksheet sheet)
    {
        for (var r = 1; r <= 10; r++)
        {
            int? sku = null, rate = null;
            foreach (var cell in sheet.Row(r).CellsUsed())
            {
                var text = cell.GetFormattedString().Trim();
                if (text.Equals(SkuHeader, StringComparison.OrdinalIgnoreCase))
                {
                    sku = cell.Address.ColumnNumber;
                }
                else if (text.Equals(NewRateHeader, StringComparison.OrdinalIgnoreCase))
                {
                    rate = cell.Address.ColumnNumber;
                }
            }

            if (sku is { } s && rate is { } n)
            {
                return (r, s, n);
            }
        }

        return null;
    }

    private static decimal? ReadRate(IXLCell cell)
    {
        if (cell.DataType == XLDataType.Number)
        {
            // Excel stores numbers as doubles; converting keeps 15 significant digits, so 123.45 stays 123.45.
            return (decimal)cell.GetDouble();
        }

        var text = cell.GetFormattedString().Replace("₹", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
