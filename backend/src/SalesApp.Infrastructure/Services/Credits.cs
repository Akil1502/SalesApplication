using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Entities;
using SalesApp.Core.Returns;
using SalesApp.Core.Tax;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Services;

/// <summary>
/// Credit on invoices and supplier bills, shared by payments, credit notes and supplier returns. What's due on a document
/// is GrandTotal - AmountPaid - AmountCredited; every change to it is one guarded UPDATE, so two people settling the same
/// document at once can never take it past its total (or below zero).
/// </summary>
internal static class Credits
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    /// <summary>
    /// Adds credit to an invoice (a negative amount takes it back off when a credit note is cancelled) and resets its
    /// status from what's now settled. False when the invoice is cancelled or the amount no longer fits.
    /// </summary>
    public static async Task<bool> CreditInvoiceAsync(AppDbContext db, int invoiceId, decimal amount, CancellationToken cancellationToken)
    {
        var value = Money(amount);
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [dbo].[Invoices]
            SET [AmountCredited] = [AmountCredited] + {value},
                [Status] = CASE WHEN [AmountPaid] + [AmountCredited] + {value} >= [GrandTotal] THEN 'Paid'
                                WHEN [AmountPaid] + [AmountCredited] + {value} > 0 THEN 'PartiallyPaid'
                                ELSE 'Unpaid' END
            WHERE [Id] = {invoiceId} AND [Status] <> 'Cancelled'
              AND [AmountPaid] + [AmountCredited] + {value} <= [GrandTotal] AND [AmountCredited] + {value} >= 0
            """, cancellationToken);
        return rows == 1;
    }

    /// <summary>The same for a supplier bill: what we owe drops by the returned goods' value.</summary>
    public static async Task<bool> CreditBillAsync(AppDbContext db, int billId, decimal amount, CancellationToken cancellationToken)
    {
        var value = Money(amount);
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [dbo].[SupplierBills]
            SET [AmountCredited] = [AmountCredited] + {value},
                [Status] = CASE WHEN [AmountPaid] + [AmountCredited] + {value} >= [GrandTotal] THEN 'Paid'
                                WHEN [AmountPaid] + [AmountCredited] + {value} > 0 THEN 'PartiallyPaid'
                                ELSE 'Unpaid' END
            WHERE [Id] = {billId} AND [Status] <> 'Cancelled'
              AND [AmountPaid] + [AmountCredited] + {value} <= [GrandTotal] AND [AmountCredited] + {value} >= 0
            """, cancellationToken);
        return rows == 1;
    }

    /// <summary>
    /// Holds the invoice's row until the transaction ends, so two credit notes for the same invoice are worked out one
    /// after the other and can't both take back the same goods.
    /// </summary>
    public static Task LockInvoiceAsync(AppDbContext db, int invoiceId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM [dbo].[Invoices] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {invoiceId}", cancellationToken);

    public static Task LockBillAsync(AppDbContext db, int billId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM [dbo].[SupplierBills] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {billId}", cancellationToken);

    /// <summary>What issued (not cancelled) credit notes have taken back from each line of the invoice.</summary>
    public static async Task<Dictionary<int, CreditedSoFar>> CreditedSoFarAsync(AppDbContext db, int invoiceId, CancellationToken cancellationToken)
    {
        var rows = await (
                from l in db.CreditNoteLines
                join c in db.CreditNotes on l.CreditNoteId equals c.Id
                where c.InvoiceId == invoiceId && c.Status == ReturnDocumentStatus.Issued
                group l by l.InvoiceLineId into g
                select new
                {
                    LineId = g.Key,
                    Quantity = g.Sum(l => l.Quantity ?? 0m),
                    Taxable = g.Sum(l => l.TaxableValue),
                    Cgst = g.Sum(l => l.CgstAmount),
                    Sgst = g.Sum(l => l.SgstAmount),
                    Igst = g.Sum(l => l.IgstAmount),
                })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.LineId, r => new CreditedSoFar(r.Quantity, r.Taxable, r.Cgst, r.Sgst, r.Igst));
    }

    /// <summary>What issued supplier returns have taken back from each line of the bill.</summary>
    public static async Task<Dictionary<int, CreditedSoFar>> ReturnedSoFarAsync(AppDbContext db, int billId, CancellationToken cancellationToken)
    {
        var rows = await (
                from l in db.SupplierReturnLines
                join r in db.SupplierReturns on l.SupplierReturnId equals r.Id
                where r.SupplierBillId == billId && r.Status == ReturnDocumentStatus.Issued
                group l by l.SupplierBillLineId into g
                select new
                {
                    LineId = g.Key,
                    Quantity = g.Sum(l => l.Quantity),
                    Taxable = g.Sum(l => l.TaxableValue),
                    Cgst = g.Sum(l => l.CgstAmount),
                    Sgst = g.Sum(l => l.SgstAmount),
                    Igst = g.Sum(l => l.IgstAmount),
                })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.LineId, r => new CreditedSoFar(r.Quantity, r.Taxable, r.Cgst, r.Sgst, r.Igst));
    }

    /// <summary>Credit the customer has with us: issued credit notes not yet set against invoices or refunded.</summary>
    public static async Task<decimal> CustomerCreditAsync(AppDbContext db, int customerId, CancellationToken cancellationToken) =>
        await db.CreditNotes
            .Where(c => c.CustomerId == customerId && c.Status == ReturnDocumentStatus.Issued)
            .SumAsync(c => (decimal?)(c.GrandTotal - c.AmountAdjusted - c.AmountRefunded), cancellationToken) ?? 0m;

    /// <summary>Credit we have with the supplier: issued returns not yet set against their bills or refunded.</summary>
    public static async Task<decimal> SupplierCreditAsync(AppDbContext db, int supplierId, CancellationToken cancellationToken) =>
        await db.SupplierReturns
            .Where(r => r.SupplierId == supplierId && r.Status == ReturnDocumentStatus.Issued)
            .SumAsync(r => (decimal?)(r.GrandTotal - r.AmountAdjusted - r.AmountRefunded), cancellationToken) ?? 0m;

    /// <summary>
    /// Why an invoice can no longer be cancelled, or null if it still can: it must be unpaid, uncredited and still in its
    /// own month, because once the month is over it has gone into that month's GST return.
    /// </summary>
    public static string? InvoiceCancelBlockedReason(Invoice invoice, bool hasCreditNotes, DateOnly today)
    {
        if (invoice.Status == InvoiceStatus.Cancelled)
        {
            return $"{invoice.Number} is already cancelled.";
        }

        if (invoice.AmountPaid > 0)
        {
            return $"{invoice.Number} has payments recorded against it, so it can't be cancelled. Issue a credit note instead.";
        }

        if (hasCreditNotes)
        {
            return $"{invoice.Number} has credit notes against it, so it can't be cancelled. Issue a further credit note instead.";
        }

        if (invoice.AmountCredited > 0)
        {
            return $"Customer credit has been set against {invoice.Number}, so it can't be cancelled. Issue a credit note instead.";
        }

        return ReturnRules.InSameMonth(invoice.InvoiceDate, today)
            ? null
            : $"{invoice.Number} is from {invoice.InvoiceDate.ToString("MMMM yyyy", India)}, which has gone into that month's GST return (GSTR-1). Issue a credit note instead.";
    }

    /// <summary>Why a credit note or supplier return can no longer be cancelled, or null if it still can.</summary>
    public static string? ReturnCancelBlockedReason(string number, ReturnDocumentStatus status, DateOnly date, decimal refunded, string? usedElsewhere, DateOnly today)
    {
        if (status == ReturnDocumentStatus.Cancelled)
        {
            return $"{number} is already cancelled.";
        }

        if (refunded > 0)
        {
            return $"Refunds are recorded against {number}, so it can't be cancelled.";
        }

        if (usedElsewhere is not null)
        {
            return $"Credit from {number} is set against {usedElsewhere}, so it can't be cancelled.";
        }

        return ReturnRules.InSameMonth(date, today)
            ? null
            : $"{number} is from {date.ToString("MMMM yyyy", India)}, which has gone into that month's GST return, so it can't be cancelled.";
    }

    public static string Rupees(decimal amount) => $"₹{AmountInWords.IndianGrouping(amount)}";

    private static SqlParameter Money(decimal amount) =>
        new() { SqlDbType = SqlDbType.Decimal, Precision = 18, Scale = 2, Value = amount };
}
