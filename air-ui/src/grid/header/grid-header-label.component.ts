import { Component, computed } from '@angular/core';
import type { GridHeaderLabelModel } from '../../core/generated/contracts';
import { UiIconDirective } from '../../icon/ui-icon.directive';
import { GridCellBase } from '../cell/grid-cell-base';
import { headerLabelOf, headerTitleOf } from '../column/grid-column-label';
import { fieldOf } from '../filter/grid-filter-set';

/**
 * The column's label and nothing else, or a header row's cell: the label in the cell's hue, or the icon its model
 * names, on the hue's light ground when the model says `isHighlighted`. When the model carries a filter, a click sets that
 * filter on the column and a second click removes it, the label red while it is on.
 */
@Component({
  selector: 'ui-grid-header-label',
  imports: [UiIconDirective],
  template: `
    @if (icon(); as icon) {
      <i [icon]="icon" aria-hidden="true" [title]="title()"></i>
      <span class="sr-only">{{ label() }}</span>
    } @else {
      <span class="block overflow-hidden whitespace-nowrap font-medium" [title]="title()">{{ label() }}</span>
    }
  `,
  host: {
    '[class]': 'classes()',
    '[attr.role]': 'filter() ? "button" : null',
    '[attr.aria-pressed]': 'filter() ? isActive() : null',
    '(click)': 'toggleFilter($event)',
  },
})
export class GridHeaderLabelComponent extends GridCellBase<GridHeaderLabelModel> {
  protected readonly header = computed(() => this.cell().header);

  protected readonly label = computed(() => headerLabelOf(this.cell()));
  protected readonly title = computed(() => headerTitleOf(this.cell()));

  protected readonly icon = computed(() => this.model()?.icon ?? null);

  protected readonly filter = computed(() => this.model()?.filter ?? null);

  protected readonly isHighlighted = computed(() => this.model()?.isHighlighted === true);

  /** Whether the filter the cell sets is on: the column's filter of its field is that filter. */
  protected readonly isActive = computed(() => {
    const filter = this.filter();
    const current = filter ? this.grid?.columnFilterOf(fieldOf(filter)) : undefined;
    return !!filter && !!current && JSON.stringify(current) === JSON.stringify(filter);
  });

  protected readonly classes = computed(() => {
    // A header row's cell and a narrow column — a calendar's week — keep their padding to a pixel, so three digits fit in 24
    const width = this.column().width ?? null;
    const narrow = this.header() !== undefined || (width !== null && width < 48);
    const padding = narrow ? 'px-px text-[11px] tracking-tight tabular-nums' : 'px-2';
    const align = this.header() || narrow ? 'justify-center text-center' : '';
    const color = this.header()?.color ?? (this.isHighlighted() ? 'indigo' : null);
    const ink = inkOf(this.isActive(), color !== null);
    const ground = this.isHighlighted() ? 'bg-(--btn-ground) font-semibold' : '';
    const hue = color ? `hue-${color}` : '';
    const acts = this.filter() && this.grid ? 'cursor-pointer hover:text-mark select-none' : '';
    return `flex items-center h-full min-w-0 ${padding} ${align} ${hue} ${ink} ${ground} ${acts}`;
  });

  protected toggleFilter(event: MouseEvent): void {
    const filter = this.filter();
    if (!filter || !this.grid) {
      return;
    }
    event.stopPropagation();
    if (this.isActive()) {
      this.grid.clearColumnFilter(fieldOf(filter));
    } else {
      this.grid.setColumnFilter(filter);
    }
  }
}

/** The label's ink: the mark while its column is active, else its hue's ink when it has a hue. */
function inkOf(isActive: boolean, hasHue: boolean): string {
  if (isActive) {
    return 'text-mark';
  }
  return hasHue ? 'text-(--hue-ink)' : '';
}
