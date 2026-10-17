import { Component, computed, inject } from '@angular/core';
import type { GridHeaderCheckboxModel } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import { UiIconDirective } from '../../icon/ui-icon.directive';
import { GRID_HEADER_CLEAR, GRID_HEADER_HOST, GRID_HEADER_STRIP } from '../../theme/css-classes';
import { GridCellBase } from '../cell/grid-cell-base';
import { columnLabelOf } from '../column/grid-column-label';
import type { GridFilter } from '../filter/grid-filter-set';
import { rulesOf } from '../filter/grid-filter-set';
import { injectClauseText } from './grid-header-clause';
import { GridHeaderMenuDirective } from './grid-header-menu.directive';
import { GridHeaderSortComponent } from './grid-header-sort.component';

/** The column's filter as the label reads it: none, yes, no, or a clause the column's menu made. */
type FlagFilter = 'any' | 'yes' | 'no' | 'other';

/** The value the label's activation posts from each filter: yes after any or a clause, no after yes, none after no. */
const NEXT: Readonly<Record<FlagFilter, boolean | null>> = { any: true, other: true, yes: false, no: null };

/**
 * The header of a checkbox column, shaped as the date header on the table's `surface-2`: a button reading the label in
 * the `link` hue — its full text as its title — whose activation cycles the column's filter through any, yes and no,
 * posting `equals true`, then `equals false`, then removing it; "no" leaves out the rows with no value, which the menu's
 * `isNull` finds. While the column is filtered the button reads the label in the `mark` hue with a check (green) or a
 * cross (red) beside it, or the clause the menu made; beside it a quiet control clears the filter, the sort control,
 * and a menu that opens the column's filter overlay for the other operators. A column that does not filter keeps the
 * label and the sort.
 */
@Component({
  selector: 'ui-grid-header-checkbox',
  imports: [GridHeaderMenuDirective, GridHeaderSortComponent, UiIconDirective],
  template: `
    <button
      type="button"
      class="flex-1 min-w-0 h-full pl-2 pr-1 flex items-center gap-1 text-sm font-semibold text-left outline-none"
      [class]="labelClasses()"
      [class.pr-16]="showsControls()"
      [title]="label()"
      [attr.aria-label]="name()"
      [disabled]="!canCycle()"
      (click)="cycle($event)"
    >
      <span class="truncate">{{ state() === 'other' ? clause() : label() }}</span>
      @if (state() === 'yes') {
        <i icon="checkmark" class="hue-green text-(--hue-ink) shrink-0" aria-hidden="true"></i>
      } @else if (state() === 'no') {
        <i icon="close" class="hue-red text-(--hue-ink) shrink-0" aria-hidden="true"></i>
      }
    </button>

    @if (hasFilter() || isSortable() || canCycle()) {
      <div class="${GRID_HEADER_STRIP}" [class.opacity-100]="showsControls()">
        @if (hasFilter()) {
          <button type="button" class="${GRID_HEADER_CLEAR}" [attr.aria-label]="texts.of('ClearFilterOf', label())" (click)="clear($event)">
            <i icon="close" aria-hidden="true"></i>
          </button>
        }
        @if (isSortable()) {
          <ui-grid-header-sort [column]="column()" />
        }
        @if (canCycle()) {
          <button [uiGridHeaderMenu]="column()"><i icon="ellipsis-vertical" aria-hidden="true"></i></button>
        }
      </div>
    }
  `,
  host: { class: GRID_HEADER_HOST },
})
export class GridHeaderCheckboxComponent extends GridCellBase<GridHeaderCheckboxModel> {
  protected readonly texts = inject(TextsService);
  private readonly clauseOf = injectClauseText();

  protected readonly label = computed(() => columnLabelOf(this.column()));
  protected readonly isSortable = computed(() => this.column().isSortable && !!this.grid);
  protected readonly canCycle = computed(() => this.column().isFilterable && !!this.column().path && !!this.grid);
  protected readonly sort = computed(() => this.grid?.sortOf(this.column().path) ?? null);

  private readonly filter = computed(() => this.grid?.columnFilterOf(this.column().path));

  protected readonly hasFilter = computed(() => !!this.filter());

  /** What the column's filter is, as the label cycles it. */
  protected readonly state = computed(() => flagFilterOf(this.filter()));

  /** The clause of a filter the menu made, as the button reads it. */
  protected readonly clause = computed(() => {
    const filter = this.filter();
    return filter ? this.clauseOf(filter, this.column()) : '';
  });

  /** The controls stay while the column is sorted or filtered, and the button leaves them room. */
  protected readonly showsControls = computed(() => this.sort() !== null || this.hasFilter());

  protected readonly labelClasses = computed(() => labelClassesOf(this.canCycle(), this.hasFilter()));

  /** The button's name: what it filters by, and the filter it reads while there is one. */
  protected readonly name = computed(() => {
    const filterBy = this.texts.of('FilterBy', this.label());
    switch (this.state()) {
      case 'yes':
        return [filterBy, this.texts.of('Yes')].join(', ');
      case 'no':
        return [filterBy, this.texts.of('No')].join(', ');
      case 'other':
        return [filterBy, this.clause()].join(', ');
      default:
        return filterBy;
    }
  });

  /** Any to yes, yes to no, no to any; a clause the menu made starts the cycle again at yes. */
  protected cycle(event: MouseEvent): void {
    event.stopPropagation();
    const path = this.column().path;
    if (!this.canCycle() || !this.grid || !path) {
      return;
    }
    const next = NEXT[this.state()];
    if (next === null) {
      this.grid.clearColumnFilter(path);
    } else {
      this.grid.setColumnFilter({ kind: 'FilterRule', field: path, operator: 'equals', value: next });
    }
  }

  protected clear(event: MouseEvent): void {
    event.stopPropagation();
    const path = this.column().path;
    if (this.grid && path) {
      this.grid.clearColumnFilter(path);
    }
  }
}

/** The column's filter as the label reads it: one `equals` rule of a boolean is yes or no, any other filter a clause. */
function flagFilterOf(filter: GridFilter | null | undefined): FlagFilter {
  if (!filter) {
    return 'any';
  }
  const rules = rulesOf(filter);
  const rule = rules.length === 1 ? rules[0] : undefined;
  if (rule?.operator === 'equals' && typeof rule.value === 'boolean') {
    return rule.value ? 'yes' : 'no';
  }
  return 'other';
}

/** The button's ink: the `mark` hue over a filter, a link while it cycles the filter, the plain ink when it filters nothing. */
function labelClassesOf(canCycle: boolean, hasFilter: boolean): string {
  if (!canCycle) {
    return 'text-ink cursor-default';
  }
  return hasFilter ? 'text-mark cursor-pointer' : 'text-link hover:text-link-hover cursor-pointer';
}
