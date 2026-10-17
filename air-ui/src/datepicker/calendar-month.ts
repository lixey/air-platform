import { pad2, pad4 } from '../controls/temporal-text';
import { todayIn } from '../core/time-zone';

/**
 * A calendar date as `yyyy-MM-dd`: the date part of the editor text the temporal rules convert
 * (`controls/temporal-text.ts`). The picker's active day, its value and today are all of this shape, so a move is
 * a function of one string and two dates compare as strings.
 */
export type IsoDate = string;

/** A day of the week as `Date#getDay` counts it: 0 is Sunday. */
export type Weekday = 0 | 1 | 2 | 3 | 4 | 5 | 6;

/** One cell of a month's grid. */
export interface CalendarDay {
  readonly date: IsoDate;
  /** The day of the month, 1 to 31. */
  readonly day: number;
  /** False for a day of the neighbouring month that fills the first or the last weeks. */
  readonly inMonth: boolean;
}

/** One row of a month's grid: its seven days and its ISO 8601 week number, the one its Thursday falls in. */
export interface CalendarWeek {
  readonly days: readonly CalendarDay[];
  readonly week: number;
}

/** A view of the picker's calendar: the days of a month, the months of a year, a page of years. */
export type CalendarView = 'days' | 'months' | 'years';

/** The years a page of the years view shows. */
export const YEARS_PER_PAGE = 12;

/** The cells a row of the months and of the years view holds: the up and down arrows move by this many. */
export const PAGE_COLUMNS = 3;

/** Six weeks hold every month, so the grid keeps its height while the user pages through the months. */
const WEEKS_PER_GRID = 6;
const DAY_MS = 86_400_000;
/** Thursday as `Date#getUTCDay` counts it: the day that decides the ISO week of a row. */
const THURSDAY = 4;
/** The years the wire's four-digit year can carry; a move stops at their ends. */
const FIRST_DATE: IsoDate = '0001-01-01';
const LAST_DATE: IsoDate = '9999-12-31';
/** A calendar date's text: four digits of the year, two of the month, two of the day. */
const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

/** The date of a year, a month (1 to 12) and a day, the year padded to four digits. */
export function isoDate(year: number, month: number, day: number): IsoDate {
  return `${pad4(year)}-${pad2(month)}-${pad2(day)}`;
}

/** Whether a text is a calendar date as the wire writes one: `yyyy-MM-dd` and nothing more. */
export function isIsoDate(text: string): text is IsoDate {
  return ISO_DATE.test(text);
}

/** The year, the month (1 to 12) and the day of a date. */
export function dateParts(date: IsoDate): { readonly year: number; readonly month: number; readonly day: number } {
  const [year, month, day] = date.split('-').map(Number);
  return { year, month, day };
}

/** The days of a month (1 to 12) of a year, February 29 in a leap year. */
export function daysInMonth(year: number, month: number): number {
  return utc(year, month + 1, 0).getUTCDate();
}

/** The UTC midnight of a date: the moment an `Intl.DateTimeFormat` in UTC writes as that date. */
export function utcMidnight(date: IsoDate): Date {
  const { year, month, day } = dateParts(date);
  return utc(year, month, day);
}

/**
 * The date of `moment` on the clock of `zone`, the user's (`LocaleService.zone`), not of UTC or of the browser: what
 * the picker marks as today and the periods count from.
 */
export function localIsoDate(moment: Date, zone: string): IsoDate {
  return todayIn(zone, moment);
}

/**
 * The six weeks of the month that holds `date`, each starting on `weekStart`: the first week holds the month's first
 * day, and the days of the neighbouring months that fill the weeks carry `inMonth: false`. Each week carries the ISO
 * 8601 number of its Thursday, which a row of seven days holds whatever day it starts on.
 */
export function monthWeeks(date: IsoDate, weekStart: Weekday): readonly CalendarWeek[] {
  const { year, month } = dateParts(date);
  const firstOfMonth = isoDate(year, month, 1);
  const first = isoOf(shifted(firstOfMonth, -offsetInWeek(firstOfMonth, weekStart)));
  return Array.from({ length: WEEKS_PER_GRID }, (_, week) => {
    const days = Array.from({ length: 7 }, (_, weekday) => {
      const cell = isoOf(shifted(first, week * 7 + weekday));
      const parts = dateParts(cell);
      return { date: cell, day: parts.day, inMonth: parts.month === month };
    });
    const thursday = days.find(day => utcMidnight(day.date).getUTCDay() === THURSDAY) ?? days[0];
    return { days, week: isoWeek(thursday.date) };
  });
}

/**
 * The ISO 8601 week of a date: weeks run Monday to Sunday, and the first week of a year is the one that holds its first
 * Thursday, so December 29 may fall in week 1 of the next year and January 3 in week 53 of the last.
 */
export function isoWeek(date: IsoDate): number {
  const moment = utcMidnight(date);
  const fromMonday = (moment.getUTCDay() + 6) % 7;
  const thursday = new Date(moment.getTime() + (3 - fromMonday) * DAY_MS);
  const firstOfYear = utc(thursday.getUTCFullYear(), 1, 1);
  return Math.floor((thursday.getTime() - firstOfYear.getTime()) / (7 * DAY_MS)) + 1;
}

/** The date `days` later, or earlier when negative, stopping at the first and the last date of the four-digit years. */
export function addDays(date: IsoDate, days: number): IsoDate {
  const moved = shifted(date, days);
  return boundaryOf(moved.getUTCFullYear()) ?? isoOf(moved);
}

