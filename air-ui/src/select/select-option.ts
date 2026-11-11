import type { PickedValue, Reference, RowDto, UiIcon } from '../core/generated/contracts';
import { isRecord, valueAt } from '../core/json';
import { isRowDto } from '../grid/grid-row';

/** One thing a select offers or holds: the id the filter uses (`null` for the empty option), the label the user sees, and the row it came from. */
export interface SelectOption extends PickedValue {
  readonly id: number | null;
  readonly label: string;
  readonly subtitle?: string;
  readonly icon?: UiIcon;
  readonly title?: string;
  readonly row?: unknown;
}

type RowInput = RowDto | Readonly<Record<string, unknown>>;

/** The label paths a reference source is asked for when the column or element names none. */
export const DEFAULT_LABEL_PATHS: readonly string[] = ['name'];

/** The label paths a reference names, or the default ones when it names none: no list and an empty one alike. */
export function labelPathsOf(labelPaths: readonly string[] | null | undefined): readonly string[] {
  return labelPaths && labelPaths.length > 0 ? labelPaths : DEFAULT_LABEL_PATHS;
}

/** The label of a row for a select: the scalar label paths' values joined by a space, or `name`, `label` or the id. */
export function optionLabelOf(row: RowInput, labelPaths: readonly string[] | null | undefined): string {
  const cells = isRowDto(row) ? row.cells : row;
  const parts = (labelPaths ?? DEFAULT_LABEL_PATHS)
    .map(path => valueAt(cells, path))
    .filter(value => value !== null && value !== undefined && value !== '' && !isRecord(value))
    .map(value => String(value));
  if (parts.length > 0) {
    return parts.join(' ');
  }
  const fallback = valueAt(cells, 'name') ?? valueAt(cells, 'label') ?? (isRowDto(row) ? row.id : valueAt(cells, 'id'));
  return fallback === null || fallback === undefined || isRecord(fallback) ? '' : String(fallback);
}

/** A row as an option: the id (`null` preserved for the synthetic empty row), the label from the label paths, the row kept beside. */
export function optionOfRow(row: RowInput, labelPaths: readonly string[] | null | undefined): SelectOption {
  const rawId = isRowDto(row) ? row.id : valueAt(row, 'id');
  const numId = rawId === null || rawId === undefined || rawId === '' ? null : typeof rawId === 'number' ? rawId : Number(rawId);
  return {
    id: numId !== null && Number.isFinite(numId) ? numId : null,
    label: optionLabelOf(row, labelPaths),
    row,
  };
}

/** A reference the server sent as the option that shows it. */
export function optionOfReference(reference: Reference & { readonly title?: string | null }): SelectOption {
  return { id: reference.id, label: reference.name, ...(reference.title ? { title: reference.title } : {}) };
}

/** The option as the reference a form value carries. */
export function referenceOfOption(option: SelectOption & { readonly id: number }): Reference {
  return { id: option.id, name: option.label };
}

/** A picked value as the option that shows it: the inverse of `pickedValueOf`, without a row (`null` preserved). */
export function optionOfPickedValue(value: PickedValue): SelectOption {
  const rawId = value.id;
  const numId = rawId === null || rawId === undefined || rawId === '' ? null : typeof rawId === 'number' ? rawId : Number(rawId);
  return { id: numId !== null && Number.isFinite(numId) ? numId : null, label: value.label ?? '' };
}

/** The option as it travels in a request: the id and the label, never the row. */
export function pickedValueOf(option: SelectOption): PickedValue {
  return { id: option.id, label: option.label };
}

/** Whether the option is among the picked, by id. */
export function isPicked(option: SelectOption, picked: readonly SelectOption[]): boolean {
  return picked.some(p => p.id === option.id);
}
