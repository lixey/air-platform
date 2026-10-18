/**
 * How a file's size is written in the session's culture: in bytes, kilobytes or megabytes with the culture's own unit,
 * and in bare megabytes where a text names the unit itself. One `Intl` formatter per culture and unit, kept for the
 * life of the page. A kilobyte is 1024 bytes, as the server counts a size limit.
 */

/** Bytes in a kilobyte and in a megabyte, as the server's size refusal counts them. */
const BYTES_PER_KILOBYTE = 1024;
const BYTES_PER_MEGABYTE = BYTES_PER_KILOBYTE * BYTES_PER_KILOBYTE;

type SizeUnit = 'byte' | 'kilobyte' | 'megabyte' | 'number';

const FORMATS = new Map<string, Intl.NumberFormat>();

function formatOf(locale: string, unit: SizeUnit): Intl.NumberFormat {
  const key = `${locale}|${unit}`;
  let format = FORMATS.get(key);
  if (!format) {
    const digits = unit === 'byte' ? 0 : 1;
    format = new Intl.NumberFormat(
      locale,
      unit === 'number'
        ? { maximumFractionDigits: digits }
        : // A byte is written in full, `512 bytes`: its short form is the bare singular in some cultures
          { style: 'unit', unit, unitDisplay: unit === 'byte' ? 'long' : 'short', maximumFractionDigits: digits },
    );
    FORMATS.set(key, format);
  }
  return format;
}

/**
 * A size in bytes with its unit, in the culture's words: bytes below a kilobyte, kilobytes below a megabyte, megabytes
 * above, with one fraction digit at most (`512 bytes`, `84 kB`, `1.2 MB`, `10 МБ`).
 */
export function formatBytes(bytes: number, locale: string): string {
  if (bytes < BYTES_PER_KILOBYTE) {
    return formatOf(locale, 'byte').format(bytes);
  }
  if (bytes < BYTES_PER_MEGABYTE) {
    return formatOf(locale, 'kilobyte').format(bytes / BYTES_PER_KILOBYTE);
  }
  return formatOf(locale, 'megabyte').format(bytes / BYTES_PER_MEGABYTE);
}

/** A size in bytes as megabytes of the locale, with one fraction digit at most, as the server's size refusal names it. */
export function formatMegabytes(bytes: number, locale: string): string {
  return formatOf(locale, 'number').format(bytes / BYTES_PER_MEGABYTE);
}
