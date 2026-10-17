import type { FilterRule, UiTemporalType } from '../../core/generated/contracts';
import { instantOf, todayIn } from '../../core/time-zone';
import {
  addDays,
  addMonths,
  dateParts,
  daysInMonth,
  isIsoDate,
  type IsoDate,
  isoDate,
  type Weekday,
  weekEndOf,
  weekStartOf,
} from '../../datepicker/calendar-month';
import { formatTemporalText } from '../../datepicker/date-text';
import { type GridFilter, rulesOf } from './grid-filter-set';

/** A range of days a date header filters by, both ends included; an end left open is `null`. */
export interface DateRange {
  readonly from: IsoDate | null;
  readonly to: IsoDate | null;
}

/** The days of each period the date header offers, from today and the first day of the locale's week, in menu order. */
const PERIODS = {
  today: today => ({ from: today, to: today }),
  thisWeek: (today, weekStart) => ({ from: weekStartOf(today, weekStart), to: weekEndOf(today, weekStart) }),
  thisMonth: today => monthOf(today, 0),
  previousMonth: today => monthOf(today, -1),
  nextMonth: today => monthOf(today, 1),
  quarter1: today => quarterOf(today, 1),
  quarter2: today => quarterOf(today, 2),
  quarter3: today => quarterOf(today, 3),
  quarter4: today => quarterOf(today, 4),
  previousYear: today => yearOf(today, -1),
  thisYear: today => yearOf(today, 0),
  nextYear: today => yearOf(today, 1),
} satisfies Record<string, (today: IsoDate, weekStart: Weekday) => DateRange>;

/** A period the date header offers. */
export type DatePeriod = keyof typeof PERIODS;

/** The periods in the order the date header's menu lists them. */
export const DATE_PERIODS = Object.keys(PERIODS) as readonly DatePeriod[];

/** A wire value of the local midnight of a date-and-time: `T00:00`, with or without zero seconds and fraction. */
const LOCAL_MIDNIGHT = /^(\d{4}-\d{2}-\d{2})T00:00(?::00(?:\.0+)?)?$/;

/** The days of `period` around `today`, the week starting on `weekStart`. */
export function periodRange(period: DatePeriod, today: IsoDate, weekStart: Weekday): DateRange {
  return PERIODS[period](today, weekStart);
}

/**
 * The wire value of the start of a day by the temporal type: the date (`2026-09-01`), a local midnight
 * (`2026-09-01T00:00:00`), or the instant of the midnight of `zone`, the user's clock (`2026-08-31T21:00:00.000Z` in
 * Kyiv; a midnight the clock skips moves forward by the gap).
 */
export function midnightValue(date: IsoDate, temporalType: UiTemporalType, zone: string): string {
  switch (temporalType) {
    case 'dateOnly':
      return date;
    case 'dateTime':
      return `${date}T00:00:00`;
    case 'dateTimeOffset':
      return midnightIn(date, zone).toISOString();
  }
}

/**
 * The filter of a range on `field`: `greaterThanOrEqual` its first day and `lessThan` the day after its last, each a
 * midnight by the temporal type, an instant's on the clock of `zone`, both in one `and` group; one rule for one end,
 * and none for no end. Ends given in the wrong order are swapped.
 */
export function rangeFilter(field: string, range: DateRange, temporalType: UiTemporalType, zone: string): GridFilter | null {
  const { from, to } = orderedRange(range);
  const rules: FilterRule[] = [];
  if (from !== null) {
    rules.push({ kind: 'FilterRule', field, operator: 'greaterThanOrEqual', value: midnightValue(from, temporalType, zone) });
  }
  if (to !== null) {
    rules.push({ kind: 'FilterRule', field, operator: 'lessThan', value: midnightValue(addDays(to, 1), temporalType, zone) });
  }
  if (rules.length === 0) {
    return null;
  }
  return rules.length === 1 ? rules[0] : { kind: 'FilterGroup', logic: 'and', items: rules };
}

