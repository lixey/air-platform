/** What a record says of the elements it does not offer: the key of each with the index of its reason. */
export type Unavailable = Readonly<Record<string, number>>;

/** The reason the first of `records` gives against the element; null while every one offers it. */
export function unavailableReasonOf(
  element: { readonly key?: string | null; readonly unavailableReasons?: readonly string[] | null } | null | undefined,
  records: readonly (Unavailable | null | undefined)[],
): string | null {
  const key = element?.key;
  const reasons = element?.unavailableReasons;
  if (!key || !reasons?.length) {
    return null;
  }

  for (const record of records) {
    const index = record && Object.hasOwn(record, key) ? record[key] : undefined;
    const reason = index === undefined ? undefined : reasons[index];
    if (reason) {
      return reason;
    }
  }

  return null;
}
