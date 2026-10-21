import { Fragment, type ReactNode } from 'react'
import { t } from './index'

/**
 * Translated text with parts that aren't plain text, such as a link or a key cap:
 * `<T text="Press {key} to search" values={{ key: <kbd>/</kbd> }} />`. Tamil can move {key} anywhere in the sentence.
 */
export function T({ text, values }: { text: string; values: Record<string, ReactNode> }) {
  return <>{fillNodes(t(text), values)}</>
}

function fillNodes(text: string, values: Record<string, ReactNode>): ReactNode[] {
  return text.split(/(\{\w+\})/).map((part, i) => {
    const name = /^\{(\w+)\}$/.exec(part)?.[1]
    return <Fragment key={i}>{name !== undefined && name in values ? values[name] : part}</Fragment>
  })
}
