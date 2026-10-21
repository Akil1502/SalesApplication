import { useEffect } from 'react'
import { useParams } from 'react-router'
import { Skeleton } from '@/components/ui/skeleton'
import { dateLanguage, docText, useDocumentLanguage } from '@/features/sales/print/document-language'
import { DocAmountInWords, DocField, DocText, DocumentLanguageScope } from '@/features/sales/print/DocText'
import { PrintLogo, PrintShell, cell, headCell, numCell } from '@/features/sales/print/PrintShell'
import { errorMessage } from '@/lib/api/errors'
import { formatAmount, formatBusinessDate, formatPercent, formatQuantity } from '@/lib/format'
import { t } from '@/lib/i18n'
import { useSupplierReturn, type SupplierReturn } from './api'
import { returnReasonLabels } from './labels'

/** Our debit note to the supplier for goods sent back, laid out for A4 printing, in the supplier's language. */
export function DebitNotePrintPage() {
  const ret = useSupplierReturn(Number(useParams().id))

  useEffect(() => {
    if (!ret.data) return
    const previous = document.title
    document.title = docText('English', 'Debit note {number}', { number: ret.data.number })
    return () => {
      document.title = previous
    }
  }, [ret.data])

  if (ret.isPending) return <Skeleton className="m-8 h-[60vh]" />
  if (ret.error) return <p className="m-8 text-red-700">{errorMessage(ret.error)}</p>

  return (
    <PrintShell title={t('Debit note {number}', { number: ret.data.number })} languages={[ret.data.documentLanguage]}>
      <DocumentLanguageScope language={ret.data.documentLanguage}>
        <DebitNoteDocument r={ret.data} />
      </DocumentLanguageScope>
    </PrintShell>
  )
}

function DebitNoteDocument({ r }: { r: SupplierReturn }) {
  const dates = dateLanguage(useDocumentLanguage())
  const inter = r.isInterState

  return (
    <div className="relative">
      {r.status === 'Cancelled' && (
        <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
          <span className="rotate-[-25deg] border-4 border-red-600 px-6 py-2 text-center text-6xl font-bold text-red-600/40">
            <DocText text="CANCELLED" />
          </span>
        </div>
      )}

      <header className="mb-3 flex items-start justify-between gap-6 border-b-2 border-neutral-800 pb-3">
        <div>
          <PrintLogo />
          <h1 className="text-xl font-bold">{r.companyName}</h1>
          <p className="max-w-[95mm]">{r.companyAddress}</p>
          <p className="mt-1 font-semibold">GSTIN: {r.companyGstin}</p>
        </div>
        <div className="text-right">
          <p className="text-lg font-bold tracking-wide">
            <DocText text="DEBIT NOTE" />
          </p>
          <p className="text-neutral-600">
            <DocText text="Goods returned to supplier" />
          </p>
        </div>
      </header>

      <table className="mb-3 w-full border-collapse">
        <tbody>
          <tr>
            <td className={cell}>
              <DocField label="Debit note no.">{r.number}</DocField>
            </td>
            <td className={cell}>
              <DocField label="Date">{formatBusinessDate(r.returnDate, dates)}</DocField>
            </td>
            <td className={cell}>
              <DocField label="Reason">
                <DocText text={returnReasonLabels[r.reason]} inline />
              </DocField>
            </td>
          </tr>
          <tr>
            <td className={cell}>
              <DocField label="Your invoice">{r.supplierInvoiceNumber}</DocField>
            </td>
            <td className={cell}>
              <DocField label="Invoice date">{formatBusinessDate(r.billDate, dates)}</DocField>
            </td>
            <td className={cell}>
              <DocField label="Our order">{r.purchaseOrderNumber}</DocField>
            </td>
          </tr>
          {r.reasonNote && (
            <tr>
              <td className={cell} colSpan={3}>
                <DocField label="Details:">{r.reasonNote}</DocField>
              </td>
            </tr>
          )}
        </tbody>
      </table>

      <div className="mb-3 border border-neutral-400 p-2">
        <p className="mb-1 font-semibold uppercase">
          <DocText text="To" />
        </p>
        <p className="font-semibold">{r.supplierName}</p>
        <p>{r.supplierStateName}</p>
        <p className="mt-1">GSTIN: {r.supplierGstin ?? <DocText text="Unregistered" inline />}</p>
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
              <DocText text="Qty returned" />
            </th>
            <th className={`${headCell} text-right`}>
              <DocText text="Rate" />
            </th>
            <th className={`${headCell} text-right`}>
              <DocText text="Taxable" />
            </th>
            <th className={`${headCell} text-right`}>{inter ? 'IGST' : 'CGST + SGST'}</th>
            <th className={`${headCell} text-right`}>
              <DocText text="Amount" />
            </th>
          </tr>
        </thead>
        <tbody>
          {r.lines.map((l) => (
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
              <td className={numCell}>{formatAmount(l.taxableValue)}</td>
              <td className={numCell}>
                {formatAmount(l.cgstAmount + l.sgstAmount + l.igstAmount)}
                {r.withGst && (
                  <>
                    <br />
                    <span className="text-neutral-500">@{formatPercent(l.gstRate)}</span>
                  </>
                )}
              </td>
              <td className={numCell}>{formatAmount(l.lineTotal)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div className="mb-3 flex items-start justify-between gap-6">
        <div className="max-w-[110mm] space-y-2">
          <DocAmountInWords english={r.amountInWords} tamil={r.amountInWordsTamil} />
          <p className="text-neutral-700">
            <DocText
              text="Please send your credit note for the goods returned against your invoice {number}."
              values={{ number: r.supplierInvoiceNumber }}
            />
          </p>
        </div>
        <table className="min-w-[65mm] border-collapse">
          <tbody>
            <tr>
              <td className={cell}>
                <DocText text="Taxable value" />
              </td>
              <td className={numCell}>{formatAmount(r.taxableValue)}</td>
            </tr>
            {inter ? (
              <tr>
                <td className={cell}>IGST</td>
                <td className={numCell}>{formatAmount(r.igstAmount)}</td>
              </tr>
            ) : (
              <>
                <tr>
                  <td className={cell}>CGST</td>
                  <td className={numCell}>{formatAmount(r.cgstAmount)}</td>
                </tr>
                <tr>
                  <td className={cell}>SGST</td>
                  <td className={numCell}>{formatAmount(r.sgstAmount)}</td>
                </tr>
              </>
            )}
            <tr>
              <td className={cell}>
                <DocText text="Round off" />
              </td>
              <td className={numCell}>{formatAmount(r.roundOff)}</td>
            </tr>
            <tr>
              <td className={`${cell} text-sm font-bold`}>
                <DocText text="Total (₹)" />
              </td>
              <td className={`${numCell} text-sm font-bold`}>{formatAmount(r.grandTotal)}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <footer className="flex justify-end border-t border-neutral-400 pt-3">
        <div className="flex flex-col items-end text-right">
          <p>
            <DocText text="For {name}" values={{ name: r.companyName }} />
          </p>
          <p className="mt-10 border-t border-neutral-500 pt-1">
            <DocText text="Authorised signatory" />
          </p>
        </div>
      </footer>
      <p className="mt-3 text-center text-[10px] text-neutral-500">
        <DocText text="This is a computer-generated debit note." />
      </p>
    </div>
  )
}
