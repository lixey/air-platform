/** The pages a return address never reopens: after signing in or changing the password the user moves on. */
const AUTH_PAGE = /^\/(?:login|change-password)(?:[/?#;]|$)/;

/**
 * The address the sign-in and change-password pages go to from their `returnUrl`: a path of this application,
 * never another origin (`//host`, `https:`), never an address a browser could read as one, and never an
 * authentication page; `/` otherwise.
 */
export function returnUrlOf(value: string | null | undefined): string {
  if (!value?.startsWith('/') || value.startsWith('//') || [...value].some(isUnsafe)) {
    return '/';
  }
  return AUTH_PAGE.test(value) ? '/' : value;
}

/** A backslash or a control character: a browser may read the first as a slash and drops the second. */
function isUnsafe(char: string): boolean {
  return char === '\\' || char < ' ' || char === '\u007f';
}
