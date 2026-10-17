import { monthOf, pad2, yearOf } from '../controls/temporal-text';
import {
  addMonths,
  addYears,
  dateParts,
  type IsoDate,
  monthWeeks,
  PAGE_COLUMNS,
  utcMidnight,
  type Weekday,
  yearPage,
} from './calendar-month';
import type { PickerView } from './date-picker.service';

/**
 * A cell of the picker's grid as the template draws it: a day, a month, a year, an hour or a minute, keyed by what it
 * stands for — the date of a day, a month or a year, the number of an hour or a minute.
 */
export interface PickerCell {
  readonly key: string;
  readonly label: string;
  readonly isActive: boolean;
  readonly isSelected: boolean;
  readonly isToday: boolean;
  readonly classes: string;
}

/** A row of the grid: its cells, and for a week of days its ISO 8601 number. */
export interface PickerRow {
  readonly cells: readonly PickerCell[];
  readonly week: number | null;
}

/** The day the draft holds, `null` for none, and today: what marks a cell as the value and as current. */
export interface PickerMarks {
  readonly day: IsoDate | null;
  readonly today: IsoDate;
}

/** The locale's week as the days are drawn: its first day and the days of its weekend. */
export interface PickerWeek {
  readonly start: Weekday;
  readonly weekend: ReadonlySet<Weekday>;
}

/** The cell the keys of a time's grid stand on, and the one the draft holds, `null` for none. */
export interface PickerTime {
  readonly active: number;
  readonly selected: number | null;
}

/**
 * Every cell of every view: bordered, the borders shared (`border-collapse`); 40 px tall, the columns sharing the
 * card's width (`table-fixed`): seven days of 40 beside the week column's 30, three months or years, six hours or
 * minutes. The keyboard's cell carries the fuchsia border of a focused field inside its edge, 2 px over a 1 px line of
 * the surface, so it reads on every state, the value's blue too, and the card's edge never clips it; it takes the place
 * of today's outline while it stands there.
 */
const CELL =
  'relative h-10 border border-line text-center text-sm tabular-nums cursor-pointer select-none outline-none ' +
  'focus:inset-ring-2 focus:inset-ring-field-focus focus:ring-3 focus:ring-inset focus:ring-surface';

/** What a view adds to its cells: the months' names are words. */
const CELL_VIEWS = {
  days: '',
  months: 'px-1 capitalize truncate',
  years: '',
  hours: '',
  minutes: '',
} satisfies Record<PickerView, string>;

/**
 * The states of a cell, each with a hover of its own: the value, today, a day of another month, a weekend day, any other.
 * A hovered cell takes the grid cell's hover edge and today its orange edge, each drawn as the cell's own border — a
 * box one pixel beyond the cell, over the line it shares with its neighbours, the cell raised above them — never a second
 * line inside it; the keyboard's mark takes their place while the cell has focus. A cell's own ring would not do: in the
 * collapsed table its neighbours and the grid's lines paint over it.
 */
const HOVER_EDGE =
  'not-focus:hover:z-2 not-focus:hover:after:pointer-events-none not-focus:hover:after:absolute not-focus:hover:after:-inset-px ' +
  'not-focus:hover:after:border not-focus:hover:after:border-cell-ring';
const VALUE = 'bg-day-value text-on-day-value hover:bg-day-value-hover';
const TODAY_EDGE =
  'z-1 font-bold not-focus:after:pointer-events-none not-focus:after:absolute not-focus:after:-inset-px not-focus:after:border ' +
  'not-focus:after:border-day-today-edge';
const TODAY = 'bg-day-today-ground text-day-today hover:bg-day-today-hover';
const OUTSIDE = 'text-day-outside hover:bg-day-hover';
const WEEKEND = 'text-day-weekend hover:bg-day-weekend-hover';
const PLAIN = 'text-day hover:bg-day-hover';

/** The hours and the minutes stand six to a row: the hours are four rows of six. */
const TIME_COLUMNS = 6;
export const HOURS = Array.from({ length: 24 }, (_, hour) => hour);

/** Where the keys of the hours or of the minutes move their active cell, from its index and the last one's. */
const TIME_MOVES = new Map<string, (index: number, last: number) => number>([
  ['ArrowLeft', index => Math.max(index - 1, 0)],
  ['ArrowRight', (index, last) => Math.min(index + 1, last)],
  ['ArrowUp', index => (index >= TIME_COLUMNS ? index - TIME_COLUMNS : index)],
  ['ArrowDown', (index, last) => (index + TIME_COLUMNS <= last ? index + TIME_COLUMNS : index)],
  ['Home', () => 0],
  ['End', (_, last) => last],
]);

