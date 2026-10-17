import type { GridColumnDto, UiTemporalType } from '../../core/generated/contracts';

/**
 * How a column writes a date on the wire: its member's temporal type, and for a column without one (a value the
 * server computes), a calendar date for a `date` column and a local date and time for a `datetime` one.
 */
export function temporalTypeOf(column: Pick<GridColumnDto, 'type' | 'temporalType'>): UiTemporalType {
  return column.temporalType ?? (column.type === 'date' ? 'dateOnly' : 'dateTime');
}
