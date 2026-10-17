/**
 * How an amount — money or a percent — is written and read in a culture: with its unit where a cell writes it,
 * grouped without the unit where a box shows it after the focus leaves, plain while the box has the focus, and parsed
 * back from what a user types. One `Intl` formatter per culture and options, kept for the life of the page.
 */

/** What an amount counts: money, in the currency it names or in none, or a percent, the stored number itself (`15` is 15 %). */
export type AmountKind = 'money' | 'percent';

/** What an amount is and how it is written: its kind, its fraction digits (two when unset), its currency (money only). */
export interface AmountFormat {
  readonly kind: AmountKind;
  readonly digits?: number | null;
  readonly currency?: string | null;
}

/** The fraction digits an amount keeps when it declares none: exactly so many for money, at most so many for a percent. */
const DEFAULT_DIGITS = 2;

/** The most fraction digits a number format takes. */
const MOST_DIGITS = 20;

/**
 * Typed text as a number, its group signs brought to a space (typed spaces are dropped before) and its decimal sign to
 * `.`: an optional minus, digits either plain or grouped by three after a lead of one to three, and a fraction after the
 * point; at least one digit.
 */
const AMOUNT = /^-?(?=\.?\d)(?:\d{1,3}(?: \d{3})+|\d*)(?:\.(\d*))?$/;

const FORMATS = new Map<string, Intl.NumberFormat>();
const SIGNS = new Map<string, NumberSigns>();

/** The signs a culture writes a number with. */
interface NumberSigns {
  readonly group: string;
  readonly decimal: string;
  readonly minus: string;
}

/** The declared fraction digits when they are a whole number from 0 to 20, the range a number format takes; null otherwise. */
export function declaredDigits(digits: number | null | undefined): number | null {
  return typeof digits === 'number' && Number.isInteger(digits) && digits >= 0 && digits <= MOST_DIGITS ? digits : null;
}

/**
 * An amount as a cell writes it, grouped by the culture with its unit where the culture puts it: money with exactly its
 * digits and the sign of its currency (`1 234,50 ₴`), a grouped number without a currency; a percent with its digits
 * and the percent sign (`15%`).
 */
export function amountText(value: number, locale: string, format: AmountFormat): string {
  return formatOf(locale, { ...digitOptions(format), ...unitOptions(format) }).format(value);
}

/**
 * An amount as a box shows it once the focus leaves: grouped as a cell
 * writes it, without its unit (`1 234,50`); its currency is not read.
 */
export function groupedAmountText(value: number, locale: string, format: AmountFormat): string {
  return formatOf(locale, digitOptions(format)).format(value);
}

/**
 * The unit a box writes beside its amount: the sign of the currency in
 * the culture, `%` for a percent, nothing for money in no currency.
 */
export function amountUnitText(locale: string, format: AmountFormat): string {
  const { kind, currency } = format;
  if (kind === 'percent') {
    return '%';
  }
  if (!currency) {
    return '';
  }
  const parts = formatOf(locale, unitOptions(format)).formatToParts(0);
  return parts.find(part => part.type === 'currency')?.value ?? currency;
}

/** An amount as a box shows it while it has the focus: every digit it holds, ungrouped, with the culture's decimal sign (`1234,5`). */
export function plainAmountText(value: number, locale: string): string {
  return String(value).replace('.', signsOf(locale).decimal);
}

/**
 * The number a user typed: spaces dropped, the culture's minus or `-` before it, the culture's group sign only between
 * groups of three digits, and before a fraction of at most `digits` digits, two when it declares none, the culture's
 * decimal sign or `.` where the culture's group sign is not `.`; where it is (`de-DE`), `.` is a group sign, so
 * `12.50` there is text it cannot read, never `1250`. Null for no text and `NaN` for text that is no such number.
 * Never rounded: a longer fraction is text it cannot read.
 */
export function parseAmount(text: string, locale: string, digits?: number | null): number | null {
  const { group, decimal, minus } = signsOf(locale);
  const compact = text.replace(/\s/g, '').replace(minus, '-');
  if (compact === '') {
    return null;
  }
  const normal = compact.replace(/./gu, sign => normalSign(sign, group, decimal));
  const match = AMOUNT.exec(normal);
  const isAmount = match !== null && (match[1] ?? '').length <= (declaredDigits(digits) ?? DEFAULT_DIGITS);
  return isAmount ? Number(normal.replace(/ /g, '')) : Number.NaN;
}

/** A typed sign as `AMOUNT` reads it: the culture's decimal sign as `.`, its group sign as a space, any other as it is. */
function normalSign(sign: string, group: string, decimal: string): string {
  if (sign === decimal) {
    return '.';
  }
  return sign === group ? ' ' : sign;
}

/** The fraction digits: exactly the declared ones; without them exactly two for money and at most two for a percent. */
function digitOptions({ kind, digits }: AmountFormat): Intl.NumberFormatOptions {
  const declared = declaredDigits(digits);
  if (declared !== null) {
    return { minimumFractionDigits: declared, maximumFractionDigits: declared };
  }
  return kind === 'money'
    ? { minimumFractionDigits: DEFAULT_DIGITS, maximumFractionDigits: DEFAULT_DIGITS }
    : { maximumFractionDigits: DEFAULT_DIGITS };
}

/** The unit an amount is written with: its currency, the percent sign over the number as stored (never multiplied), or none. */
function unitOptions({ kind, currency }: AmountFormat): Intl.NumberFormatOptions {
  if (kind === 'percent') {
    return { style: 'unit', unit: 'percent' };
  }
  return currency ? { style: 'currency', currency } : {};
}

function formatOf(locale: string, options: Intl.NumberFormatOptions): Intl.NumberFormat {
  const key = `${locale}|${JSON.stringify(options)}`;
  let format = FORMATS.get(key);
  if (!format) {
    format = new Intl.NumberFormat(locale, options);
    FORMATS.set(key, format);
  }
  return format;
}

function signsOf(locale: string): NumberSigns {
  let signs = SIGNS.get(locale);
  if (!signs) {
    const parts = formatOf(locale, {}).formatToParts(-12345.6);
    const sign = (type: Intl.NumberFormatPartTypes, fallback: string) => parts.find(part => part.type === type)?.value ?? fallback;
    signs = { group: sign('group', ','), decimal: sign('decimal', '.'), minus: sign('minusSign', '-') };
    SIGNS.set(locale, signs);
  }
  return signs;
}
