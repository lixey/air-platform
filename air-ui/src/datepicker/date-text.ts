import { datePartOf, pad2, type TemporalKind, timePartOf } from '../controls/temporal-text';
import { dateParts, daysInMonth, isIsoDate, type IsoDate, isoDate, utcMidnight, type Weekday } from './calendar-month';

/** A part of a written date. */
export type DatePart = 'day' | 'month' | 'year';

/** The Intl data the picker reads, typed here because the library's `lib` (ES2022) predates it. */
interface WeekInfo {
  readonly firstDay: number;
  /** The days of the weekend, 1 (Monday) to 7 (Sunday). */
  readonly weekend?: readonly number[];
}
type LocaleWithWeekInfo = Intl.Locale & { getWeekInfo?(): WeekInfo; readonly weekInfo?: WeekInfo };

/**
 * The picker writes Gregorian dates in Latin digits whatever the locale's own calendar and digits, so what it shows
 * is what it parses back and a picked day is the day stored.
 */
const GREGORIAN = { calendar: 'gregory', numberingSystem: 'latn', timeZone: 'UTC' } as const;
const DATE_OPTIONS: Intl.DateTimeFormatOptions = { ...GREGORIAN, day: '2-digit', month: '2-digit', year: 'numeric' };
const ISO_ORDER: readonly DatePart[] = ['year', 'month', 'day'];

/** A date, then optionally a time `H:mm` after spaces, a comma or the ISO `T`. */
const DATE_AND_TIME = /^(.+?)(?:[\sT,]+(\d{1,2}:\d{2}))?$/;
const TIME = /^(\d{1,2}):(\d{2})$/;
const DAY_OR_MONTH = /^\d{1,2}$/;
const YEAR = /^(\d{2}|\d{4})$/;

/** A two-digit year lands in the century that puts it at most this many years after today's. */
const YEARS_AHEAD = 20;

/** The regions whose week does not start on Monday (CLDR), for a runtime without `Intl.Locale#getWeekInfo`. */
const FIRST_DAY_BY_REGION: Readonly<Record<string, Weekday>> = {
  ...regions(
    'AG AS BD BR BS BT BW BZ CA CO DM DO ET GT GU HK HN ID IL IN IS JM JP KE KH KR LA MH MM MO MT MX MZ NI NP PA PE ' +
      'PH PK PR PT PY SA SG SV TH TT TW UM US VE VI WS YE ZA ZW',
    0,
  ),
  ...regions('AF AE BH DJ DZ EG IQ IR JO KW LY OM QA SD SY', 6),
  ...regions('MV', 5),
};

/** The regions whose weekend is not Saturday and Sunday (CLDR), for a runtime without `Intl.Locale#getWeekInfo`. */
const WEEKEND_BY_REGION: Readonly<Record<string, readonly Weekday[]>> = {
  ...weekends('BH DZ EG IL IQ JO KW LY OM QA SA SD SY YE', [5, 6]),
  ...weekends('AF', [4, 5]),
  ...weekends('IR', [5]),
  ...weekends('IN UG', [0]),
};
const SATURDAY_AND_SUNDAY: readonly Weekday[] = [6, 0];

const FORMATS = new Map<string, Intl.DateTimeFormat>();

/** The order of day, month and year in the locale's numeric date: `['month', 'day', 'year']` for `en-US`. */
export function dateOrder(locale: string): readonly DatePart[] {
  return formatterOf(locale, 'date', DATE_OPTIONS)
    .formatToParts(0)
    .map(part => part.type)
    .filter((type): type is DatePart => type === 'day' || type === 'month' || type === 'year');
}

/**
 * The editor text (`controls/temporal-text.ts`) of what the user typed, or `null` for a text that is not a real
 * value. A date takes three groups of digits in the locale's order, any non-digit between them, one or two digits for
 * the day and the month and two or four for the year; four digits first read as an ISO date whatever the locale. A
 * two-digit year lands within 80 years before and 20 after `today`. A time is `H:mm`; a date and time takes a date,
 * then a time after a space, a comma or `T`, and midnight when the time is left out.
 */
export function parseTemporalText(text: string, kind: TemporalKind, locale: string, today: IsoDate): string | null {
  const trimmed = text.trim();
  if (kind === 'time') {
    return timeText(trimmed);
  }
  const [, datePart, timePart] = DATE_AND_TIME.exec(trimmed) ?? [];
  const timeOnADate = kind === 'date' && timePart !== undefined;
  if (datePart === undefined || timeOnADate) {
    return null;
  }
  const date = dateText(datePart, locale, today);
  if (date === null || kind === 'date') {
    return date;
  }
  const time = timePart === undefined ? '00:00' : timeText(timePart);
  return time === null ? null : `${date}T${time}`;
}

/**
 * What the control and the picker's input show for an editor text: the date in the locale's numeric format and the
 * time as `HH:mm`, 24-hour in every locale, so the shown text parses back; an unreadable text is shown as it is.
 */
