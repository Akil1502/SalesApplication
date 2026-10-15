/**
 * The same rules as the server's Permissions class, driven by the needs it sends with the permission list: granting
 * a permission grants what it needs (Add & change needs View), and taking one away takes away what depends on it.
 * The server applies them again on save; these keep the grid honest while the user clicks.
 */
export type Needs = Record<string, string[]>

/** The keys plus everything they need. */
export function withNeeds(keys: Iterable<string>, needs: Needs): Set<string> {
  const result = new Set<string>()
  const pending = [...keys]
  while (pending.length > 0) {
    const key = pending.pop()!
    if (result.has(key)) continue
    result.add(key)
    pending.push(...(needs[key] ?? []))
  }
  return result
}

/** The keys without any whose needs are missing. */
export function withoutUnmetNeeds(keys: Iterable<string>, needs: Needs): Set<string> {
  const result = new Set(keys)
  let removed = true
  while (removed) {
    removed = false
    for (const key of result) {
      if ((needs[key] ?? []).some((needed) => !result.has(needed))) {
        result.delete(key)
        removed = true
      }
    }
  }
  return result
}

/** A user's permissions: their role's, plus the added ones, minus the removed ones. */
export function effective(role: Iterable<string>, added: Iterable<string>, removed: Iterable<string>, needs: Needs): Set<string> {
  const result = withNeeds([...role, ...added], needs)
  for (const key of removed) result.delete(key)
  return withoutUnmetNeeds(result, needs)
}

/** Ticks or unticks one permission, together with what it needs or what depends on it. */
export function toggle(current: ReadonlySet<string>, key: string, on: boolean, needs: Needs): Set<string> {
  if (on) return withNeeds([...current, key], needs)
  const next = new Set(current)
  next.delete(key)
  return withoutUnmetNeeds(next, needs)
}

/** How a user's permissions differ from their role's, as the API stores them. */
export function changesFrom(role: ReadonlySet<string>, granted: ReadonlySet<string>) {
  return {
    addedPermissions: [...granted].filter((p) => !role.has(p)).sort(),
    removedPermissions: [...role].filter((p) => !granted.has(p)).sort(),
  }
}
