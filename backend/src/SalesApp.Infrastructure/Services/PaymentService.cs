using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Contracts.Common;
using SalesApp.Core.Contracts.Sales;
using SalesApp.Core.Entities;
using SalesApp.Core.Security;
using SalesApp.Core.Tax;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Services;

public interface IPaymentService
{
    Task<PagedResult<PaymentDto>> ListAsync(PaymentListRequest request, CancellationToken cancellationToken);
    Task<PaymentDto> CreateAsync(PaymentCreateRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Customer payments against invoices. Recording one updates the invoice's paid amount and status in a
/// single guarded UPDATE, so two people recording payments at once can never overpay an invoice.
/// </summary>
public sealed class PaymentService(
    AppDbContext db,
    IBusinessClock clock,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IPaymentService
{
    /// <summary>Income-tax Act s.269ST: no cash receipt of ₹2,00,000 or more from one person in a day.</summary>
    public const decimal CashLimitPerDay = 200_000m;

    public async Task<PagedResult<PaymentDto>> ListAsync(PaymentListRequest request, CancellationToken cancellationToken)
    {
        var query = db.Payments.AsNoTracking();

        if (request.Mode is { } mode)
        {
            query = query.Where(p => p.Mode == mode);
        }

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(p => p.Invoice!.CustomerId == customerId);
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
            query = query.Where(p => p.Invoice!.Number.Contains(q) || p.Invoice.CustomerName.Contains(q) ||
                                     (p.Reference != null && p.Reference.Contains(q)));
        }

        return await query
            .OrderByDescending(p => p.PaymentDate)
            .ThenByDescending(p => p.Id)
            .Select(p => new PaymentDto(p.Id, p.InvoiceId, p.Invoice!.Number, p.Invoice.CustomerId, p.Invoice.CustomerName,
                p.PaymentDate, p.Amount, p.Mode, p.Reference, p.Notes, p.CreatedBy!.FullName, p.CreatedAtUtc))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<PaymentDto> CreateAsync(PaymentCreateRequest request, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.AsNoTracking()
            .Where(i => i.Id == request.InvoiceId)
            .Select(i => new { i.Id, i.Number, i.Status, i.InvoiceDate, i.CustomerId, i.CustomerName, Balance = i.GrandTotal - i.AmountPaid })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException("invoiceId", "Choose a valid invoice.");

        if (invoice.Status == InvoiceStatus.Cancelled)
        {
            throw new ValidationException("invoiceId", $"{invoice.Number} is cancelled; payments can't be recorded against it.");
        }

        if (invoice.Status == InvoiceStatus.Paid)
        {
            throw new ValidationException("amount", $"{invoice.Number} is already fully paid.");
        }

        var today = clock.Today;
        var paymentDate = request.PaymentDate ?? today;
        if (paymentDate > today)
        {
            throw new ValidationException("paymentDate", "The payment date can't be in the future.");
        }

        if (paymentDate < invoice.InvoiceDate)
        {
            throw new ValidationException("paymentDate", "The payment date can't be before the invoice date.");
        }

        if (request.Amount > invoice.Balance)
        {
            throw new ValidationException("amount", $"Only ₹{AmountInWords.IndianGrouping(invoice.Balance)} is due on {invoice.Number}.");
        }

        if (request.Mode == PaymentMode.Cash)
        {
            var cashToday = await db.Payments
                .Where(p => p.Mode == PaymentMode.Cash && p.PaymentDate == paymentDate && p.Invoice!.CustomerId == invoice.CustomerId)
                .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
            if (cashToday + request.Amount >= CashLimitPerDay)
            {
                throw new ValidationException("amount",
                    $"Cash received from {invoice.CustomerName} on this day would reach ₹{AmountInWords.IndianGrouping(cashToday + request.Amount)}. " +
                    "Cash of ₹2,00,000 or more from one person in a day isn't allowed (Income-tax Act s.269ST). Use a bank transfer or cheque.");
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var amount = new SqlParameter { SqlDbType = SqlDbType.Decimal, Precision = 18, Scale = 2, Value = request.Amount };
        var updated = await db.Database.SqlQuery<decimal>($"""
            UPDATE [dbo].[Invoices]
            SET [AmountPaid] = [AmountPaid] + {amount},
                [Status] = CASE WHEN [AmountPaid] + {amount} >= [GrandTotal] THEN 'Paid' ELSE 'PartiallyPaid' END
            OUTPUT inserted.[AmountPaid] AS [Value]
            WHERE [Id] = {invoice.Id} AND [Status] <> 'Cancelled' AND [AmountPaid] + {amount} <= [GrandTotal]
            """).ToListAsync(cancellationToken);

        if (updated.Count == 0)
        {
            throw new ConflictException($"{invoice.Number} changed while this payment was being recorded (another payment or a cancellation). Reload and try again.");
        }

        var payment = new Payment
        {
            InvoiceId = invoice.Id,
            PaymentDate = paymentDate,
            Amount = request.Amount,
            Mode = request.Mode,
            Reference = Text.NullIfBlank(request.Reference),
            Notes = Text.NullIfBlank(request.Notes),
            CreatedByUserId = currentUser.UserId,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await db.Payments.AsNoTracking()
            .Where(p => p.Id == payment.Id)
            .Select(p => new PaymentDto(p.Id, p.InvoiceId, p.Invoice!.Number, p.Invoice.CustomerId, p.Invoice.CustomerName,
                p.PaymentDate, p.Amount, p.Mode, p.Reference, p.Notes, p.CreatedBy!.FullName, p.CreatedAtUtc))
            .SingleAsync(cancellationToken);
    }
}