export function formatTemporalText(text: string, kind: TemporalKind, locale: string): string {
  if (kind === 'time') {
    return text;
  }
  const datePart = datePartOf(text);
  if (!isIsoDate(datePart)) {
    return text;
  }
  const date = formatterOf(locale, 'date', DATE_OPTIONS).format(utcMidnight(datePart));
  return kind === 'date' ? date : `${date} ${timePartOf(text)}`;
}

/**
 * The first day of the locale's week: `Intl.Locale#getWeekInfo`, else the region of the locale's likely subtags
 * (Sunday for `en-US`, Saturday for `ar-EG`), else Monday.
 */
export function weekStart(locale: string): Weekday {
  const tag = new Intl.Locale(locale) as LocaleWithWeekInfo;
  const info = tag.getWeekInfo?.() ?? tag.weekInfo;
  if (info) {
    return (info.firstDay % 7) as Weekday;
  }
  return FIRST_DAY_BY_REGION[tag.maximize().region ?? ''] ?? 1;
}

/**
 * The days of the locale's weekend, `Date#getDay` numbered: `Intl.Locale#getWeekInfo`, else the region of the locale's
 * likely subtags (Friday and Saturday for `ar-EG`, Sunday alone for `en-IN`), else Saturday and Sunday.
 */
export function weekendDays(locale: string): ReadonlySet<Weekday> {
  const tag = new Intl.Locale(locale) as LocaleWithWeekInfo;
  const weekend = (tag.getWeekInfo?.() ?? tag.weekInfo)?.weekend;
  if (weekend) {
    return new Set(weekend.map(day => (day % 7) as Weekday));
  }
  return new Set(WEEKEND_BY_REGION[tag.maximize().region ?? ''] ?? SATURDAY_AND_SUNDAY);
}

/** The locale's twelve month names as the months view shows them, January first. */
export function monthNames(locale: string): readonly string[] {
  const names = formatterOf(locale, 'month', { ...GREGORIAN, month: 'long' });
  return Array.from({ length: 12 }, (_, index) => names.format(utcMidnight(isoDate(2000, index + 1, 1))));
}

/** The locale's seven weekday names in the order of its week (`weekStart`), as the days grid heads its columns. */
export function weekdayNames(locale: string, width: 'short' | 'long'): readonly string[] {
  const names = formatterOf(locale, `weekday-${width}`, { ...GREGORIAN, weekday: width });
  const first = weekStart(locale);
  // January 2, 2000 was a Sunday, the weekday 0.
  return Array.from({ length: 7 }, (_, index) => names.format(utcMidnight(isoDate(2000, 1, 2 + ((first + index) % 7)))));
}

/** The month and year of `date` as the button of the days view names them: `September 2026` in `en-US`. */
export function monthTitle(date: IsoDate, locale: string): string {
  return formatterOf(locale, 'month-year', { ...GREGORIAN, month: 'long', year: 'numeric' }).format(utcMidnight(date));
}

function dateText(text: string, locale: string, today: IsoDate): IsoDate | null {
  const groups = text.split(/\D+/).filter(group => group !== '');
  if (groups.length !== 3) {
    return null;
  }
  const order = groups[0].length === 4 ? ISO_ORDER : dateOrder(locale);
  const [day, month, year] = (['day', 'month', 'year'] as const).map(part => groups[order.indexOf(part)]);
  const digitsFit = DAY_OR_MONTH.test(day) && DAY_OR_MONTH.test(month) && YEAR.test(year);
  if (!digitsFit) {
    return null;
  }
  const fullYear = year.length === 2 ? yearOfTwoDigits(Number(year), dateParts(today).year) : Number(year);
  const monthNumber = Number(month);
  const dayNumber = Number(day);
  const real = fullYear >= 1 && monthNumber >= 1 && monthNumber <= 12 && dayNumber >= 1;
  return real && dayNumber <= daysInMonth(fullYear, monthNumber) ? isoDate(fullYear, monthNumber, dayNumber) : null;
}

function timeText(text: string): string | null {
  const [, hours, minutes] = TIME.exec(text) ?? [];
  if (hours === undefined) {
    return null;
  }
  return Number(hours) <= 23 && Number(minutes) <= 59 ? `${pad2(Number(hours))}:${minutes}` : null;
}

function yearOfTwoDigits(twoDigits: number, currentYear: number): number {
  const year = currentYear - (currentYear % 100) + twoDigits;
  return year > currentYear + YEARS_AHEAD ? year - 100 : year;
}

/** One formatter per locale and purpose: building an `Intl.DateTimeFormat` costs far more than using one. */
function formatterOf(locale: string, purpose: string, options: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  const key = `${purpose}|${locale}`;
  let formatter = FORMATS.get(key);
  if (!formatter) {
    formatter = new Intl.DateTimeFormat(locale, options);
    FORMATS.set(key, formatter);
  }
  return formatter;
}

function regions(codes: string, firstDay: Weekday): Record<string, Weekday> {
  return Object.fromEntries(codes.split(' ').map(code => [code, firstDay]));
}

function weekends(codes: string, days: readonly Weekday[]): Record<string, readonly Weekday[]> {
  return Object.fromEntries(codes.split(' ').map(code => [code, days]));
}
