import type { FormElementDto, UiFieldType } from '../core/generated/contracts';
import { instantOf, type WallClock, wallOf } from '../core/time-zone';

/** The field types a temporal control edits: a calendar date, a date and time, a time of day. */
export type TemporalKind = Extract<UiFieldType, 'date' | 'datetime' | 'time'>;

const ZONE = /(Z|[+-]\d{2}:\d{2})$/i;
/** The length of `yyyy-MM-dd`; the `T` of a date-time text follows it. */
const DATE_LENGTH = 10;
/** The length of `yyyy-MM`. */
const MONTH_LENGTH = 7;
/** The length of `yyyy`. */
const YEAR_LENGTH = 4;
/** The length of `HH:mm`. */
const TIME_LENGTH = 5;

/**
 * The editor text of a wire value: `yyyy-MM-dd`, `yyyy-MM-ddTHH:mm` with seconds and at most milliseconds when the
 * value holds them, an instant on the clock of `zone` (the user's, `LocaleService.zone`), and `HH:mm` for a time of
 * day; the untouched wire value stays in the form service.
 */
export function temporalText(value: unknown, element: Pick<FormElementDto, 'type' | 'temporalType'>, zone: string): string {
  if (typeof value !== 'string' || value.startsWith('0001-01-01')) {
    return '';
  }
  if (element.type === 'time') {
    return timePartOf(value);
  }
  if (element.temporalType === 'dateOnly') {
    return element.type === 'date' ? datePartOf(value) : `${datePartOf(value)}T00:00`;
  }
  const instant = hasZone(value) ? new Date(value) : null;
  const local = instant && Number.isFinite(instant.getTime()) ? wallText(wallOf(instant, zone)) : value;
  return element.type === 'date' ? datePartOf(local) : normalizedInput(local.replace(/(\.\d{3})\d+/, '$1'));
}

/**
 * The wire value of an editor text: the current value when the text is the one it shows, so untouched precision
 * survives; otherwise the text converted, a time of day as `HH:mm:00`, an instant typed as a wall clock of `zone` (a
 * time the clock skips moves forward by the gap, one it repeats is the earlier instant); `null` for no text.
 */
export function temporalValue(
  text: string,
  current: unknown,
  element: Pick<FormElementDto, 'type' | 'temporalType'>,
  zone: string,
): string | null {
  if (!text) {
    return null;
  }
  if (typeof current === 'string' && normalizedInput(text) === normalizedInput(temporalText(current, element, zone))) {
    return current;
  }
  if (element.type === 'time') {
    return `${timePartOf(text)}:00`;
  }
  if (element.temporalType === 'dateOnly') {
    return datePartOf(text);
  }
  const hasInstant = element.temporalType === 'dateTimeOffset' || (typeof current === 'string' && hasZone(current));
  if (element.type === 'date') {
    if (hasInstant) {
      const previous = typeof current === 'string' ? new Date(current) : null;
      const time = previous && Number.isFinite(previous.getTime()) ? wallOf(previous, zone) : MIDNIGHT;
      const extra = typeof current === 'string' ? (current.match(/\.(\d+)/)?.[1].slice(3) ?? '') : '';
      return instantOf({ ...time, ...dateOf(text) }, zone)
        .toISOString()
        .replace(/Z$/, `${extra}Z`);
    }
    if (element.temporalType === 'dateTime') {
      const time = typeof current === 'string' ? current.split('T')[1] : null;
      return `${text}T${time || '00:00:00'}`;
    }
    return text;
  }
  const wallTime = text.length === 16 ? `${text}:00` : text;
  return hasInstant ? instantOf(wallOfText(wallTime), zone).toISOString() : wallTime;
}

/** Whether a text ends with `Z` or an offset `±HH:mm`: an instant, not a wall clock. */
export function hasZone(text: string): boolean {
  return ZONE.test(text);
}

/** The `yyyy-MM-dd` a date or a date-time text starts with. */
export function datePartOf(text: string): string {
  return text.slice(0, DATE_LENGTH);
}

/**
 * The `HH:mm` of a time text, or of the time after the `T` of a date-time text, its seconds and fraction dropped;
 * empty for a date text.
 */
export function timePartOf(text: string): string {
  return text.includes('-') ? text.slice(DATE_LENGTH + 1, DATE_LENGTH + 1 + TIME_LENGTH) : text.slice(0, TIME_LENGTH);
}

/** The `yyyy-MM` of a date: the month it falls in. */
export function monthOf(date: string): string {
  return date.slice(0, MONTH_LENGTH);
}

/** The `yyyy` of a date. */
export function yearOf(date: string): string {
  return date.slice(0, YEAR_LENGTH);
}

/** A number as two digits, `07`. */
export function pad2(value: number): string {
  return padded(value, 2);
}

/** A number as four digits, `0987`: the year of a wire date. */
export function pad4(value: number): string {
  return padded(value, 4);
}

/** A number as at least `digits` digits, zeros first. */
function padded(value: number, digits: number): string {
  return String(value).padStart(digits, '0');
}

function normalizedInput(value: string): string {
  return value
    .replace(/(\.\d*?)0+$/, '$1')
    .replace(/\.$/, '')
    .replace(/(T\d{2}:\d{2}):00$/, '$1');
}

/** The time of a date typed for an instant that had none: midnight on the zone's clock. */
const MIDNIGHT = { hour: 0, minute: 0, second: 0, millisecond: 0 } as const;

/** The year, month and day of a `yyyy-MM-dd` text. */
function dateOf(text: string): Pick<WallClock, 'year' | 'month' | 'day'> {
  const [year, month, day] = datePartOf(text).split('-').map(Number);
  return { year, month, day };
}

/** The wall clock of a `yyyy-MM-ddTHH:mm:ss[.fff]` text; the fraction past milliseconds is dropped. */
function wallOfText(text: string): WallClock {
  const [hour, minute, second = 0] = text
    .slice(DATE_LENGTH + 1)
    .split(/[:.]/)
    .slice(0, 3)
    .map(Number);
  const fraction = text.match(/\.(\d+)/)?.[1] ?? '';
  return { ...dateOf(text), hour, minute, second, millisecond: Number(fraction.slice(0, 3).padEnd(3, '0')) };
}

function wallText(wall: WallClock): string {
  return (
    `${pad4(wall.year)}-${pad2(wall.month)}-${pad2(wall.day)}` +
    `T${pad2(wall.hour)}:${pad2(wall.minute)}:${pad2(wall.second)}` +
    (wall.millisecond ? `.${padded(wall.millisecond, 3)}` : '')
  );
}
