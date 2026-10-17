/** An address that leaves the application: anything with a scheme of its own. */
export function isExternalPath(path: string | undefined | null): boolean {
  return !!path && /^[a-z][a-z0-9+.-]*:\/\//i.test(path);
}
