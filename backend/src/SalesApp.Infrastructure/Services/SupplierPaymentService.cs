using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Purchasing;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Core.Tax;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Services;

public interface ISupplierPaymentService
{
    Task<PagedResult<SupplierPaymentDto>> ListAsync(SupplierPaymentListRequest request, CancellationToken cancellationToken);
    Task<SupplierPaymentDto> CreateAsync(SupplierPaymentCreateRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Payments to suppliers against their bills. A bill whose prices or quantities didn't match its order can't be paid
/// until someone approves the differences. The bill's paid amount and status change in one guarded UPDATE, so two
/// people paying at once can never overpay it.
/// </summary>
public sealed class SupplierPaymentService(
    AppDbContext db,
    IBusinessClock clock,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ISupplierPaymentService
{
    /// <summary>Income-tax Act s.40A(3): cash paid to one person in a day above ₹10,000 isn't allowed as a business expense.</summary>
    public const decimal CashLimitPerDay = 10_000m;

    public async Task<PagedResult<SupplierPaymentDto>> ListAsync(SupplierPaymentListRequest request, CancellationToken cancellationToken)
    {
        var query = db.SupplierPayments.AsNoTracking();

        if (request.Mode is { } mode)
        {
            query = query.Where(p => p.Mode == mode);
        }

        if (request.SupplierId is { } supplierId)
        {
            query = query.Where(p => p.SupplierBill!.SupplierId == supplierId);
        }

        if (request.From is { } from)
        {
            query = query.Where(p => p.PaymentDate >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(p => p.PaymentDate <= to);
        }

        if (Text.NullIfBlank(request.Q) is { } q)
        {
            query = query.Where(p => p.SupplierBill!.Number.Contains(q) || p.SupplierBill.SupplierInvoiceNumber.Contains(q) ||
                                     p.SupplierBill.SupplierName.Contains(q) || (p.Reference != null && p.Reference.Contains(q)));
        }

        return await query
            .OrderByDescending(p => p.PaymentDate)
            .ThenByDescending(p => p.Id)
            .Select(p => new SupplierPaymentDto(p.Id, p.SupplierBillId, p.SupplierBill!.Number, p.SupplierBill.SupplierInvoiceNumber,
                p.SupplierBill.SupplierId, p.SupplierBill.SupplierName, p.PaymentDate, p.Amount, p.Mode, p.Reference, p.Notes,
                p.CreatedBy!.FullName, p.CreatedAtUtc))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<SupplierPaymentDto> CreateAsync(SupplierPaymentCreateRequest request, CancellationToken cancellationToken)
    {
        var bill = await db.SupplierBills.AsNoTracking()
            .Where(b => b.Id == request.SupplierBillId)
            .Select(b => new { b.Id, b.Number, b.Status, b.MatchStatus, b.BillDate, b.SupplierId, b.SupplierName, Balance = b.GrandTotal - b.AmountPaid - b.AmountCredited })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException("supplierBillId", "Choose a valid bill.");

        if (bill.Status == SupplierBillStatus.Cancelled)
        {
            throw new ValidationException("supplierBillId", $"{bill.Number} is cancelled; payments can't be recorded against it.");
        }

        if (bill.Status == SupplierBillStatus.Paid)
        {
            throw new ValidationException("amount", $"{bill.Number} is already fully paid.");
        }

        if (bill.MatchStatus == BillMatchStatus.NeedsApproval)
        {
            throw new ValidationException("supplierBillId",
                $"{bill.Number} doesn't match its purchase order. Someone who can approve bill differences has to approve it before it's paid.");
        }

        var today = clock.Today;
        var paymentDate = request.PaymentDate ?? today;
        if (paymentDate > today)
        {
            throw new ValidationException("paymentDate", "The payment date can't be in the future.");
        }

        if (paymentDate < bill.BillDate)
        {
            throw new ValidationException("paymentDate", "The payment date can't be before the bill date.");
        }

        if (request.Amount > bill.Balance)
        {
            throw new ValidationException("amount", $"Only ₹{AmountInWords.IndianGrouping(bill.Balance)} is due on {bill.Number}.");
        }

        if (request.Mode == PaymentMode.Cash)
        {
            var cashThatDay = await db.SupplierPayments
                .Where(p => p.Mode == PaymentMode.Cash && p.PaymentDate == paymentDate && p.SupplierBill!.SupplierId == bill.SupplierId)
                .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
            if (cashThatDay + request.Amount > CashLimitPerDay)
            {
                throw new ValidationException("amount",
                    $"Cash paid to {bill.SupplierName} on this day would reach ₹{AmountInWords.IndianGrouping(cashThatDay + request.Amount)}. " +
                    "Cash payments of more than ₹10,000 to one supplier in a day aren't allowed as a business expense (Income-tax Act s.40A(3)). " +
                    "Pay by bank transfer or cheque.");
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var amount = new SqlParameter { SqlDbType = SqlDbType.Decimal, Precision = 18, Scale = 2, Value = request.Amount };
        var updated = await db.Database.SqlQuery<decimal>($"""
            UPDATE [dbo].[SupplierBills]
            SET [AmountPaid] = [AmountPaid] + {amount},
                [Status] = CASE WHEN [AmountPaid] + [AmountCredited] + {amount} >= [GrandTotal] THEN 'Paid' ELSE 'PartiallyPaid' END
            OUTPUT inserted.[AmountPaid] AS [Value]
            WHERE [Id] = {bill.Id} AND [Status] <> 'Cancelled' AND [MatchStatus] <> 'NeedsApproval' AND [AmountPaid] + [AmountCredited] + {amount} <= [GrandTotal]
            """).ToListAsync(cancellationToken);

        if (updated.Count == 0)
        {
            throw new ConflictException($"{bill.Number} changed while this payment was being recorded (another payment or a cancellation). Reload and try again.");
        }

        var payment = new SupplierPayment
        {
            SupplierBillId = bill.Id,
            PaymentDate = paymentDate,
            Amount = request.Amount,
            Mode = request.Mode,
            Reference = Text.NullIfBlank(request.Reference),
            Notes = Text.NullIfBlank(request.Notes),
            CreatedByUserId = currentUser.UserId,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        };
        db.SupplierPayments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await db.SupplierPayments.AsNoTracking()
            .Where(p => p.Id == payment.Id)
            .Select(p => new SupplierPaymentDto(p.Id, p.SupplierBillId, p.SupplierBill!.Number, p.SupplierBill.SupplierInvoiceNumber,
                p.SupplierBill.SupplierId, p.SupplierBill.SupplierName, p.PaymentDate, p.Amount, p.Mode, p.Reference, p.Notes,
                p.CreatedBy!.FullName, p.CreatedAtUtc))
            .SingleAsync(cancellationToken);
    }
}
