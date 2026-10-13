import { describe, expect, it } from 'vitest'
import { gstinCheckCharacter, gstinPan, gstinStateCode, normalizeGstin, validateGstin } from './gstin'

describe('validateGstin', () => {
  it.each(['27AAPFU0939F1ZV', '29AAGCB7383J1Z4', '33ABCDE1234F1Z7'])('accepts %s', (gstin) => {
    expect(validateGstin(gstin)).toBeNull()
  })

  it('normalises case and spaces', () => {
    expect(normalizeGstin(' 33abcde 1234f1z7 ')).toBe('33ABCDE1234F1Z7')
    expect(validateGstin(' 33abcde 1234f1z7 ')).toBeNull()
  })

  it('catches typos through the check character', () => {
    expect(validateGstin('33ABCDE1243F1Z7')).toMatch(/check character/)
  })

  it('explains length and format problems', () => {
    expect(validateGstin('33ABCDE1234F1Z')).toMatch(/exactly 15/)
    expect(validateGstin('33ABCDE1234F1A7')).toMatch(/format/)
  })

  it('agrees with the server on the check character', () => {
    expect(gstinCheckCharacter('27AAPFU0939F1Z')).toBe('V')
  })

  it('extracts the state code and PAN', () => {
    expect(gstinStateCode('33ABCDE1234F1Z7')).toBe('33')
    expect(gstinPan('33ABCDE1234F1Z7')).toBe('ABCDE1234F')
  })
})
