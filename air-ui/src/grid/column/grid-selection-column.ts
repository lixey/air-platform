import type { GridColumnDto } from '../../core/generated/contracts';
import { SELECTION_COLUMN_WIDTH } from './grid-column-width';

/** The path of the column the grid prepends when rows can be selected; no server column has it. */
export const SELECTION_PATH = '$selection';

/** The column that selects rows: a checkbox cell, a page-selecting header, and nothing to sort or filter. */
export const SELECTION_COLUMN_DTO: GridColumnDto = {
  path: SELECTION_PATH,
  type: 'checkbox',
  width: SELECTION_COLUMN_WIDTH,
  maxWidth: SELECTION_COLUMN_WIDTH,
  isSortable: false,
  isFilterable: false,
  isResizable: false,
  isSticky: true,
  isStickyRight: false,
  isHidden: false,
  isDisabled: false,
  isRequired: false,
  cell: { kind: 'GridCellCheckbox' },
  header: { kind: 'GridHeaderSelection' },
};
