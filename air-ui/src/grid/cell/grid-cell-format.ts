/**
 * How the grid writes a value in the session's culture: numbers, amounts, dates, stamps, and counts, each through
 * one `Intl` formatter per culture and digits, grouping or kind, kept for the life of the page — a cell asks for its formatter,
 * never builds one. Amounts are written as `controls/amount-text.ts` writes them, the same text the amount box shows.
 */

import { type AmountFormat, amountText, declaredDigits } from '../../controls/amount-text';
import type { GridCellMoneyModel, GridCellPercentModel, GridColumnDto, Reference } from '../../core/generated/contracts';
import { knownZone } from '../../core/time-zone';
import { isIsoDate, utcMidnight } from '../../datepicker/calendar-month';

/** What a temporal column shows: a day, or a day with its hour and minute. */
export type CellDateKind = 'date' | 'datetime';

const NUMBERS = new Map<string, Intl.NumberFormat>();
const DATES = new Map<string, Intl.DateTimeFormat>();

const DATE_PARTS: Intl.DateTimeFormatOptions = { day: '2-digit', month: '2-digit', year: 'numeric' };
const DATETIME_PARTS: Intl.DateTimeFormatOptions = { ...DATE_PARTS, hour: '2-digit', minute: '2-digit' };
/** A time of day is written on a fixed day in UTC, so no zone of the browser moves its hour. */
const TIME_PARTS: Intl.DateTimeFormatOptions = { hour: '2-digit', minute: '2-digit', timeZone: 'UTC' };
/**
 * The parts of each kind: an instant's are written on the user's clock, the zone its formatter is built for; a date's
 * and a local date-time's at UTC, where their parts were put, so no zone moves them.
 */
const FORMAT_PARTS = {
  date: DATE_PARTS,
  datetime: DATETIME_PARTS,
  time: TIME_PARTS,
  'local-date': { ...DATE_PARTS, timeZone: 'UTC' },
  'local-datetime': { ...DATETIME_PARTS, timeZone: 'UTC' },
} satisfies Record<CellDateKind | `local-${CellDateKind}` | 'time', Intl.DateTimeFormatOptions>;

/** A time of day of the wire: `HH:mm`, with seconds and a fraction when it holds them. */
const TIME_OF_DAY = /^([01]\d|2[0-3]):([0-5]\d)(?::[0-5]\d(?:\.\d+)?)?$/;

/** A local date-time of the wire: `yyyy-MM-ddTHH:mm`, with seconds and a fraction when it holds them, and no `Z` or offset. */
const LOCAL_DATE_TIME = /^(\d{4})-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])T([01]\d|2[0-3]):([0-5]\d)(?::([0-5]\d)(?:\.(\d+))?)?$/;

/** The most fraction digits a number is written with when nothing declares them. */
const UNDECLARED_DIGITS = 2;

/** The formatter of a culture, its fraction digits and its grouping: grouped by the culture unless `isGrouped` is false. */
function numberFormatOf(locale: string, digits: number | null, isGrouped: boolean): Intl.NumberFormat {
  const key = `${locale}|${digits ?? `<=${UNDECLARED_DIGITS}`}|${isGrouped}`;
  let format = NUMBERS.get(key);
  if (!format) {
    const fraction: Intl.NumberFormatOptions =
      digits === null ? { maximumFractionDigits: UNDECLARED_DIGITS } : { minimumFractionDigits: digits, maximumFractionDigits: digits };
    format = new Intl.NumberFormat(locale, isGrouped ? fraction : { ...fraction, useGrouping: false });
    NUMBERS.set(key, format);
  }
  return format;
}

/** The formatter of a kind in a culture; an instant's kinds (`date`, `datetime`) on the clock of `zone`. */
function dateFormatOf(locale: string, kind: keyof typeof FORMAT_PARTS, zone?: string): Intl.DateTimeFormat {
  const key = `${locale}|${kind}|${zone ?? ''}`;
  let format = DATES.get(key);
  if (!format) {
    format = new Intl.DateTimeFormat(locale, zone ? { ...FORMAT_PARTS[kind], timeZone: zone } : FORMAT_PARTS[kind]);
    DATES.set(key, format);
  }
  return format;
}

function isBlank(value: unknown): value is null | undefined | '' {
  return value === null || value === undefined || value === '';
}

/** The value written by `write` when it is a number or a text of one; the text itself otherwise, and nothing for nothing. */
function asNumber(value: unknown, write: (number: number) => string): string {
  if (isBlank(value)) {
    return '';
  }
  const number = typeof value === 'number' ? value : Number(value);
  return Number.isFinite(number) ? write(number) : String(value);
}

/** What a temporal value names: an instant, or a date or a local date-time whose parts are held at UTC. */
interface CellMoment {
  readonly at: Date;
  readonly local: boolean;
}

/** The parts of a local date-time text at UTC, or nothing for another text; `setUTCFullYear` keeps a year below 100. */
function localDateTimeOf(text: string): Date | null {
  const parts = LOCAL_DATE_TIME.exec(text);
  if (!parts) {
    return null;
  }
  const [, year, month, day, hour, minute, second = '0', fraction = ''] = parts;
  const at = new Date(0);
  at.setUTCFullYear(Number(year), Number(month) - 1, Number(day));
  at.setUTCHours(Number(hour), Number(minute), Number(second), Number(fraction.padEnd(3, '0').slice(0, 3)));
  return at;
}

/**
 * What a value names: a `yyyy-MM-dd` text is that day and a date-time text without `Z` or offset a local date-time, the
 * same day and hour on every clock (even an hour a clock skips or repeats), and a text with one, a number or a `Date`
 * is the instant `Date` reads.
 */
