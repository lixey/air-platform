import type { UiFieldType } from '../../core/generated/contracts';

/**
 * A typed value for the request from the text of the header's box: a number for a number column, the text otherwise. A
 * checkbox column types nothing: its header cycles yes and no, and the default header's box refuses typing on it.
 */
export function typedFilterValue(type: UiFieldType, raw: string): unknown {
  const text = raw.trim();
  switch (type) {
    case 'number': {
      const n = Number(text.replace(',', '.'));
      return text === '' || Number.isNaN(n) ? null : n;
    }
    default:
      return text === '' ? null : text;
  }
}

/**
 * The value a clause of a filter overlay posts from what its control wrote: a number for a number column, a boolean for
 * a checkbox, the wire string of a date, the text trimmed otherwise; `null` when the control holds nothing.
 */
export function filterValueOf(type: UiFieldType, value: unknown): unknown {
  switch (type) {
    case 'number':
      return typeof value === 'number' && Number.isFinite(value) ? value : null;
    case 'checkbox':
      return typeof value === 'boolean' ? value : null;
    default: {
      if (value === null || value === undefined) {
        return null;
      }
      const text = typeof value === 'string' ? value.trim() : String(value);
      return text === '' ? null : text;
    }
  }
}
