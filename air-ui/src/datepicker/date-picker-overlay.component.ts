import { afterNextRender, Component, computed, ElementRef, inject, Injector, linkedSignal } from '@angular/core';
import { LocaleService } from '../app/locale.service';
import { BtnComponent } from '../btn/btn.component';
import { RECORD_AUTOCOMPLETE, RECORD_NAME } from '../controls/record-autocomplete';
import { pad2 } from '../controls/temporal-text';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { NavComponent } from '../nav/nav.component';
import { fieldClasses, SEGMENT, SEGMENT_FIRST, SEGMENT_LAST, SEGMENTED } from '../theme/css-classes';
import { addMonths, addYears, calendarMove, dateParts, yearPage, YEARS_PER_PAGE } from './calendar-month';
import {
  dayRows,
  HOURS,
  monthRows,
  type PickerMarks,
  type PickerRow,
  type PickerTime,
  timeMove,
  timeRows,
  yearRows,
} from './date-picker-cells';
import { DatePickerService, type PickerView } from './date-picker.service';
import { formatTemporalText, monthNames, monthTitle, weekdayNames, weekendDays, weekStart } from './date-text';

/**
 * The card's width, fixed at the days grid's 310 px (seven days of 40 and the week column of 30): its intrinsic width
 * is this whatever its content, so the menu card sized to its content is as wide as the grid and no wider, and the
 * grid's percentage width cannot stretch it to the window. The card drops the menu's floor of the anchor's width
 * (`styles.css`), so a day is never wider than 40; on a narrow window it shrinks with the card, centred where the card
 * fills the window.
 */
const CARD_WIDTH = 'w-77.5 max-w-full mx-auto';

/**
 * The grid, flush with the card's sides: in the collapsed model the table's hidden border wins over every cell border on
 * its edge, so the card's own border is the one line at its sides, the steppers' (or the bar's) bottom line the one
 * above it and the footer's top line the one below; the lines between the cells stay.
 */
const GRID = 'w-full table-fixed border-collapse border-hidden';

/**
 * The input is as wide as the widest text of its kind in the locale, every part two digits or more (`28.12.2000 22:22`,
 * `22:22`), counted in `ch`: a tabular digit is one, a separator (a point, a colon, a space, a slash) under half of one,
 * and the allowance of its padding, its border and the caret; the field's label beside it takes the rest of the bar.
 */
const INPUT_SAMPLES = { date: '2000-12-28', datetime: '2000-12-28T22:22', time: '22:22' } as const;
const SEPARATOR_WIDTH = 0.45;
const INPUT_ALLOWANCE = 2.2;

/** The width in `ch` of a text of tabular digits and narrow separators. */
function textWidth(text: string): number {
  return [...text].reduce((width, char) => width + (/\d/.test(char) ? 1 : SEPARATOR_WIDTH), 0);
}

/**
 * A stepper, a segmented control (`SEGMENTED`): a narrow previous and next button around the one naming the month or
 * the year, which takes the rest; the arrows round the box's outer corners, the name is square. The year's stepper is
 * as wide as its year, the month's takes the rest of the row, so the longest month name fits.
 */
const STEPPER = SEGMENTED;
const STEP_PREVIOUS = `${SEGMENT} flex-none w-7 ${SEGMENT_FIRST}`;
const STEP_NEXT = `${SEGMENT} flex-none w-7 ${SEGMENT_LAST}`;
const STEP_NAME = `${SEGMENT} min-w-0 px-1.5`;

/**
 * The body of the date picker's overlay, the kind `DatePickerOverlay`, one card as wide as its days grid: a bar on the
 * card's own ground, as the select's, with the input the value is typed into, which takes the first focus with its text
 * selected, the field's label and a close button; for a date the month and the year steppers, each a previous and a
 * next button around the name that opens the months or the years, twelve to a page, in the same card; the days of a
 * month as a date grid flush with the card's sides, each week ending with its ISO 8601 number, each state of a day with
 * a hover of its own; for a date and time a row with the hours and the minutes that switches the card to the
 * hours, four rows of six, and from an hour picked to the minutes; a time of day opens on the hours. Then `Today` or
 * `Now`, `Clear` where the control may be emptied, and `Close`. What each pick does is the picker service's; this draws
 * it, moves the grid's one tab stop by the keys, and brings focus to the cell the keys or a new view put it on.
 */