function momentOf(value: unknown): CellMoment | null {
  if (value instanceof Date) {
    return Number.isNaN(value.getTime()) ? null : { at: value, local: false };
  }
  if (typeof value !== 'string' && typeof value !== 'number') {
    return null;
  }
  const local = typeof value === 'string' ? localDateTimeOf(value) : null;
  if (local) {
    return { at: local, local: true };
  }
  if (typeof value === 'string' && isIsoDate(value)) {
    return { at: utcMidnight(value), local: true };
  }
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : { at: date, local: false };
}

/** The culture a value is written in and the clock an instant of it is shown on: the session's and the user's. */
export interface ValueClock {
  readonly locale: string;
  readonly zone: string;
}

/**
 * The value as the culture's short date, or its date and time: an instant on the clock of `zone` (read as UTC when the
 * browser does not know it), a date and a local date-time as written; empty for nothing and the text itself for what
 * is not a date.
 */
export function formatCellDate(value: unknown, kind: CellDateKind, locale: string, zone: string): string {
  if (isBlank(value)) {
    return '';
  }
  const moment = momentOf(value);
  if (!moment) {
    return String(value);
  }
  return (moment.local ? dateFormatOf(locale, `local-${kind}`) : dateFormatOf(locale, kind, knownZone(zone))).format(moment.at);
}

/**
 * A time of day as the culture writes its hour and minute, as the stamp's
 * cell does; empty for nothing and the text itself for what is not a time.
 */
export function formatCellTime(value: unknown, locale: string): string {
  if (isBlank(value)) {
    return '';
  }
  const time = typeof value === 'string' ? TIME_OF_DAY.exec(value) : null;
  return time ? dateFormatOf(locale, 'time').format(Date.UTC(2000, 0, 1, Number(time[1]), Number(time[2]))) : String(value);
}

/**
 * The value as a number grouped by the locale unless `isGrouped` is false, when the digits are written alone with the
 * locale's decimal sign: with exactly `decimals` fraction digits when the column declares a whole number of them from
 * 0 to 20, with two at most otherwise; empty for nothing and the text itself for what is not a number.
 */
export function formatCellNumber(value: unknown, locale: string, decimals?: number | null, isGrouped?: boolean | null): string {
  // Intl.NumberFormat throws outside 0..100; what the server lets through unchecked falls back to the default
  return asNumber(value, number => numberFormatOf(locale, declaredDigits(decimals), isGrouped !== false).format(number));
}

/**
 * The value as an amount by its format: money grouped by the locale with its currency and exactly its digits, two when
 * it declares none; a percent as stored, with the locale's percent sign; empty for nothing and the text itself for what
 * is not a number.
 */
export function formatCellAmount(value: unknown, locale: string, format: AmountFormat): string {
  return asNumber(value, number => amountText(number, locale, format));
}

/** How a money cell's model writes an amount: its digits and its currency. */
export function moneyFormatOf(model: Pick<GridCellMoneyModel, 'decimals' | 'currency'> | null | undefined): AmountFormat {
  return { kind: 'money', digits: model?.decimals, currency: model?.currency };
}

/** How a percent cell's model writes an amount: its digits. */
export function percentFormatOf(model: Pick<GridCellPercentModel, 'decimals'> | null | undefined): AmountFormat {
  return { kind: 'percent', digits: model?.decimals };
}

/** The reference a value carries: the `id` and `name` the server projected from a navigation, or nothing for anything else. */
export function referenceOf(value: unknown): Reference | null {
  return typeof value === 'object' && value !== null && typeof (value as Partial<Reference>).name === 'string'
    ? (value as Reference)
    : null;
}

/**
 * A value as its column's standard cell writes it: by the cell's kind first — money, a percent, a number by the
 * grouping of its cell, a reference and a select column's option by its name, an open-form cell's value as text,
 * ungrouped — then by the column's type — a number, a date, a stamp and a time of day by the locale, an instant on the
 * clock of `zone`, the rest as text; empty for nothing. A reference or a select column's value that is no reference,
 * the key a filter holds, is written as text, in digits alone, whatever the column's type.
 */
export function formatCellValue(
  value: unknown,
  column: Pick<GridColumnDto, 'type' | 'cell'> | null | undefined,
  locale: string,
  zone: string,
): string {
  const cell = column?.cell;
  switch (cell?.kind) {
    case 'GridCellMoney':
      return formatCellAmount(value, locale, moneyFormatOf(cell));
    case 'GridCellPercent':
      return formatCellAmount(value, locale, percentFormatOf(cell));
    case 'GridCellNumber':
      return formatCellNumber(value, locale, cell.decimals, cell.isGrouped);
    case 'GridCellOpenForm':
      return formatCellText(value);
    case 'GridCellReference':
    case 'GridCellSelect': {
      const reference = referenceOf(value);
      // The key a filter holds: digits alone, whatever the column's type
      return reference ? reference.name : formatCellText(value);
    }
  }
  switch (column?.type) {
    case 'number':
      return formatCellNumber(value, locale);
    case 'date':
    case 'datetime':
      return formatCellDate(value, column.type, locale, zone);
    case 'time':
      return formatCellTime(value, locale);
    default:
      return formatCellText(value);
  }
}

/** A value as text: nothing for nothing. */
export function formatCellText(value: unknown): string {
  return value === null || value === undefined ? '' : String(value);
}

/** A count — a total, a number of rows — grouped by the locale, without fraction digits. */
export function formatCount(value: number, locale: string): string {
  return numberFormatOf(locale, 0, true).format(value);
}
