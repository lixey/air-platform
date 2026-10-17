import { Component, computed } from '@angular/core';
import type { GridHeaderToggleModel } from '../../core/generated/contracts';
import { UiIconDirective } from '../../icon/ui-icon.directive';
import { GridCellBase } from '../cell/grid-cell-base';
import { headerLabelOf } from '../column/grid-column-label';

/**
 * A header row's cell that folds a group: a click shows the columns the cell names when any
 * is hidden and hides them all otherwise, the chevron telling which.
 * The cell's span follows the columns shown, so the group widens and narrows with them.
 * As a column's header it is labelled by the column, its label or else its path.
 */
@Component({
  selector: 'ui-grid-header-toggle',
  imports: [UiIconDirective],
  template: `
    <span class="block truncate font-medium" [title]="label()">{{ label() }}</span>
    <i [icon]="isExpanded() ? 'chevron-left' : 'chevron-right'" class="shrink-0" aria-hidden="true"></i>
  `,
  host: {
    '[class]': 'classes()',
    role: 'button',
    '[attr.aria-expanded]': 'isExpanded()',
    '(click)': 'toggle($event)',
  },
})
export class GridHeaderToggleComponent extends GridCellBase<GridHeaderToggleModel> {
  protected readonly header = computed(() => this.cell().header);

  protected readonly label = computed(() => headerLabelOf(this.cell()));

  private readonly toggles = computed<readonly string[]>(() => this.model()?.toggles ?? []);

  /** Expanded while every column the cell names is shown. */
  protected readonly isExpanded = computed(() => {
    const paths = this.toggles();
    const columns = this.grid?.allColumns() ?? [];
    return paths.length > 0 && paths.every(path => columns.some(column => column.path === path && !column.isHidden));
  });

  protected readonly classes = computed(() => {
    const color = this.header()?.color;
    const hue = color ? `hue-${color} text-(--hue-ink)` : '';
    return `flex items-center gap-1 h-full min-w-0 px-2 cursor-pointer select-none hover:text-mark ${hue}`;
  });

  protected toggle(event: MouseEvent): void {
    event.stopPropagation();
    const grid = this.grid;
    if (!grid) {
      return;
    }
    const hide = this.isExpanded();
    for (const path of this.toggles()) {
      grid.columnState.setHidden(path, hide);
    }
  }
}