/**
 * The range a filter holds when it has the shape `rangeFilter` writes, read back by the temporal type, an instant on
 * the clock of `zone`; `null` for any other filter — another operator, an `or`, a nested group, a bound that is not a
 * midnight, ends that hold no day.
 */
export function rangeOfFilter(filter: GridFilter | undefined, temporalType: UiTemporalType, zone: string): DateRange | null {
  if (!filter) {
    return null;
  }
  const isFlatAnd = filter.kind === 'FilterRule' || (filter.logic === 'and' && filter.items.every(item => item.kind === 'FilterRule'));
  if (!isFlatAnd) {
    return null;
  }
  const rules = rulesOf(filter);
  const lower = rules.filter(rule => rule.operator === 'greaterThanOrEqual');
  const upper = rules.filter(rule => rule.operator === 'lessThan');
  const onlyBounds = lower.length <= 1 && upper.length <= 1 && lower.length + upper.length === rules.length;
  if (!onlyBounds || rules.length === 0) {
    return null;
  }
  const from = lower.length ? dayOfMidnight(lower[0].value, temporalType, zone) : null;
  const end = upper.length ? dayOfMidnight(upper[0].value, temporalType, zone) : null;
  const unreadable = (lower.length > 0 && from === null) || (upper.length > 0 && end === null);
  if (unreadable) {
    return null;
  }
  const to = end === null ? null : addDays(end, -1);
  return from !== null && to !== null && from > to ? null : { from, to };
}

/** A range as the header shows it: `01.09.2026 – 30.09.2026` in the locale's numeric dates, one date when both ends are the same day. */
export function rangeText(range: DateRange, locale: string): string {
  const from = range.from === null ? '' : formatTemporalText(range.from, 'date', locale);
  if (range.from === range.to) {
    return from;
  }
  const to = range.to === null ? '' : formatTemporalText(range.to, 'date', locale);
  return `${from} – ${to}`.trim();
}

function monthOf(today: IsoDate, shift: number): DateRange {
  const { year, month } = dateParts(addMonths(firstOfMonth(today), shift));
  return { from: isoDate(year, month, 1), to: isoDate(year, month, daysInMonth(year, month)) };
}

function quarterOf(today: IsoDate, quarter: number): DateRange {
  const { year } = dateParts(today);
  const last = quarter * 3;
  return { from: isoDate(year, last - 2, 1), to: isoDate(year, last, daysInMonth(year, last)) };
}

function yearOf(today: IsoDate, shift: number): DateRange {
  const year = dateParts(today).year + shift;
  return { from: isoDate(year, 1, 1), to: isoDate(year, 12, 31) };
}

function firstOfMonth(date: IsoDate): IsoDate {
  const { year, month } = dateParts(date);
  return isoDate(year, month, 1);
}

function orderedRange(range: DateRange): DateRange {
  const reversed = range.from !== null && range.to !== null && range.from > range.to;
  return reversed ? { from: range.to, to: range.from } : range;
}

/** The instant of the midnight of a date on the clock of `zone`; one the clock skips moves forward by the gap. */
function midnightIn(date: IsoDate, zone: string): Date {
  return instantOf({ ...dateParts(date), hour: 0, minute: 0, second: 0, millisecond: 0 }, zone);
}

/** The day whose midnight a wire value is, by the temporal type; `null` for a value that is no midnight. */
function dayOfMidnight(value: unknown, temporalType: UiTemporalType, zone: string): IsoDate | null {
  if (typeof value !== 'string') {
    return null;
  }
  switch (temporalType) {
    case 'dateOnly':
      return isIsoDate(value) ? value : null;
    case 'dateTime':
      return LOCAL_MIDNIGHT.exec(value)?.[1] ?? null;
    case 'dateTimeOffset': {
      const instant = new Date(value);
      if (Number.isNaN(instant.getTime())) {
        return null;
      }
      const day = todayIn(zone, instant);
      return midnightIn(day, zone).getTime() === instant.getTime() ? day : null;
    }
  }
}
