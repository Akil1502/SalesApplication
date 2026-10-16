/** What the login page is told: the page that asked for sign-in, and whose session it was if one just ended there. */
export interface SignInState {
  from?: string
  userId?: string
}

/**
 * Where to go after signing in: back to the page that asked for it, unless it was someone else's session that ended
 * there. Home is the dashboard, or the first page the user may open.
 */
export function landingPage(state: unknown, userId: string): string {
  const { from, userId: owner } = (state ?? {}) as SignInState
  return from && (owner === undefined || owner === userId) ? from : '/'
}