/** The days of the month of `active`, six weeks from the week's first day, each week with its number. */
export function dayRows(active: IsoDate, marks: PickerMarks, week: PickerWeek): readonly PickerRow[] {
  return monthWeeks(active, week.start).map(({ days, week: number }) => ({
    week: number,
    cells: days.map(cell =>
      gridCell(cell.date, String(cell.day), 'days', {
        isActive: cell.date === active,
        isSelected: cell.date === marks.day,
        isToday: cell.date === marks.today,
        isOutside: !cell.inMonth,
        isWeekend: week.weekend.has(utcMidnight(cell.date).getUTCDay() as Weekday),
      }),
    ),
  }));
}

/** The months of the year of `active` under their `names`, each keyed by the day of `active` in it. */
export function monthRows(active: IsoDate, marks: PickerMarks, names: readonly string[]): readonly PickerRow[] {
  const { month } = dateParts(active);
  const picked = marks.day === null ? null : monthOf(marks.day);
  const current = monthOf(marks.today);
  const cells = names.map((name, index) => {
    const date = addMonths(active, index + 1 - month);
    const each = monthOf(date);
    return gridCell(date, name, 'months', { isActive: date === active, isSelected: each === picked, isToday: each === current });
  });
  return rowsOf(cells, PAGE_COLUMNS);
}

/** The years of the page of `active`, each keyed by the day of `active` in it. */
export function yearRows(active: IsoDate, marks: PickerMarks): readonly PickerRow[] {
  const { year } = dateParts(active);
  const picked = marks.day === null ? null : yearOf(marks.day);
  const current = yearOf(marks.today);
  const cells = yearPage(year).map(each => {
    const date = addYears(active, each - year);
    const text = yearOf(date);
    return gridCell(date, String(each), 'years', { isActive: date === active, isSelected: text === picked, isToday: text === current });
  });
  return rowsOf(cells, PAGE_COLUMNS);
}

/** The hours or the minutes among `values`, each keyed by its number and written in two digits. */
export function timeRows(view: 'hours' | 'minutes', values: readonly number[], time: PickerTime): readonly PickerRow[] {
  const cells = values.map(value =>
    gridCell(String(value), pad2(value), view, { isActive: value === time.active, isSelected: value === time.selected }),
  );
  return rowsOf(cells, TIME_COLUMNS);
}

/**
 * The index a key of the hours or of the minutes moves the active cell
 * to, from its `index` and the `last` one; `null` for any other key.
 */
export function timeMove(key: string, index: number, last: number): number | null {
  return TIME_MOVES.get(key)?.(index, last) ?? null;
}

/**
 * A cell and its classes: the value filled, and outlined too when it is today; today outlined with its text, keeping
 * its orange edge under the pointer while any other cell takes the hover edge; a day of
 * another month pale, even on a weekend; a weekend day red; any other plain. The month and the year of today are today
 * among the months and the years; the hours and the minutes are only the value or plain.
 */
function gridCell(
  key: string,
  label: string,
  view: PickerView,
  state: { isActive: boolean; isSelected: boolean; isToday?: boolean; isOutside?: boolean; isWeekend?: boolean },
): PickerCell {
  const { isActive, isSelected, isToday = false, isOutside = false, isWeekend = false } = state;
  const states: readonly (readonly [boolean, string])[] = [
    [isSelected, VALUE],
    [isToday, TODAY],
    [isOutside, OUTSIDE],
    [isWeekend, WEEKEND],
  ];
  const look = states.find(([is]) => is)?.[1] ?? PLAIN;
  const classes = [CELL, CELL_VIEWS[view], look, isToday ? TODAY_EDGE : HOVER_EDGE];
  return { key, label, isActive, isSelected, isToday, classes: classes.filter(Boolean).join(' ') };
}

/** The cells of the months, the years, the hours or the minutes in rows of `columns`, none of them a week. */
function rowsOf(cells: readonly PickerCell[], columns: number): readonly PickerRow[] {
  return Array.from({ length: Math.ceil(cells.length / columns) }, (_, row) => ({
    cells: cells.slice(row * columns, (row + 1) * columns),
    week: null,
  }));
}
