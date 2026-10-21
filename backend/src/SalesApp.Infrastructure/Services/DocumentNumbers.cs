using System.Data;
using Microsoft.Data.SqlClient;
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
/// Gapless per-financial-year numbering. The counter row is incremented inside the caller's transaction, so
/// if the document fails to save the increment rolls back with it. That's what GST expects of invoice numbers
/// (consecutive, no gaps) and what SQL Server SEQUENCE objects can't promise.
/// <para>
/// Callers for the same type and year queue on an application lock held until their transaction ends.
/// It replaces MERGE ... HOLDLOCK, whose key-range locks deadlocked when several transactions created
/// the first number of a year for different document types at the same moment.
/// </para>
/// <para>
/// The type goes to SQL as varchar(20), like the key column. EF's default nvarchar made SQL Server convert the
/// column and scan every type's counter row instead of seeking its own, so a purchase order and a supplier bill
/// numbered at the same moment could still deadlock.
/// </para>
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
        var lockName = $"DocumentNumber:{documentType}:{financialYear}";
        var type = new SqlParameter { SqlDbType = SqlDbType.VarChar, Size = 20, Value = documentType };

        // ToListAsync: composing would wrap MERGE in a subquery, which SQL Server doesn't allow.
        var allocated = (await db.Database.SqlQuery<int>($"""
            DECLARE @locked int;
            EXEC @locked = sp_getapplock @Resource = {lockName}, @LockMode = 'Exclusive', @LockOwner = 'Transaction';
            IF @locked < 0 THROW 51000, 'Could not lock the document number counter.', 1;
            MERGE [dbo].[DocumentSequences] AS target
            USING (SELECT {type} AS [DocumentType], {financialYear} AS [FinancialYear]) AS source
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
