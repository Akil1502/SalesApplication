import { useEffect } from 'react'
import { useParams } from 'react-router'
import { Skeleton } from '@/components/ui/skeleton'
import { errorMessage } from '@/lib/api/errors'
import { formatAmount, formatBusinessDate, formatPercent, formatQuantity } from '@/lib/format'
import { useInvoice, type Invoice } from '../api'
import { PrintLogo, PrintShell, cell, headCell, numCell } from './PrintShell'

function Address({ a }: { a: Invoice['billingAddress'] }) {
  return (
    <>
      {a.line1}
      {a.line2 && <>, {a.line2}</>}
      <br />
      {a.city} - {a.pincode}, {a.stateName} (State code {a.stateCode})
    </>
  )
}

/** A GST tax invoice laid out for A4 printing. */
export function InvoicePrintPage() {
  const id = Number(useParams().id)
  const invoice = useInvoice(id)

  // The title becomes the default PDF file name; restore it when leaving the page.
  useEffect(() => {
    if (!invoice.data) return
    const previous = document.title
    document.title = `Invoice ${invoice.data.number}`
    return () => {
      document.title = previous
    }
  }, [invoice.data])

  if (invoice.isPending) return <Skeleton className="m-8 h-[60vh]" />
  if (invoice.error) return <p className="m-8 text-red-700">{errorMessage(invoice.error)}</p>

  return (
    <PrintShell title={`Invoice ${invoice.data.number}`}>
      <InvoiceDocument inv={invoice.data} />
    </PrintShell>
  )
}

