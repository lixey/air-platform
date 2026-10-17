import { Component, computed, inject, input, linkedSignal, untracked } from '@angular/core';
import { LocaleService } from '../../app/locale.service';
import { BtnComponent } from '../../btn/btn.component';
import type { GridColumnDto, OverlayModelDto } from '../../core/generated/contracts';
import { TextsService, type UiTextKey } from '../../core/texts.service';
import { todayIn } from '../../core/time-zone';
import type { IsoDate } from '../../datepicker/calendar-month';
import { weekStart } from '../../datepicker/date-text';
import { TemporalBoxComponent } from '../../datepicker/temporal-box.component';
import { OverlayRef } from '../../overlay/overlay-ref';
import { singlePanelLayout } from '../../panels/single-panel-layout';
import { FOCUS_RING } from '../../theme/css-classes';
import type { GridDateRangeOverlayModel } from '../../ui-components';
import { columnLabelOf } from '../column/grid-column-label';
import { temporalTypeOf } from '../column/grid-column-temporal-type';
import { GridService } from '../grid.service';
import { DATE_PERIODS, type DatePeriod, type DateRange, periodRange, rangeFilter, rangeOfFilter } from './grid-date-periods';

/** The key of the date range menu's layout, and the stem of its overlay's key. */
const GRID_DATE_RANGE_OVERLAY = 'grid.dateRange';

/** The text of each period, a quarter's with its number. */
const PERIOD_TEXTS = {
  today: ['Today'],
  thisWeek: ['PeriodThisWeek'],
  thisMonth: ['PeriodThisMonth'],
  previousMonth: ['PeriodPreviousMonth'],
  nextMonth: ['PeriodNextMonth'],
  quarter1: ['PeriodQuarter', 1],
  quarter2: ['PeriodQuarter', 2],
  quarter3: ['PeriodQuarter', 3],
  quarter4: ['PeriodQuarter', 4],
  previousYear: ['PeriodPreviousYear'],
  thisYear: ['PeriodThisYear'],
  nextYear: ['PeriodNextYear'],
} satisfies Record<DatePeriod, readonly [UiTextKey, ...number[]]>;

/** The row of the menu each period stands in: 0 the days, weeks and months around today, 1 the quarters, 2 the years. */
const PERIOD_ROW = {
  today: 0,
  thisWeek: 0,
  thisMonth: 0,
  previousMonth: 0,
  nextMonth: 0,
  quarter1: 1,
  quarter2: 1,
  quarter3: 1,
  quarter4: 1,
  previousYear: 2,
  thisYear: 2,
  nextYear: 2,
} satisfies Record<DatePeriod, 0 | 1 | 2>;

/** A period's button: a quiet field that reads as a link, ringed in `field-hover` under the pointer. */
const PERIOD_BUTTON = `h-7 px-2 rounded-md border border-field-edge bg-field text-sm text-link hover:border-field-hover hover:text-link-hover cursor-pointer ${FOCUS_RING}`;

/** A menu hanging from a date header, whose one panel is the column's date range menu. */
export function gridDateRangeOverlay(column: GridColumnDto): OverlayModelDto {
  return {
    key: `${GRID_DATE_RANGE_OVERLAY}.${column.path ?? ''}`,
    presentation: 'menu',
    layout: singlePanelLayout(GRID_DATE_RANGE_OVERLAY, { kind: 'GridDateRangeOverlay', column }),
  };
}

/**
 * A date column's range, opened as a menu from its date header: From and To in the temporal control, and the periods
 * around today — today, this week, this, the previous and the next month, the quarters, the previous, this and the next
 * year. A period is resolved when it is picked, from today on the user's clock and the first day of the locale's week,
 * and applies at once; Apply writes the From and To typed, Clear removes the column's filter. The range is written by
 * the column's temporal type (`grid-date-periods.ts`); the menu opens on the range the column holds, empty for another
 * filter.
 */
