/** The rail's full width, margin included: icons only, and open with labels. The page leaves this much room. */
export const RAIL_WIDTH = 72
export const RAIL_OPEN_WIDTH = 280

/** Shared by the rail and the page beside it, so the two move as one. */
export const railSpring = { type: 'spring', stiffness: 380, damping: 36 } as const
