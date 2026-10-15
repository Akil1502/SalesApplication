using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Services;

public interface IDocumentNumbers
{
    /// <summary>
    /// Allocates the next number for a document type in the financial year containing <paramref name="documentDate"/>,
    /// formatted as PREFIX/26-27/00001. Must run inside the transaction that saves the document.
    /// </summary>
    Task<string> NextAsync(string documentType, string prefix, DateOnly documentDate, CancellationToken cancellationToken);
}

/// <summary>
/// Gapless per-financial-year numbering. The counter row is incremented with MERGE ... HOLDLOCK inside the
/// caller's transaction: concurrent callers queue on the row lock, and if the document fails to save the
/// increment rolls back with it. That's what GST expects of invoice numbers (consecutive, no gaps) and
/// what SQL Server SEQUENCE objects can't promise.
/// </summary>
public sealed class DocumentNumbers(AppDbContext db) : IDocumentNumbers
{
    /// <summary>GST rule: an invoice number may have at most 16 characters.</summary>
    public const int MaxLength = 16;

    public async Task<string> NextAsync(string documentType, string prefix, DateOnly documentDate, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Document numbers must be allocated inside the transaction that saves the document.");
        }

        var financialYear = FinancialYear.StartYear(documentDate);

        // ToListAsync: composing would wrap MERGE in a subquery, which SQL Server doesn't allow.
        var allocated = (await db.Database.SqlQuery<int>($"""
            MERGE [dbo].[DocumentSequences] WITH (HOLDLOCK) AS target
            USING (SELECT {documentType} AS [DocumentType], {financialYear} AS [FinancialYear]) AS source
               ON target.[DocumentType] = source.[DocumentType] AND target.[FinancialYear] = source.[FinancialYear]
            WHEN MATCHED THEN
                UPDATE SET [NextNumber] = target.[NextNumber] + 1
            WHEN NOT MATCHED THEN
                INSERT ([DocumentType], [FinancialYear], [NextNumber]) VALUES (source.[DocumentType], source.[FinancialYear], 2)
            OUTPUT inserted.[NextNumber] - 1 AS [Value];
            """).ToListAsync(cancellationToken)).Single();

        return Format(prefix, documentDate, allocated);
    }

    public static string Format(string prefix, DateOnly date, int number)
    {
        var formatted = $"{prefix}/{FinancialYear.Label(date)}/{number:D5}";
        return formatted.Length <= MaxLength
            ? formatted
            : throw new ConflictException($"Document number {formatted} would exceed {MaxLength} characters. Use a shorter prefix in Company settings.");
    }
}
