import { Component, computed, inject, input } from '@angular/core';
import type { GridColumnDto, SortDirection } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import type { UiIcon } from '../../icon/ui-icon';
import { UiIconDirective } from '../../icon/ui-icon.directive';
import { GRID_HEADER_BUTTON } from '../../theme/css-classes';
import { columnLabelOf } from '../column/grid-column-label';
import { GridService } from '../grid.service';

/**
 * The sort control of a header: the direction as an icon, the column's place while several
 * columns are sorted, and the click that cycles none -> ascending -> descending, joining the
 * columns already sorted while `Shift` is held. Every sortable header composes it instead of
 * spelling the same button out again.
 */
@Component({
  selector: 'ui-grid-header-sort',
  imports: [UiIconDirective],
  template: `
    <button
      type="button"
      class="${GRID_HEADER_BUTTON} relative"
      [class.text-mark]="sort() !== null"
      [attr.aria-label]="texts.of('SortBy', label())"
      (click)="cycle($event)"
    >
      <i [icon]="icon()" aria-hidden="true"></i>
      @if (position(); as position) {
        <span class="absolute top-0 right-0 text-[9px] leading-3 font-semibold tabular-nums" aria-hidden="true">{{ position }}</span>
      }
    </button>
  `,
  host: { class: 'contents' },
})
export class GridHeaderSortComponent {
  protected readonly texts = inject(TextsService);
  private readonly grid = inject(GridService, { optional: true });

  readonly column = input.required<GridColumnDto>();

  protected readonly label = computed(() => columnLabelOf(this.column()));

  protected readonly sort = computed(() => this.grid?.sortOf(this.column().path) ?? null);

  /** The column's place in the sort while more than one column is sorted. */
  protected readonly position = computed(() => this.grid?.sortPositionOf(this.column().path) ?? null);

  protected readonly icon = computed<UiIcon>(() => {
    switch (this.sort()) {
      case 'asc':
        return 'chevrons-up';
      case 'desc':
        return 'chevrons-down';
      default:
        return 'chevrons-up-down';
    }
  });

  protected cycle(event: MouseEvent): void {
    event.stopPropagation();
    const path = this.column().path;
    if (!this.grid || !path) {
      return;
    }
    this.grid.setSort(path, nextSort(this.sort()), event.shiftKey);
  }
}

/** The sort a click moves to: none, ascending, descending, none again. */
function nextSort(sort: SortDirection | null): SortDirection | null {
  if (sort === null) {
    return 'asc';
  }
  return sort === 'asc' ? 'desc' : null;
}
