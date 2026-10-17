import type { SelectOption } from '../../select/select-option';
import type { SelectSource } from '../../select/select-source';

/**
 * The library's select over a fixed list of values: the source it opens with — its label the combobox's name, an
 * option per value by its index — the values the options stand for, and each option's pick as one list, so a
 * template binds the same list while the value holds.
 */
export interface FixedSelect<T> {
  readonly source: SelectSource;
  readonly values: readonly T[];
  readonly picks: readonly (readonly SelectOption[])[];
}

const NOTHING_PICKED: readonly SelectOption[] = [];

/** A select named `label` over the values, each shown by its text. */
export function fixedSelectOf<T>(label: string, entries: readonly (readonly [value: T, text: string])[]): FixedSelect<T> {
  const options = entries.map(([, text], id): SelectOption => ({ id, label: text }));
  return { source: { label, options }, values: entries.map(([value]) => value), picks: options.map(option => [option]) };
}

/** What the select shows picked for the value: its option, or nothing when the list lacks it. */
export function pickOf<T>(select: FixedSelect<T>, value: T): readonly SelectOption[] {
  const index = select.values.indexOf(value);
  return index < 0 ? NOTHING_PICKED : select.picks[index];
}

/** The value a pick stands for; `undefined` for no pick. */
export function valueOfPick<T>(select: FixedSelect<T>, picked: readonly SelectOption[]): T | undefined {
  const option = picked[0];
  return option ? select.values[option.id] : undefined;
}
