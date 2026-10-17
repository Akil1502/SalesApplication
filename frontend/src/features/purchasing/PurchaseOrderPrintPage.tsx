import { useEffect } from 'react'
import { useParams } from 'react-router'
import { Skeleton } from '@/components/ui/skeleton'
import { PrintLogo, PrintShell, cell, headCell, numCell } from '@/features/sales/print/PrintShell'
import { errorMessage } from '@/lib/api/errors'
import { formatAmount, formatBusinessDate, formatPercent, formatQuantity } from '@/lib/format'
import { usePurchaseOrder } from './api'

/** A purchase order laid out for A4 printing, to send to the supplier. */
export function PurchaseOrderPrintPage() {
  const id = Number(useParams().id)
  const order = usePurchaseOrder(id)
  const o = order.data
  const draft = o?.status === 'Draft' || o?.status === 'PendingApproval'

  // The title becomes the default PDF file name; restore it when leaving the page.
  useEffect(() => {
    if (!o) return
    const previous = document.title
    document.title = `Purchase order ${o.number}`
    return () => {
      document.title = previous
    }
  }, [o])

  if (order.isPending) return <Skeleton className="m-8 h-[60vh]" />
  if (order.error) return <p className="m-8 text-red-700">{errorMessage(order.error)}</p>

  const p = order.data
  const a = p.supplierAddress

  return (
    <PrintShell title={`Purchase order ${p.number}`}>
      <header className="mb-3 flex items-start justify-between gap-6 border-b-2 border-neutral-800 pb-3">
        <div>
          <PrintLogo />
          <h1 className="text-xl font-bold">{p.companyName}</h1>
          <p className="max-w-[95mm]">{p.companyAddress}</p>
          <p className="mt-1 font-semibold">GSTIN: {p.companyGstin}</p>
        </div>
        <div className="text-right">
          <p className="text-lg font-bold tracking-wide">PURCHASE ORDER{draft && ' (DRAFT)'}</p>
          <p>
            <b>No.</b> {p.number}
          </p>
          <p>
            <b>Date</b> {formatBusinessDate(p.orderDate)}
          </p>
          {p.expectedDate && (
            <p>
              <b>Deliver by</b> {formatBusinessDate(p.expectedDate)}
            </p>
          )}
        </div>
      </header>

      <div className="mb-3 border border-neutral-400 p-2">
        <p className="mb-1 font-semibold uppercase">To</p>
        <p className="font-semibold">{p.supplierName}</p>
        {p.supplierContactPerson && <p>Attn: {p.supplierContactPerson}</p>}
        <p>
          {a.line1}
          {a.line2 && `, ${a.line2}`}, {a.city} - {a.pincode}, {a.stateName}
        </p>
        <p>GSTIN: {p.supplierGstin ?? 'Unregistered'}</p>
        {p.supplierReference && <p>Your ref: {p.supplierReference}</p>}
      </div>

      <table className="mb-3 w-full border-collapse">
        <thead>
          <tr>
            <th className={headCell}>#</th>
            <th className={headCell}>Description</th>
            <th className={headCell}>HSN</th>
            <th className={`${headCell} text-right`}>Qty</th>
            <th className={`${headCell} text-right`}>Rate</th>
            <th className={`${headCell} text-right`}>GST</th>
            <th className={`${headCell} text-right`}>Taxable</th>
            <th className={`${headCell} text-right`}>Amount</th>
          </tr>
        </thead>
        <tbody>
          {p.lines.map((l) => (
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
              <td className={numCell}>{formatPercent(l.gstRate)}</td>
              <td className={numCell}>{formatAmount(l.taxableValue)}</td>
              <td className={numCell}>{formatAmount(l.lineTotal)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div className="mb-4 flex items-start justify-between gap-6">
        <p className="max-w-[110mm]">
          <b>Amount in words:</b> {p.amountInWords}
        </p>
        <table className="min-w-[65mm] border-collapse">
          <tbody>
            <tr>
              <td className={cell}>Taxable value</td>
              <td className={numCell}>{formatAmount(p.taxableValue)}</td>
            </tr>
            {p.isInterState ? (
              <tr>
                <td className={cell}>IGST</td>
                <td className={numCell}>{formatAmount(p.igstAmount)}</td>
              </tr>
            ) : (
              <>
                <tr>
                  <td className={cell}>CGST</td>
                  <td className={numCell}>{formatAmount(p.cgstAmount)}</td>
                </tr>
                <tr>
                  <td className={cell}>SGST</td>
                  <td className={numCell}>{formatAmount(p.sgstAmount)}</td>
                </tr>
              </>
            )}
            <tr>
              <td className={cell}>Round off</td>
              <td className={numCell}>{formatAmount(p.roundOff)}</td>
            </tr>
            <tr>
              <td className={`${cell} text-sm font-bold`}>Total (₹)</td>
              <td className={`${numCell} text-sm font-bold`}>{formatAmount(p.grandTotal)}</td>
            </tr>
          </tbody>
        </table>
      </div>

      {p.notes && <p className="mb-3 whitespace-pre-line">{p.notes}</p>}
      {p.terms && (
        <div className="mb-4">
          <p className="font-semibold">Terms</p>
          <p className="whitespace-pre-line">{p.terms}</p>
        </div>
      )}
      <div className="flex justify-end">
        <div className="text-right">
          <p>For {p.companyName}</p>
          <p className="mt-10 border-t border-neutral-500 pt-1">Authorised signatory</p>
        </div>
      </div>
    </PrintShell>
  )
}
