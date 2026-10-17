import type { GridColumnDto, OptionDto } from '../../core/generated/contracts';
import type { SelectSource } from '../../select/select-source';
import { columnLabelOf } from './grid-column-label';

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
    };
  }
  if (column.options) {
    return { label, options: column.options.map(option => ({ id: option.id, label: option.name })) };
  }
  return null;
}

/** The option of a select column a key names; none for a column without options or a key no option names. */
export function optionOf(column: GridColumnDto, id: number): OptionDto | null {
  return column.options?.find(option => option.id === id) ?? null;
}
