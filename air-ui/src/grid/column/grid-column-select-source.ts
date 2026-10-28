import type { GridColumnDto, OptionDto } from '../../core/generated/contracts';
import { labelPathsOf } from '../../select/select-option';
import type { SelectSource } from '../../select/select-source';
import type { GridRow } from '../grid-row';
import { columnLabelOf } from './grid-column-label';

/** The label of the synthetic empty filter option (`id: 0`) for non-required reference/select columns. */
export const EMPTY_SELECT_LABEL = '—';

/**
 * What the select of a column's filter picks from, named by the column's label: the rows of the reference service a
 * reference column names, or the options a select column carries, in the order given and without a request. None for
 * a column with neither: its header opens nothing and the Filters drawer holds no multiselect for it.
 */
export function selectSourceOf(column: GridColumnDto): SelectSource | null {
  const label = columnLabelOf(column);
  if (column.referenceService) {
    return {
      label,
      service: column.referenceService,
      labelPaths: column.referenceLabelPaths,
      referenceFilter: column.referenceFilter,
      ...(!column.isRequired ? { allowEmpty: true } : {}),
    };
  }
  if (column.options) {
    const options = column.options.map(option => ({
      id: option.id,
      label: option.name,
      ...(option.title ? { title: option.title } : {}),
    }));
    return {
      label,
      options: !column.isRequired && !options.some(o => o.id === 0) ? [{ id: 0, label: EMPTY_SELECT_LABEL }, ...options] : options,
    };
  }
  return null;
}

/** The option of a select column a key names; none for a column without options or a key no option names. */
export function optionOf(column: GridColumnDto, id: number): OptionDto | null {
  return column.options?.find(option => option.id === id) ?? null;
}

/** Synthetic top row (`id: 0`, `'—'`) for a select over a non-required reference column on page 1 without search. */
export function emptySelectRowOf(labelPaths: readonly string[] | null | undefined, columns: readonly GridColumnDto[]): GridRow {
  const row: Record<string, unknown> = { id: 0, name: EMPTY_SELECT_LABEL };
  const paths = new Set<string>([labelPathsOf(labelPaths)[0] ?? 'name']);
  const firstColPath = columns.find(c => c.path && c.path !== 'id')?.path;
  if (firstColPath) {
    paths.add(firstColPath);
  }
  for (const path of paths) {
    const segments = path.split('.').filter(Boolean);
    if (segments.length === 1) {
      row[segments[0]] = EMPTY_SELECT_LABEL;
    } else if (segments.length > 1) {
      let current = row;
      for (let i = 0; i < segments.length - 1; i++) {
        const key = segments[i];
        const next = current[key];
        if (!next || typeof next !== 'object') {
          current[key] = {};
        }
        current = current[key] as Record<string, unknown>;
      }
      current[segments[segments.length - 1]] = EMPTY_SELECT_LABEL;
    }
  }
  return row;
}
