/** True while the user is typing (a text field, list box or editable area), when letter shortcuts must not fire. */
export function isTyping(event: KeyboardEvent): boolean {
  const target = event.target
  if (!(target instanceof HTMLElement)) return false
  if (target.isContentEditable) return true
  const tag = target.tagName
  if (tag === 'TEXTAREA' || tag === 'SELECT') return true
  if (tag === 'INPUT') {
    const type = (target as HTMLInputElement).type
    return !['button', 'checkbox', 'radio', 'submit', 'reset', 'range', 'color', 'file'].includes(type)
  }
  return target.getAttribute('role') === 'combobox' || target.getAttribute('role') === 'textbox'
}

/** True while a dialog, menu or popover is open: keys belong to it. */
export function overlayOpen(): boolean {
  return !!document.querySelector('[role="dialog"][data-state="open"], [role="alertdialog"][data-state="open"], [role="menu"], [role="listbox"]')
}

/** A plain key press, with no Ctrl, Alt or Cmd: Shift is allowed (it makes "?" from "/"). */
export function plainKey(event: KeyboardEvent): boolean {
  return !event.ctrlKey && !event.metaKey && !event.altKey
}