@Component({
  selector: 'ui-date-picker-overlay',
  imports: [BtnComponent, NavComponent, UiIconDirective],
  template: `
    <ui-nav class="shrink-0 border-b border-line bg-surface">
      <input
        #input
        start
        type="text"
        autocomplete="${RECORD_AUTOCOMPLETE}"
        name="${RECORD_NAME}"
        [class]="inputClasses()"
        [style.width.ch]="inputWidth()"
        [value]="picker.text()"
        [placeholder]="placeholder()"
        [attr.aria-label]="picker.name()"
        [attr.aria-invalid]="picker.isInvalid() || null"
        (input)="picker.typeText(input.value)"
        (focus)="selectText(input)"
        (keydown.enter)="enterText($event)"
      />
      <span class="min-w-0 truncate text-sm text-muted">{{ picker.label() }}</span>
      <ui-btn end icon="close" variant="ghost" color="slate" [title]="texts.of('Close')" (click)="picker.close()" />
    </ui-nav>
    @if (isCalendar()) {
      <div class="flex gap-1 p-1 border-b border-line">
        <div class="flex-1 min-w-0 ${STEPPER}">
          <button
            type="button"
            class="${STEP_PREVIOUS}"
            [title]="texts.of('PreviousMonth')"
            [attr.aria-label]="texts.of('PreviousMonth')"
            (click)="stepMonth(-1)"
          >
            <i icon="chevron-left" aria-hidden="true"></i>
          </button>
          <button type="button" class="flex-1 capitalize ${STEP_NAME}" [title]="texts.of('ChooseMonth')" (click)="show('months')">
            <span class="min-w-0 truncate">{{ monthName() }}</span>
          </button>
          <button
            type="button"
            class="${STEP_NEXT}"
            [title]="texts.of('NextMonth')"
            [attr.aria-label]="texts.of('NextMonth')"
            (click)="stepMonth(1)"
          >
            <i icon="chevron-right" aria-hidden="true"></i>
          </button>
        </div>
        <div class="flex-none ${STEPPER}">
          <button
            type="button"
            class="${STEP_PREVIOUS}"
            [title]="previousYearTitle()"
            [attr.aria-label]="previousYearTitle()"
            (click)="stepYear(-1)"
          >
            <i icon="chevron-left" aria-hidden="true"></i>
          </button>
          <button type="button" class="tabular-nums ${STEP_NAME}" [title]="texts.of('ChooseYear')" (click)="show('years')">
            {{ yearText() }}
          </button>
          <button type="button" class="${STEP_NEXT}" [title]="nextYearTitle()" [attr.aria-label]="nextYearTitle()" (click)="stepYear(1)">
            <i icon="chevron-right" aria-hidden="true"></i>
          </button>
        </div>
      </div>
    } @else if (picker.kind() === 'datetime') {
      <div class="flex p-1 border-b border-line">
        <ui-btn
          variant="soft"
          color="blue"
          class="flex-1 justify-center tabular-nums"
          [label]="dateText()"
          [title]="texts.of('ChooseDate')"
          (click)="show('days')"
        />
      </div>
    }
    <table role="grid" class="${GRID}" [attr.aria-label]="gridName()">
      @if (picker.view() === 'days') {
        <thead>
          <tr>
            @for (weekday of weekdays(); track $index) {
              <th scope="col" class="h-7 text-xs font-semibold text-ink capitalize" [attr.abbr]="weekday.long">{{ weekday.short }}</th>
            }
            <th scope="col" class="w-7.5 h-7 text-xs font-semibold text-muted" [attr.abbr]="texts.of('Week')">#</th>
          </tr>
        </thead>
      }
      <tbody>
        @for (row of rows(); track $index) {
          <tr>
            @for (cell of row.cells; track cell.key) {
              <td
                [class]="cell.classes"
                [attr.tabindex]="cell.isActive ? 0 : -1"
                [attr.aria-selected]="cell.isSelected"
                [attr.aria-current]="cell.isToday ? 'date' : null"
                (click)="choose(cell.key)"
                (keydown)="onGridKeyDown($event)"
              >
                {{ cell.label }}
              </td>
            }
            @if (row.week !== null) {
              <th scope="row" class="h-10 border border-line bg-surface-1 text-xs font-normal text-muted tabular-nums">
                {{ row.week }}
              </th>
            }
          </tr>
        }
      </tbody>
    </table>
    <div class="border-t border-line">
      @if (showsTime()) {
        <div class="flex items-center justify-center gap-1 px-1 pt-1">
          <ui-btn
            color="blue"
            class="w-12 justify-center tabular-nums"
            [variant]="picker.view() === 'hours' ? 'solid' : 'soft'"
            [label]="timeText().hour"
            [title]="texts.of('Hours')"
            (click)="show('hours')"
          />
          <span class="text-sm font-semibold text-muted" aria-hidden="true">:</span>
          <ui-btn
            color="blue"
            class="w-12 justify-center tabular-nums"
            [variant]="picker.view() === 'minutes' ? 'solid' : 'soft'"
            [label]="timeText().minute"
            [title]="texts.of('Minutes')"
            (click)="show('minutes')"
          />
        </div>
      }
      <div class="flex items-center gap-1 p-1">
        <ui-btn variant="soft" color="blue" [label]="nowLabel()" (click)="picker.pickNow()" />
        @if (picker.request().clearable) {
          <ui-btn variant="ghost" color="slate" [label]="texts.of('Clear')" (click)="picker.clear()" />
        }
        <ui-btn variant="soft" color="slate" class="ml-auto" [label]="texts.of('Close')" (click)="picker.close()" />
      </div>
    </div>
  `,
  host: { class: `flex flex-col ${CARD_WIDTH}` },
})
export class DatePickerOverlayComponent {
  protected readonly texts = inject(TextsService);
  protected readonly picker = inject(DatePickerService);
  private readonly locale = inject(LocaleService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly injector = inject(Injector);

  protected readonly inputClasses = computed(() => `${fieldClasses(this.picker.isInvalid())} tabular-nums`);

  /** The input's width in `ch`: the locale's widest text of the kind and the allowance around it. */
  protected readonly inputWidth = computed(() => {
    const kind = this.picker.kind();
    return Math.round((textWidth(formatTemporalText(INPUT_SAMPLES[kind], kind, this.locale.current())) + INPUT_ALLOWANCE) * 10) / 10;
  });

  /** Today at midnight as the input would show it, while it is empty: an example of the locale's order of the parts. */
  protected readonly placeholder = computed(() => {
    const kind = this.picker.kind();
    const today = kind === 'time' ? '00:00' : `${this.picker.today()}T00:00`;
    return formatTemporalText(today, kind, this.locale.current());
  });

  protected readonly nowLabel = computed(() => this.texts.of(this.picker.kind() === 'date' ? 'Today' : 'Now'));

  /** Whether the card shows a view of the calendar, with its steppers, rather than the hours or the minutes. */
  protected readonly isCalendar = computed(() => !isTimeView(this.picker.view()));

  /** Whether the card shows the row of the hours and the minutes: under the days of a date and time, and over the time's grids. */
  protected readonly showsTime = computed(() => {
    const view = this.picker.view();
    return this.picker.kind() !== 'date' && (view === 'days' || isTimeView(view));
  });

  private readonly weekStart = computed(() => weekStart(this.locale.current()));
  private readonly weekend = computed(() => weekendDays(this.locale.current()));

  /** The heads of the days' columns, short as drawn and long as read out. */
  protected readonly weekdays = computed(() => {
    const locale = this.locale.current();
    const long = weekdayNames(locale, 'long');
    return weekdayNames(locale, 'short').map((short, index) => ({ short, long: long[index] }));
  });

  protected readonly monthName = computed(() => monthNames(this.locale.current())[dateParts(this.picker.active()).month - 1]);
  protected readonly yearText = computed(() => String(dateParts(this.picker.active()).year));

  /** The year stepper moves by a year, and by a page among the years. */
  protected readonly previousYearTitle = computed(() => this.texts.of(this.picker.view() === 'years' ? 'PreviousYears' : 'PreviousYear'));
  protected readonly nextYearTitle = computed(() => this.texts.of(this.picker.view() === 'years' ? 'NextYears' : 'NextYear'));

  /** The day of a date and time, shown over its hours and minutes: the button that returns to the days. */
  protected readonly dateText = computed(() =>
    formatTemporalText(this.picker.day() ?? this.picker.active(), 'date', this.locale.current()),
  );

  /** The hours and the minutes of the draft, `--` for none. */
  protected readonly timeText = computed(() => ({ hour: twoDigits(this.picker.hour()), minute: twoDigits(this.picker.minute()) }));

  /**
   * The name of the grid: the month and year of the days, the year of the months, the first and last of the years, what
   * the hours and the minutes choose.
   */
  protected readonly gridName = computed(() => {
    const active = this.picker.active();
    const { year } = dateParts(active);
    const page = yearPage(year);
    const names = {
      days: () => monthTitle(active, this.locale.current()),
      months: () => String(year),
      years: () => `${page[0]} – ${page[page.length - 1]}`,
      hours: () => this.texts.of('ChooseHour'),
      minutes: () => this.texts.of('ChooseMinute'),
    } satisfies Record<PickerView, () => string>;
    return names[this.picker.view()]();
  });

  /** The day of the draft and today: what marks a cell of the calendar as the value and as current. */
  private readonly marks = computed<PickerMarks>(() => ({ day: this.picker.day(), today: this.picker.today() }));

  /** The hour or the minute the keys of the time's grids move: the one selected, else the first; each new view starts over. */
  private readonly timeActive = linkedSignal(() => (this.picker.view() === 'minutes' ? this.picker.minute() : this.picker.hour()) ?? 0);

  /** The rows of the grid in its view: six weeks of days, rows of three months or years, rows of six hours or minutes. */
  protected readonly rows = computed<readonly PickerRow[]>(() => {
    const view = this.picker.view();
    const active = this.picker.active();
    switch (view) {
      case 'days':
        return dayRows(active, this.marks(), { start: this.weekStart(), weekend: this.weekend() });
      case 'months':
        return monthRows(active, this.marks(), monthNames(this.locale.current()));
      case 'years':
        return yearRows(active, this.marks());
      default: {
        const time: PickerTime = { selected: view === 'hours' ? this.picker.hour() : this.picker.minute(), active: this.timeActive() };
        return timeRows(view, this.timeValues(view), time);
      }
    }
  });

  /** The input takes focus with the value's text selected, so typing replaces it; a text typed keeps its caret at the end. */
  protected selectText(input: HTMLInputElement): void {
    if (this.picker.isTyped()) {
      input.setSelectionRange(input.value.length, input.value.length);
    } else {
      input.select();
    }
  }

  protected enterText(event: Event): void {
    event.preventDefault();
    this.picker.enterText();
  }

  /** Opens a view in the card, focus on its active cell. */
  protected show(view: PickerView): void {
    this.picker.show(view);
    this.focusActive();
  }

  /** The month stepper: the previous or the next month. */
  protected stepMonth(direction: -1 | 1): void {
    this.picker.moveActive(addMonths(this.picker.active(), direction));
  }

  /** The year stepper: the previous or the next year, or page of the years. */
  protected stepYear(direction: -1 | 1): void {
    const years = this.picker.view() === 'years' ? YEARS_PER_PAGE : 1;
    this.picker.moveActive(addYears(this.picker.active(), direction * years));
  }

  /**
   * A cell picked: a day, an hour and a minute go to the picker, which moves a date and time on to the hours and the
   * minutes; a month or a year opens the days of the month it stands for.
   */
  protected choose(key: string): void {
    switch (this.picker.view()) {
      case 'days':
        this.picker.pickDay(key);
        break;
      case 'hours':
        this.picker.pickHour(Number(key));
        break;
      case 'minutes':
        this.picker.pickMinute(Number(key));
        return;
      default:
        this.picker.moveActive(key);
        this.picker.show('days');
    }
    if (this.picker.isOpen()) {
      this.focusActive();
    }
  }

  /**
   * The keys of the grid: `Enter` and `Space` choose the active cell; in the calendar the rest move it (`calendarMove`),
   * in the hours and the minutes the arrows move it by one and by a row, `Home` and `End` to the first and the last.
   */
  protected onGridKeyDown(event: KeyboardEvent): void {
    const view = this.picker.view();
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.choose(isTimeView(view) ? String(this.timeActive()) : this.picker.active());
      return;
    }
    if (isTimeView(view)) {
      const values = this.timeValues(view);
      const index = timeMove(event.key, values.indexOf(this.timeActive()), values.length - 1);
      if (index === null) {
        return;
      }
      event.preventDefault();
      this.timeActive.set(values[index]);
      this.focusActive();
      return;
    }
    const moved = calendarMove(view, event, this.picker.active(), this.weekStart());
    if (moved === null) {
      return;
    }
    event.preventDefault();
    this.picker.moveActive(moved);
    this.focusActive();
  }

  private timeValues(view: 'hours' | 'minutes'): readonly number[] {
    return view === 'hours' ? HOURS : this.picker.minutes();
  }

  /** Focus onto the active cell once the grid is drawn again, where the keys or a new view put it. */
  private focusActive(): void {
    afterNextRender(() => this.host.querySelector<HTMLElement>('[role=grid] [tabindex="0"]')?.focus(), { injector: this.injector });
  }
}

function isTimeView(view: PickerView): view is 'hours' | 'minutes' {
  return view === 'hours' || view === 'minutes';
}

function twoDigits(value: number | null): string {
  return value === null ? '--' : pad2(value);
}
