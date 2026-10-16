import { useEffect } from 'react'
import { useParams } from 'react-router'
import { Skeleton } from '@/components/ui/skeleton'
import { cell, headCell, numCell, PrintShell } from '@/features/sales/print/PrintShell'
import { errorMessage } from '@/lib/api/errors'
import { formatAmount, formatBusinessDate, formatPercent, formatQuantity } from '@/lib/format'
import { useCreditNote } from './api'
import { creditNoteKindLabels, dispositionLabels, returnReasonLabels } from './labels'

/** A GST credit note (CGST Act s.34) laid out for A4 printing, like the invoice it corrects. */
export function CreditNotePrintPage() {
  const note = useCreditNote(Number(useParams().id))

  // The title becomes the default PDF file name; restore it when leaving the page.
  useEffect(() => {
    if (!note.data) return
    document.title = `Credit note ${note.data.number}`
    return () => {
      document.title = 'SalesApp'
    }
  }, [note.data])

  if (note.isPending) return <Skeleton className="m-8 h-[60vh]" />
  if (note.error) return <p className="m-8 text-red-700">{errorMessage(note.error)}</p>

  const cn = note.data
  const inter = cn.isInterState
  const sgst = cn.usesUtgst ? 'UTGST' : 'SGST'
  const goods = cn.kind === 'GoodsReturn'
  const a = cn.billingAddress

  return (
    <PrintShell title={`Credit note ${cn.number}`}>
      <div className="relative">
        {cn.status === 'Cancelled' && (
          <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
            <span className="rotate-[-25deg] border-4 border-red-600 px-6 py-2 text-6xl font-bold text-red-600/40">CANCELLED</span>
          </div>
        )}

        <header className="mb-3 flex items-start justify-between gap-6 border-b-2 border-neutral-800 pb-3">
          <div>
            <h1 className="text-xl font-bold">{cn.sellerName}</h1>
            <p className="max-w-[95mm]">{cn.sellerAddress}</p>
            <p className="mt-1 font-semibold">GSTIN: {cn.sellerGstin}</p>
          </div>
          <div className="text-right">
            <p className="text-lg font-bold tracking-wide">CREDIT NOTE</p>
            <p className="text-neutral-600">{cn.withGst ? 'Under section 34, CGST Act' : 'Financial credit note: no GST adjustment'}</p>
          </div>
        </header>

        <table className="mb-3 w-full border-collapse">
          <tbody>
            <tr>
              <td className={cell}>
                <b>Credit note no.</b> {cn.number}
              </td>
              <td className={cell}>
                <b>Date</b> {formatBusinessDate(cn.creditNoteDate)}
              </td>
              <td className={cell}>
                <b>Place of supply</b> {cn.placeOfSupplyStateName} ({cn.placeOfSupplyStateCode})
              </td>
            </tr>
            <tr>
              <td className={cell}>
                <b>Against invoice</b> {cn.invoiceNumber}
              </td>
              <td className={cell}>
                <b>Invoice date</b> {formatBusinessDate(cn.invoiceDate)}
              </td>
              <td className={cell}>
                <b>Reason</b> {creditNoteKindLabels[cn.kind]}: {returnReasonLabels[cn.reason]}
              </td>
            </tr>
            {cn.reasonNote && (
              <tr>
                <td className={cell} colSpan={3}>
                  <b>Details:</b> {cn.reasonNote}
                </td>
              </tr>
            )}
          </tbody>
        </table>

        <div className="mb-3 border border-neutral-400 p-2">
          <p className="mb-1 font-semibold uppercase">Issued to</p>
          <p className="font-semibold">{cn.customerName}</p>
          <p>
            {a.line1}
            {a.line2 && <>, {a.line2}</>}
            <br />
            {a.city} - {a.pincode}, {a.stateName} (State code {a.stateCode})
          </p>
          <p className="mt-1">GSTIN: {cn.customerGstin ?? 'Unregistered'}</p>
        </div>

        <table className="mb-3 w-full border-collapse">
          <thead>
            <tr>
              <th className={headCell}>#</th>
              <th className={headCell}>Description</th>
              <th className={headCell}>HSN</th>
              {goods && <th className={`${headCell} text-right`}>Qty returned</th>}
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
            {cn.lines.map((l) => (
              <tr key={l.lineNumber}>
                <td className={cell}>{l.lineNumber}</td>
                <td className={cell}>
                  {l.productName}
                  <br />
                  <span className="text-neutral-500">
                    {l.sku}
                    {l.disposition && ` · ${dispositionLabels[l.disposition]}`}
                  </span>
                </td>
                <td className={cell}>{l.hsnCode}</td>
                {goods && <td className={numCell}>{l.quantity != null ? formatQuantity(l.quantity, l.unit) : '—'}</td>}
                <td className={numCell}>{formatAmount(l.taxableValue)}</td>
                {inter ? (
                  <td className={numCell}>
                    {formatAmount(l.igstAmount)}
                    {cn.withGst && (
                      <>
                        <br />
                        <span className="text-neutral-500">@{formatPercent(l.gstRate)}</span>
                      </>
                    )}
                  </td>
                ) : (
                  <>
                    <td className={numCell}>
                      {formatAmount(l.cgstAmount)}
                      {cn.withGst && (
                        <>
                          <br />
                          <span className="text-neutral-500">@{formatPercent(l.gstRate / 2)}</span>
                        </>
                      )}
                    </td>
                    <td className={numCell}>
                      {formatAmount(l.sgstAmount)}
                      {cn.withGst && (
                        <>
                          <br />
                          <span className="text-neutral-500">@{formatPercent(l.gstRate / 2)}</span>
                        </>
                      )}
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
              <b>Amount in words:</b> {cn.amountInWords}
            </p>
            {!cn.withGst && (
              <p className="text-neutral-700">
                Issued after 30 November following the financial year of the invoice, so no GST is reduced: the value only is credited.
              </p>
            )}
          </div>

          <table className="min-w-[65mm] border-collapse">
            <tbody>
              <tr>
                <td className={cell}>Taxable value</td>
                <td className={numCell}>{formatAmount(cn.taxableValue)}</td>
              </tr>
              {inter ? (
                <tr>
                  <td className={cell}>IGST</td>
                  <td className={numCell}>{formatAmount(cn.igstAmount)}</td>
                </tr>
              ) : (
                <>
                  <tr>
                    <td className={cell}>CGST</td>
                    <td className={numCell}>{formatAmount(cn.cgstAmount)}</td>
                  </tr>
                  <tr>
                    <td className={cell}>{sgst}</td>
                    <td className={numCell}>{formatAmount(cn.sgstAmount)}</td>
                  </tr>
                </>
              )}
              <tr>
                <td className={cell}>Round off</td>
                <td className={numCell}>{formatAmount(cn.roundOff)}</td>
              </tr>
              <tr>
                <td className={`${cell} text-sm font-bold`}>Total credit (₹)</td>
                <td className={`${numCell} text-sm font-bold`}>{formatAmount(cn.grandTotal)}</td>
              </tr>
            </tbody>
          </table>
        </div>

        <footer className="flex justify-end border-t border-neutral-400 pt-3">
          <div className="flex flex-col items-end text-right">
            <p>For {cn.sellerName}</p>
            <p className="mt-10 border-t border-neutral-500 pt-1">Authorised signatory</p>
          </div>
        </footer>
        <p className="mt-3 text-center text-[10px] text-neutral-500">This is a computer-generated credit note.</p>
      </div>
    </PrintShell>
  )
}
