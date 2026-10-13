/**
 * GST arithmetic for live previews on the order form. Mirrors SalesApp.Core.Tax.GstCalculator exactly
 * (same rounding), so what the user sees while typing matches what the server saves. The server is still
 * the authority: invoices are recalculated there at issue.
 */

export interface GstLineInput {
  quantity: number
  unitPrice: number
  discountPercent: number
  gstRate: number
}

export interface GstLineResult {
  grossAmount: number
  discountAmount: number
  taxableValue: number
  cgstAmount: number
  sgstAmount: number
  igstAmount: number
  taxAmount: number
  lineTotal: number
}

export interface GstTotals {
  lines: GstLineResult[]
  taxableValue: number
  cgstAmount: number
  sgstAmount: number
  igstAmount: number
  taxAmount: number
  roundOff: number
  grandTotal: number
}

// Work in integer paise to avoid binary floating-point drift (0.1 + 0.2 !== 0.3).
const toPaise = (rupees: number) => Math.round(rupees * 100)
const fromPaise = (paise: number) => paise / 100

/** Round half away from zero to 2 decimals, like .NET's MidpointRounding.AwayFromZero. */
export function roundMoney(value: number): number {
  // Nudge by a tiny epsilon proportional to the magnitude so values like 1.005 (stored as 1.00499...) round up.
  const scaled = Math.abs(value) * 100
  const rounded = Math.round(scaled + scaled * Number.EPSILON * 4)
  return (Math.sign(value) * rounded) / 100
}

export function calculateLine(line: GstLineInput, isInterState: boolean): GstLineResult {
  const gross = roundMoney(line.quantity * line.unitPrice)
  const discount = roundMoney((gross * line.discountPercent) / 100)
  const taxable = fromPaise(toPaise(gross) - toPaise(discount))

  if (isInterState) {
    const igst = roundMoney((taxable * line.gstRate) / 100)
    return {
      grossAmount: gross,
      discountAmount: discount,
      taxableValue: taxable,
      cgstAmount: 0,
      sgstAmount: 0,
      igstAmount: igst,
      taxAmount: igst,
      lineTotal: fromPaise(toPaise(taxable) + toPaise(igst)),
    }
  }

  // Each half rounded on its own, so CGST always equals SGST.
  const half = roundMoney((taxable * (line.gstRate / 2)) / 100)
  return {
    grossAmount: gross,
    discountAmount: discount,
    taxableValue: taxable,
    cgstAmount: half,
    sgstAmount: half,
    igstAmount: 0,
    taxAmount: fromPaise(toPaise(half) * 2),
    lineTotal: fromPaise(toPaise(taxable) + toPaise(half) * 2),
  }
}

export function calculateTotals(lines: GstLineInput[], isInterState: boolean): GstTotals {
  const results = lines.map((line) => calculateLine(line, isInterState))
  const sum = (pick: (r: GstLineResult) => number) => fromPaise(results.reduce((total, r) => total + toPaise(pick(r)), 0))

  const taxableValue = sum((r) => r.taxableValue)
  const cgstAmount = sum((r) => r.cgstAmount)
  const sgstAmount = sum((r) => r.sgstAmount)
  const igstAmount = sum((r) => r.igstAmount)
  const taxAmount = fromPaise(toPaise(cgstAmount) + toPaise(sgstAmount) + toPaise(igstAmount))
  const exactPaise = toPaise(taxableValue) + toPaise(taxAmount)
  // Nearest rupee, half away from zero.
  const grandPaise = Math.sign(exactPaise) * Math.round(Math.abs(exactPaise) / 100) * 100

  return {
    lines: results,
    taxableValue,
    cgstAmount,
    sgstAmount,
    igstAmount,
    taxAmount,
    roundOff: fromPaise(grandPaise - exactPaise),
    grandTotal: fromPaise(grandPaise),
  }
}