@Component({
  selector: 'ui-grid-date-range-overlay',
  imports: [BtnComponent, TemporalBoxComponent],
  template: `
    <div class="flex flex-col gap-3 p-3 w-80">
      <h2 class="m-0 text-sm font-semibold truncate">{{ label() }}</h2>

      <div class="grid grid-cols-2 gap-2">
        <div class="flex flex-col gap-1 min-w-0">
          <label class="text-xs text-muted" [for]="fromId">{{ texts.of('PeriodFrom') }}</label>
          <ui-temporal-box [id]="fromId" kind="date" temporalType="dateOnly" [value]="from()" (valueChange)="from.set($event)" />
        </div>
        <div class="flex flex-col gap-1 min-w-0">
          <label class="text-xs text-muted" [for]="toId">{{ texts.of('PeriodTo') }}</label>
          <ui-temporal-box [id]="toId" kind="date" temporalType="dateOnly" [value]="to()" (valueChange)="to.set($event)" />
        </div>
      </div>

      <div class="flex flex-col gap-1.5" role="group" [attr.aria-labelledby]="periodsId">
        <h3 [id]="periodsId" class="m-0 text-xs text-muted">{{ texts.of('Periods') }}</h3>
        @for (row of periodRows(); track $index) {
          <div class="flex flex-wrap gap-1">
            @for (period of row; track period.period) {
              <button type="button" class="${PERIOD_BUTTON}" (click)="pick(period.period)">{{ period.text }}</button>
            }
          </div>
        }
      </div>

      <div class="flex justify-end gap-2 pt-1">
        <ui-btn [label]="texts.of('Clear')" variant="ghost" (click)="clear()" />
        <ui-btn [label]="texts.of('Apply')" variant="solid" (click)="apply()" />
      </div>
    </div>
  `,
  host: { class: 'block' },
})
export class GridDateRangeOverlayComponent {
  private static next = 0;

  protected readonly texts = inject(TextsService);
  private readonly locale = inject(LocaleService).current;
  private readonly zone = inject(LocaleService).zone;
  private readonly grid = inject(GridService, { optional: true });
  private readonly ref = inject(OverlayRef, { optional: true });

  readonly model = input<GridDateRangeOverlayModel | null>();

  protected readonly column = computed(() => this.model()?.column ?? null);
  protected readonly label = computed(() => {
    const column = this.column();
    return column ? columnLabelOf(column) : '';
  });
  protected readonly periodRows = computed(() =>
    ([0, 1, 2] as const).map(row =>
      DATE_PERIODS.filter(period => PERIOD_ROW[period] === row).map(period => {
        const [key, ...args] = PERIOD_TEXTS[period];
        return { period, text: this.texts.of(key, ...args) };
      }),
    ),
  );

  /** The column's range as the menu opens on it: read again only for another model, never after the grid's filters change. */
  private readonly opening = computed<DateRange | null>(() => {
    const column = this.column();
    const filter = untracked(() => this.grid?.columnFilterOf(column?.path));
    return column ? rangeOfFilter(filter, temporalTypeOf(column), untracked(this.zone)) : null;
  });

  protected readonly from = linkedSignal<IsoDate | null>(() => this.opening()?.from ?? null);
  protected readonly to = linkedSignal<IsoDate | null>(() => this.opening()?.to ?? null);

  private readonly id = ++GridDateRangeOverlayComponent.next;
  protected readonly fromId = `ui-grid-date-range-from-${this.id}`;
  protected readonly toId = `ui-grid-date-range-to-${this.id}`;
  protected readonly periodsId = `ui-grid-date-range-periods-${this.id}`;

  /** Applies the period's days around today on the user's clock. */
  protected pick(period: DatePeriod): void {
    const range = periodRange(period, todayIn(this.zone()), weekStart(this.locale()));
    this.write(range);
  }

  protected apply(): void {
    this.write({ from: this.from(), to: this.to() });
  }

  protected clear(): void {
    this.write({ from: null, to: null });
  }

  private write(range: DateRange): void {
    const column = this.column();
    if (column?.path && this.grid) {
      const filter = rangeFilter(column.path, range, temporalTypeOf(column), this.zone());
      if (filter) {
        this.grid.setColumnFilter(filter);
      } else {
        this.grid.clearColumnFilter(column.path);
      }
    }
    this.ref?.dismiss();
  }
}