/** One invoice's printed pages, for printing alone or with others. */
export function InvoiceDocument({ inv }: { inv: Invoice }) {
  const inter = inv.isInterState
  const sgst = inv.usesUtgst ? 'UTGST' : 'SGST'

  return (
    <div className="relative">
      {inv.status === 'Cancelled' && (
        <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
          <span className="rotate-[-25deg] border-4 border-red-600 px-6 py-2 text-6xl font-bold text-red-600/40">CANCELLED</span>
        </div>
      )}

      <header className="mb-3 flex items-start justify-between gap-6 border-b-2 border-neutral-800 pb-3">
        <div>
          <PrintLogo />
          <h1 className="text-xl font-bold">{inv.sellerName}</h1>
          <p className="max-w-[95mm]">{inv.sellerAddress}</p>
          <p className="mt-1 font-semibold">GSTIN: {inv.sellerGstin}</p>
        </div>
        <div className="text-right">
          <p className="text-lg font-bold tracking-wide">TAX INVOICE</p>
          <p className="text-neutral-600">Original for recipient</p>
        </div>
      </header>

      <table className="mb-3 w-full border-collapse">
        <tbody>
          <tr>
            <td className={cell}>
              <b>Invoice no.</b> {inv.number}
            </td>
            <td className={cell}>
              <b>Date</b> {formatBusinessDate(inv.invoiceDate)}
            </td>
            <td className={cell}>
              <b>Due</b> {formatBusinessDate(inv.dueDate)}
            </td>
          </tr>
          <tr>
            <td className={cell}>
              <b>Place of supply</b> {inv.placeOfSupplyStateName} ({inv.placeOfSupplyStateCode})
            </td>
            <td className={cell}>
              <b>Order</b> {inv.salesOrderNumber}
            </td>
            <td className={cell}>
              <b>Customer PO</b> {inv.customerReference ?? '—'}
            </td>
          </tr>
          <tr>
            <td className={cell} colSpan={3}>
              <b>Reverse charge:</b> No
            </td>
          </tr>
        </tbody>
      </table>

      <div className="mb-3 grid grid-cols-2 gap-3">
        <div className="border border-neutral-400 p-2">
          <p className="mb-1 font-semibold uppercase">Bill to</p>
          <p className="font-semibold">{inv.customerName}</p>
          <p>
            <Address a={inv.billingAddress} />
          </p>
          <p className="mt-1">GSTIN: {inv.customerGstin ?? 'Unregistered'}</p>
        </div>
        <div className="border border-neutral-400 p-2">
          <p className="mb-1 font-semibold uppercase">Ship to</p>
          <p className="font-semibold">{inv.customerName}</p>
          <p>
            <Address a={inv.shippingAddress ?? inv.billingAddress} />
          </p>
        </div>
      </div>

      <table className="mb-3 w-full border-collapse">
        <thead>
          <tr>
            <th className={headCell}>#</th>
            <th className={headCell}>Description</th>
            <th className={headCell}>HSN</th>
            <th className={`${headCell} text-right`}>Qty</th>
            <th className={`${headCell} text-right`}>Rate</th>
            <th className={`${headCell} text-right`}>Disc.</th>
            <th className={`${headCell} text-right`}>Taxable</th>
            {inter ? (
              <th className={`${headCell} text-right`}>IGST</th>
            ) : (
              <>
                <th className={`${headCell} text-right`}>CGST</th>
                <th className={`${headCell} text-right`}>{sgst}</th>
              </>
            )}
            <th className={`${headCell} text-right`}>Amount</th>
          </tr>
        </thead>
        <tbody>
          {inv.lines.map((l) => (
            <tr key={l.lineNumber}>
              <td className={cell}>{l.lineNumber}</td>
              <td className={cell}>
                {l.productName}
                <br />
                <span className="text-neutral-500">{l.sku}</span>
              </td>
              <td className={cell}>{l.hsnCode}</td>
              <td className={numCell}>{formatQuantity(l.quantity, l.unit)}</td>
              <td className={numCell}>{formatAmount(l.unitPrice)}</td>
              <td className={numCell}>{l.discountPercent ? formatPercent(l.discountPercent) : '—'}</td>
              <td className={numCell}>{formatAmount(l.taxableValue)}</td>
              {inter ? (
                <td className={numCell}>
                  {formatAmount(l.igstAmount)}
                  <br />
                  <span className="text-neutral-500">@{formatPercent(l.igstRate)}</span>
                </td>
              ) : (
                <>
                  <td className={numCell}>
                    {formatAmount(l.cgstAmount)}
                    <br />
                    <span className="text-neutral-500">@{formatPercent(l.cgstRate)}</span>
                  </td>
                  <td className={numCell}>
                    {formatAmount(l.sgstAmount)}
                    <br />
                    <span className="text-neutral-500">@{formatPercent(l.sgstRate)}</span>
                  </td>
                </>
              )}
              <td className={numCell}>{formatAmount(l.lineTotal)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div className="mb-3 flex items-start justify-between gap-6">
        <div className="max-w-[110mm] space-y-2">
          <p>
            <b>Amount in words:</b> {inv.amountInWords}
          </p>
          {/* HSN-wise summary: required on B2B tax invoices. */}
          <table className="w-full border-collapse">
            <thead>
              <tr>
                <th className={headCell}>HSN</th>
                <th className={`${headCell} text-right`}>Rate</th>
                <th className={`${headCell} text-right`}>Taxable</th>
                {inter ? (
                  <th className={`${headCell} text-right`}>IGST</th>
                ) : (
                  <>
                    <th className={`${headCell} text-right`}>CGST</th>
                    <th className={`${headCell} text-right`}>{sgst}</th>
                  </>
                )}
              </tr>
            </thead>
            <tbody>
              {inv.hsnSummary.map((h) => (
                <tr key={`${h.hsnCode}-${h.gstRate}`}>
                  <td className={cell}>{h.hsnCode}</td>
                  <td className={numCell}>{formatPercent(h.gstRate)}</td>
                  <td className={numCell}>{formatAmount(h.taxableValue)}</td>
                  {inter ? (
                    <td className={numCell}>{formatAmount(h.igstAmount)}</td>
                  ) : (
                    <>
                      <td className={numCell}>{formatAmount(h.cgstAmount)}</td>
                      <td className={numCell}>{formatAmount(h.sgstAmount)}</td>
                    </>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <table className="min-w-[65mm] border-collapse">
          <tbody>
            <tr>
              <td className={cell}>Taxable value</td>
              <td className={numCell}>{formatAmount(inv.taxableValue)}</td>
            </tr>
            {inter ? (
              <tr>
                <td className={cell}>IGST</td>
                <td className={numCell}>{formatAmount(inv.igstAmount)}</td>
              </tr>
            ) : (
              <>
                <tr>
                  <td className={cell}>CGST</td>
                  <td className={numCell}>{formatAmount(inv.cgstAmount)}</td>
                </tr>
                <tr>
                  <td className={cell}>{sgst}</td>
                  <td className={numCell}>{formatAmount(inv.sgstAmount)}</td>
                </tr>
              </>
            )}
            <tr>
              <td className={cell}>Round off</td>
              <td className={numCell}>{formatAmount(inv.roundOff)}</td>
            </tr>
            <tr>
              <td className={`${cell} text-sm font-bold`}>Total (₹)</td>
              <td className={`${numCell} text-sm font-bold`}>{formatAmount(inv.grandTotal)}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <footer className="grid grid-cols-2 gap-6 border-t border-neutral-400 pt-3">
        <div className="space-y-2">
          {inv.bankName && (
            <p>
              <b>Bank:</b> {inv.bankName}
              {inv.bankBranch && `, ${inv.bankBranch}`}
              <br />
              <b>A/c:</b> {inv.bankAccountNumber} · <b>IFSC:</b> {inv.bankIfsc}
            </p>
          )}
          {inv.terms && (
            <p className="whitespace-pre-line text-neutral-700">
              <b>Terms:</b> {inv.terms}
            </p>
          )}
        </div>
        <div className="flex flex-col items-end justify-between text-right">
          <p>For {inv.sellerName}</p>
          <p className="mt-10 border-t border-neutral-500 pt-1">Authorised signatory</p>
        </div>
      </footer>
      <p className="mt-3 text-center text-[10px] text-neutral-500">This is a computer-generated invoice.</p>
    </div>
  )
}
