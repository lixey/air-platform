/**
 * The library's zone arithmetic over `Intl`: the wall clock of an instant in a zone, the instant of a wall clock in a
 * zone and today in a zone, each named after the Temporal operation it stands for, so the day Temporal is everywhere
 * this one file gives way to it. A zone is its IANA id; one the browser does not know is read as UTC and nothing throws.
 */

/** The zone of the end of every chain, and of a zone the browser does not know. */
const UTC = 'UTC';

const DAY_MS = 86_400_000;

/** What a zone's clock reads, without a zone: Temporal's `PlainDateTime`, its month from 1 to 12. */
export interface WallClock {
  readonly year: number;
  readonly month: number;
  readonly day: number;
  readonly hour: number;
  readonly minute: number;
  readonly second: number;
  readonly millisecond: number;
}

function formatIn(zone: string): Intl.DateTimeFormat {
  return new Intl.DateTimeFormat('en-US', {
    timeZone: zone,
    hourCycle: 'h23',
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    hour: 'numeric',
    minute: 'numeric',
    second: 'numeric',
  });
}

const UTC_FORMAT = formatIn(UTC);

/** One formatter per zone the browser knows, `null` for one it does not; building one costs far more than using one. */
const FORMATS = new Map<string, Intl.DateTimeFormat | null>([[UTC, UTC_FORMAT]]);

function formatOf(zone: string): Intl.DateTimeFormat | null {
  let format = FORMATS.get(zone);
  if (format === undefined) {
    try {
      format = formatIn(zone);
    } catch {
      // Intl refuses a zone it does not know with a RangeError: the zone reads as UTC
      format = null;
    }
    FORMATS.set(zone, format);
  }
  return format;
}

/** The zone the browser stands in, Temporal's `Now.timeZoneId()`; UTC when it names none the browser knows. */
export function browserZone(): string {
  return knownZone(new Intl.DateTimeFormat().resolvedOptions().timeZone);
}

/** `zone` when the browser knows it, as it was written (`Europe/Kyiv` stays itself where ICU says `Europe/Kiev`); UTC otherwise. */
export function knownZone(zone: string | null | undefined): string {
  return zone && formatOf(zone) ? zone : UTC;
}

/**
 * The wall clock of `instant` in `zone`, Temporal's `Instant.toZonedDateTimeISO(zone).toPlainDateTime()`; a zone the
 * browser does not know reads as UTC.
 */
export function wallOf(instant: Date | number, zone: string): WallClock {
  const at = typeof instant === 'number' ? instant : instant.getTime();
  const parts: Record<string, number> = {};
  for (const part of (formatOf(zone) ?? UTC_FORMAT).formatToParts(at)) {
    if (part.type !== 'literal') {
      parts[part.type] = Number(part.value);
    }
  }
  return {
    year: parts['year'],
    month: parts['month'],
    day: parts['day'],
    hour: parts['hour'],
    minute: parts['minute'],
    second: parts['second'],
    millisecond: ((at % 1000) + 1000) % 1000,
  };
}

/**
 * The instant of `wall` in `zone`, Temporal's `PlainDateTime.toZonedDateTime(zone, { disambiguation: 'compatible' })`:
 * the wall read at the zone's offset a day before and a day after it, the two passes; a wall both offsets reach, which
 * the clock repeats, is the earlier instant, and a wall neither reaches, which the clock skips, moves forward by the gap
 * (read at the offset before it).
 */
export function instantOf(wall: WallClock, zone: string): Date {
  const local = utcOf(wall);
  const before = local - offsetAt(local - DAY_MS, zone);
  const after = local - offsetAt(local + DAY_MS, zone);
  const reached = [before, after].filter(candidate => local - candidate === offsetAt(candidate, zone));
  return new Date(reached.length ? Math.min(...reached) : before);
}

/** The date of `now` on the clock of `zone` as `yyyy-MM-dd`, Temporal's `Now.plainDateISO(zone)`. */
export function todayIn(zone: string, now: Date | number = Date.now()): string {
  const { year, month, day } = wallOf(now, zone);
  return `${String(year).padStart(4, '0')}-${two(month)}-${two(day)}`;
}

/** The offset of `zone` at the instant `at`, in milliseconds ahead of UTC. */
function offsetAt(at: number, zone: string): number {
  return utcOf(wallOf(at, zone)) - at;
}

/** The instant whose UTC clock reads `wall`; `setUTCFullYear` keeps a year below 100 as written. */
function utcOf(wall: WallClock): number {
  const moment = new Date(0);
  moment.setUTCFullYear(wall.year, wall.month - 1, wall.day);
  moment.setUTCHours(wall.hour, wall.minute, wall.second, wall.millisecond);
  return moment.getTime();
}

function two(value: number): string {
  return String(value).padStart(2, '0');
}
