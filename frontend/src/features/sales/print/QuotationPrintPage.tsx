import { useQuery } from '@tanstack/react-query'
import { useEffect } from 'react'
import { useParams } from 'react-router'
import { Skeleton } from '@/components/ui/skeleton'
import { api } from '@/lib/api/client'
import { errorMessage, unwrap } from '@/lib/api/errors'
import { formatAmount, formatBusinessDate, formatPercent, formatQuantity } from '@/lib/format'
import { useOrder } from '../api'
import { PrintLogo, PrintShell, cell, headCell, numCell } from './PrintShell'

/** Quotation (or order confirmation, once confirmed) laid out for A4 printing. */
export function QuotationPrintPage() {
  const id = Number(useParams().id)
  const order = useOrder(id)
  const company = useQuery({ queryKey: ['settings', 'company'], queryFn: () => unwrap(api.GET('/api/settings/company')) })

  const o = order.data
  const isQuote = o?.status === 'Draft' || o?.status === 'Quoted'
  const title = isQuote ? 'QUOTATION' : 'ORDER CONFIRMATION'
  const docNumber = o ? (isQuote ? (o.quotationNumber ?? `${o.number} (draft)`) : o.number) : ''

  // The title becomes the default PDF file name; restore it when leaving the page.
  useEffect(() => {
    if (!o) return
    const previous = document.title
    document.title = `${isQuote ? 'Quotation' : 'Order'} ${docNumber}`
    return () => {
      document.title = previous
    }
  }, [o, isQuote, docNumber])

  if (order.isPending || company.isPending) return <Skeleton className="m-8 h-[60vh]" />
  if (order.error || company.error) return <p className="m-8 text-red-700">{errorMessage(order.error ?? company.error)}</p>

  const c = company.data
  const inter = o!.isInterState
  const sgst = o!.usesUtgst ? 'UTGST' : 'SGST'

  return (
    <PrintShell title={`${isQuote ? 'Quotation' : 'Order'} ${docNumber}`}>
      <header className="mb-3 flex items-start justify-between gap-6 border-b-2 border-neutral-800 pb-3">
        <div>
          <PrintLogo />
          <h1 className="text-xl font-bold">{c.legalName}</h1>
          <p className="max-w-[95mm]">
            {c.addressLine1}
            {c.addressLine2 && `, ${c.addressLine2}`}, {c.city} - {c.pincode}, {c.stateName}
          </p>
          <p className="mt-1 font-semibold">GSTIN: {c.gstin}</p>
        </div>
        <div className="text-right">
          <p className="text-lg font-bold tracking-wide">{title}</p>
          <p>
            <b>No.</b> {docNumber}
          </p>
          <p>
            <b>Date</b> {formatBusinessDate(o!.orderDate)}
          </p>
          {isQuote && o!.validUntil && (
            <p>
              <b>Valid until</b> {formatBusinessDate(o!.validUntil)}
            </p>
          )}
        </div>
      </header>

      <div className="mb-3 border border-neutral-400 p-2">
        <p className="mb-1 font-semibold uppercase">To</p>
        <p className="font-semibold">{o!.customerName}</p>
        <p>GSTIN: {o!.customerGstin ?? 'Unregistered'}</p>
        <p>
          Place of supply: {o!.placeOfSupplyStateName} ({o!.placeOfSupplyStateCode})
          {o!.customerReference && ` · Your ref: ${o!.customerReference}`}
        </p>
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
            <th className={`${headCell} text-right`}>GST</th>
            <th className={`${headCell} text-right`}>Taxable</th>
            <th className={`${headCell} text-right`}>Amount</th>
          </tr>
        </thead>
        <tbody>
          {o!.lines.map((l) => (
            <tr key={l.id}>
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
              <td className={numCell}>{formatPercent(l.gstRate)}</td>
              <td className={numCell}>{formatAmount(l.taxableValue)}</td>
              <td className={numCell}>{formatAmount(l.lineTotal)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div className="mb-4 flex items-start justify-between gap-6">
        <p className="max-w-[110mm]">
          <b>Amount in words:</b> {o!.amountInWords}
          {isQuote && <span className="mt-2 block text-neutral-600">GST is charged at the rate in force on the invoice date.</span>}
        </p>
        <table className="min-w-[65mm] border-collapse">
          <tbody>
            <tr>
              <td className={cell}>Taxable value</td>
              <td className={numCell}>{formatAmount(o!.taxableValue)}</td>
            </tr>
            {inter ? (
              <tr>
                <td className={cell}>IGST</td>
                <td className={numCell}>{formatAmount(o!.igstAmount)}</td>
              </tr>
            ) : (
              <>
                <tr>
                  <td className={cell}>CGST</td>
                  <td className={numCell}>{formatAmount(o!.cgstAmount)}</td>
                </tr>
                <tr>
                  <td className={cell}>{sgst}</td>
                  <td className={numCell}>{formatAmount(o!.sgstAmount)}</td>
                </tr>
              </>
            )}
            <tr>
              <td className={cell}>Round off</td>
              <td className={numCell}>{formatAmount(o!.roundOff)}</td>
            </tr>
            <tr>
              <td className={`${cell} text-sm font-bold`}>Total (₹)</td>
              <td className={`${numCell} text-sm font-bold`}>{formatAmount(o!.grandTotal)}</td>
            </tr>
          </tbody>
        </table>
      </div>

      {o!.notes && <p className="mb-4 whitespace-pre-line">{o!.notes}</p>}
      <div className="flex justify-end">
        <div className="text-right">
          <p>For {c.legalName}</p>
          <p className="mt-10 border-t border-neutral-500 pt-1">Authorised signatory</p>
        </div>
      </div>
    </PrintShell>
  )
}
