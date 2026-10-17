import type { GridColumnDto, OverlayConfirmModel } from '../core/generated/contracts';
import type { TextsService } from '../core/texts.service';
import { formatCellValue, referenceOf, type ValueClock } from './cell/grid-cell-format';

/** What the line of a change writes for no value: a dash, as the form's info element writes none. */
const NO_VALUE = '—';

/**
 * The question asked before a cell of a confirmed column is saved: the column's own text, else the catalogue's, the
 * one line `<label>: <before> → <after>` with both values written as the cell draws them, and the catalogue's `Save`
 * and `Cancel`; no colour, so the dialog's default confirming button stands.
 */
export function cellQuestionOf(
  column: GridColumnDto,
  before: unknown,
  after: unknown,
  clock: ValueClock,
  texts: TextsService,
): Omit<OverlayConfirmModel, 'kind'> {
  const label = column.label ?? column.path ?? '';
  const change = `${cellChangeTextOf(before, column, clock, texts)} → ${cellChangeTextOf(after, column, clock, texts)}`;
  return {
    title: column.confirmText || texts.of('CellSaveQuestion'),
    message: `${label}: ${change}`,
    confirmLabel: texts.of('Save'),
    cancelLabel: texts.of('Cancel'),
  };
}

/**
 * A value as the column's cell draws it, for the line of a change: a checkbox by `Yes` and `No`, a select column by
 * the name of its option, the row's reference or the key the editor holds, any other by the cell's format; a dash
 * for nothing and for an empty text.
 */
export function cellChangeTextOf(value: unknown, column: GridColumnDto, clock: ValueClock, texts: TextsService): string {
  if (column.cell.kind === 'GridCellCheckbox') {
    return texts.of(value === true ? 'Yes' : 'No');
  }
  if (value === null || value === undefined || value === '') {
    return NO_VALUE;
  }
  if (column.cell.kind === 'GridCellSelect' && !referenceOf(value)) {
    const option = column.options?.find(candidate => candidate.id === value || String(candidate.id) === String(value));
    if (option) {
      return option.name;
    }
  }
  return formatCellValue(value, column, clock.locale, clock.zone) || NO_VALUE;
}
