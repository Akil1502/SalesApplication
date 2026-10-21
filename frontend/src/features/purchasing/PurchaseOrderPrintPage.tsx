import { useEffect } from 'react'
import { useParams } from 'react-router'
import { Skeleton } from '@/components/ui/skeleton'
import { dateLanguage, docText, useDocumentLanguage } from '@/features/sales/print/document-language'
import { DocAmountInWords, DocField, DocText, DocumentLanguageScope } from '@/features/sales/print/DocText'
import { PrintLogo, PrintShell, cell, headCell, numCell } from '@/features/sales/print/PrintShell'
import { errorMessage } from '@/lib/api/errors'
import { formatAmount, formatBusinessDate, formatPercent, formatQuantity } from '@/lib/format'
import { t } from '@/lib/i18n'
import { usePurchaseOrder, type PurchaseOrder } from './api'

/** A purchase order laid out for A4 printing, to send to the supplier, in the supplier's language. */
export function PurchaseOrderPrintPage() {
  const id = Number(useParams().id)
  const order = usePurchaseOrder(id)
  const o = order.data

  // The title becomes the default PDF file name (in English, whatever the document's language); restore it when leaving.
  useEffect(() => {
    if (!o) return
    const previous = document.title
    document.title = docText('English', 'Purchase order {number}', { number: o.number })
    return () => {
      document.title = previous
    }
  }, [o])

  if (order.isPending) return <Skeleton className="m-8 h-[60vh]" />
  if (order.error) return <p className="m-8 text-red-700">{errorMessage(order.error)}</p>

  return (
    <PrintShell title={t('Purchase order {number}', { number: order.data.number })} languages={[order.data.documentLanguage]}>
      <DocumentLanguageScope language={order.data.documentLanguage}>
        <PurchaseOrderDocument p={order.data} />
      </DocumentLanguageScope>
    </PrintShell>
  )
}

function PurchaseOrderDocument({ p }: { p: PurchaseOrder }) {
  const dates = dateLanguage(useDocumentLanguage())
  const draft = p.status === 'Draft' || p.status === 'PendingApproval'
  const a = p.supplierAddress

  return (
    <>
      <header className="mb-3 flex items-start justify-between gap-6 border-b-2 border-neutral-800 pb-3">
        <div>
          <PrintLogo />
          <h1 className="text-xl font-bold">{p.companyName}</h1>
          <p className="max-w-[95mm]">{p.companyAddress}</p>
          <p className="mt-1 font-semibold">GSTIN: {p.companyGstin}</p>
        </div>
        <div className="text-right">
          <p className="text-lg font-bold tracking-wide">{draft ? <DocText text="PURCHASE ORDER (DRAFT)" /> : <DocText text="PURCHASE ORDER" />}</p>
          <p>
            <DocField label="No.">{p.number}</DocField>
          </p>
          <p>
            <DocField label="Date">{formatBusinessDate(p.orderDate, dates)}</DocField>
          </p>
          {p.expectedDate && (
            <p>
              <DocField label="Deliver by">{formatBusinessDate(p.expectedDate, dates)}</DocField>
            </p>
          )}
        </div>
      </header>

      <div className="mb-3 border border-neutral-400 p-2">
        <p className="mb-1 font-semibold uppercase">
          <DocText text="To" />
        </p>
        <p className="font-semibold">{p.supplierName}</p>
        {p.supplierContactPerson && (
          <p>
            <DocField label="Attn:">{p.supplierContactPerson}</DocField>
          </p>
        )}
        <p>
          {a.line1}
          {a.line2 && `, ${a.line2}`}, {a.city} - {a.pincode}, {a.stateName}
        </p>
        <p>GSTIN: {p.supplierGstin ?? <DocText text="Unregistered" inline />}</p>
        {p.supplierReference && (
          <p>
            <DocField label="Your ref:">{p.supplierReference}</DocField>
          </p>
        )}
      </div>

      <table className="mb-3 w-full border-collapse">
        <thead>
          <tr>
            <th className={headCell}>#</th>
            <th className={headCell}>
              <DocText text="Description" />
            </th>
            <th className={headCell}>HSN</th>
            <th className={`${headCell} text-right`}>
              <DocText text="Qty" />
            </th>
            <th className={`${headCell} text-right`}>
              <DocText text="Rate" />
            </th>
            <th className={`${headCell} text-right`}>GST</th>
            <th className={`${headCell} text-right`}>
              <DocText text="Taxable" />
            </th>
            <th className={`${headCell} text-right`}>
              <DocText text="Amount" />
            </th>
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
        <div className="max-w-[110mm] space-y-2">
          <DocAmountInWords english={p.amountInWords} tamil={p.amountInWordsTamil} />
        </div>
        <table className="min-w-[65mm] border-collapse">
          <tbody>
            <tr>
              <td className={cell}>
                <DocText text="Taxable value" />
              </td>
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
              <td className={cell}>
                <DocText text="Round off" />
              </td>
              <td className={numCell}>{formatAmount(p.roundOff)}</td>
            </tr>
            <tr>
              <td className={`${cell} text-sm font-bold`}>
                <DocText text="Total (₹)" />
              </td>
              <td className={`${numCell} text-sm font-bold`}>{formatAmount(p.grandTotal)}</td>
            </tr>
          </tbody>
        </table>
      </div>

      {p.notes && <p className="mb-3 whitespace-pre-line">{p.notes}</p>}
      {p.terms && (
        <div className="mb-4">
          <p className="font-semibold">
            <DocText text="Terms" />
          </p>
          <p className="whitespace-pre-line">{p.terms}</p>
        </div>
      )}
      <div className="flex justify-end">
        <div className="text-right">
          <p>
            <DocText text="For {name}" values={{ name: p.companyName }} />
          </p>
          <p className="mt-10 border-t border-neutral-500 pt-1">
            <DocText text="Authorised signatory" />
          </p>
        </div>
      </div>
    </>
  )
}