/** The same day `months` later, or earlier when negative, clamped to the length of the month it lands in. */
export function addMonths(date: IsoDate, months: number): IsoDate {
  const { year, month, day } = dateParts(date);
  const index = year * 12 + month - 1 + months;
  const targetYear = Math.floor(index / 12);
  const targetMonth = index - targetYear * 12 + 1;
  return boundaryOf(targetYear) ?? isoDate(targetYear, targetMonth, Math.min(day, daysInMonth(targetYear, targetMonth)));
}

/** The same day `years` later, or earlier when negative: February 29 lands on February 28 of a common year. */
export function addYears(date: IsoDate, years: number): IsoDate {
  return addMonths(date, years * 12);
}

/** The first day of the week that holds `date`. */
export function weekStartOf(date: IsoDate, weekStart: Weekday): IsoDate {
  return addDays(date, -offsetInWeek(date, weekStart));
}

/** The last day of the week that holds `date`. */
export function weekEndOf(date: IsoDate, weekStart: Weekday): IsoDate {
  return addDays(date, 6 - offsetInWeek(date, weekStart));
}

/**
 * The page of the years view that holds `year`: twelve years starting on a multiple of twelve, so paging by twelve
 * lands on the neighbouring page; the years outside 1 to 9999 are left out.
 */
export function yearPage(year: number): readonly number[] {
  const first = year - (year % YEARS_PER_PAGE);
  return Array.from({ length: YEARS_PER_PAGE }, (_, index) => first + index).filter(y => y >= 1 && y <= 9999);
}

/**
 * Where a key moves the active date of a view, or `null` for a key that moves nothing. In the days the arrows move by a
 * day and a week, `PageUp` and `PageDown` by a month and with `Shift` by a year, `Home` and `End` to the week's ends;
 * in the months and the years the arrows move by one cell and by a row, `Home` and `End` to the first and the last
 * cell, `PageUp` and `PageDown` by a year among the months and by a page among the years.
 */
export function calendarMove(
  view: CalendarView,
  { key, shiftKey }: Pick<KeyboardEvent, 'key' | 'shiftKey'>,
  date: IsoDate,
  weekStart: Weekday,
): IsoDate | null {
  if (view === 'days') {
    return dayMove(key, shiftKey, date, weekStart);
  }
  const months = (view === 'months' ? monthMoves(date) : yearMoves(date)).get(key);
  return months === undefined ? null : addMonths(date, months);
}

/** How many days `date` stands after the start of its week. */
function offsetInWeek(date: IsoDate, weekStart: Weekday): number {
  return (utcMidnight(date).getUTCDay() - weekStart + 7) % 7;
}

function dayMove(key: string, shiftKey: boolean, date: IsoDate, weekStart: Weekday): IsoDate | null {
  switch (key) {
    case 'ArrowLeft':
      return addDays(date, -1);
    case 'ArrowRight':
      return addDays(date, 1);
    case 'ArrowUp':
      return addDays(date, -7);
    case 'ArrowDown':
      return addDays(date, 7);
    case 'PageUp':
      return shiftKey ? addYears(date, -1) : addMonths(date, -1);
    case 'PageDown':
      return shiftKey ? addYears(date, 1) : addMonths(date, 1);
    case 'Home':
      return weekStartOf(date, weekStart);
    case 'End':
      return weekEndOf(date, weekStart);
    default:
      return null;
  }
}

/** The keys of the months view, as the months each moves by. */
function monthMoves(date: IsoDate): ReadonlyMap<string, number> {
  const { month } = dateParts(date);
  return cellMoves(1, 1 - month, 12 - month, 12);
}

/** The keys of the years view, as the months each moves by: a cell is a year, a page twelve of them. */
function yearMoves(date: IsoDate): ReadonlyMap<string, number> {
  const { year } = dateParts(date);
  const page = yearPage(year);
  return cellMoves(12, (page[0] - year) * 12, (page[page.length - 1] - year) * 12, YEARS_PER_PAGE * 12);
}

function cellMoves(cell: number, toFirst: number, toLast: number, page: number): ReadonlyMap<string, number> {
  return new Map([
    ['ArrowLeft', -cell],
    ['ArrowRight', cell],
    ['ArrowUp', -cell * PAGE_COLUMNS],
    ['ArrowDown', cell * PAGE_COLUMNS],
    ['Home', toFirst],
    ['End', toLast],
    ['PageUp', -page],
    ['PageDown', page],
  ]);
}

function shifted(date: IsoDate, days: number): Date {
  return new Date(utcMidnight(date).getTime() + days * DAY_MS);
}

/** The first or the last date of the four-digit years when `year` falls outside them, else null. */
function boundaryOf(year: number): IsoDate | null {
  if (year < 1) {
    return FIRST_DATE;
  }
  return year > 9999 ? LAST_DATE : null;
}

function isoOf(moment: Date): IsoDate {
  return isoDate(moment.getUTCFullYear(), moment.getUTCMonth() + 1, moment.getUTCDate());
}

/** `Date.UTC` reads a year below 100 as 19xx; `setUTCFullYear` takes the year as it is. */
function utc(year: number, month: number, day: number): Date {
  const moment = new Date(0);
  moment.setUTCFullYear(year, month - 1, day);
  return moment;
}
