import { Component, computed, inject } from '@angular/core';
import { TextsService } from '../../core/texts.service';
import { GRID_CHECKBOX_CLASSES } from '../../theme/css-classes';
import type { GridHeaderSelectionModel } from '../../ui-components';
import { GridCellBase } from '../cell/grid-cell-base';

/** The header of the selection column: selects the page, or clears the selection; half-checked while some are selected. */
@Component({
  selector: 'ui-grid-header-selection',
  template: `
    <input
      type="checkbox"
      class="${GRID_CHECKBOX_CLASSES}"
      [attr.aria-label]="texts.of('SelectPage')"
      [checked]="isAll()"
      [indeterminate]="isSome()"
      [disabled]="!grid || counts().selectable === 0"
      (change)="toggle($event)"
    />
  `,
  host: { class: 'flex items-center justify-center h-full' },
})
export class GridHeaderSelectionComponent extends GridCellBase<GridHeaderSelectionModel> {
  protected readonly texts = inject(TextsService);

  protected readonly counts = computed(
    () =>
      this.grid?.selection.countIn(this.grid.rows()) ?? {
        selectable: 0,
        selected: 0,
      },
  );

  protected readonly isAll = computed(() => {
    const { selectable, selected } = this.counts();
    return selectable > 0 && selected === selectable;
  });

  protected readonly isSome = computed(() => {
    const { selectable, selected } = this.counts();
    return selected > 0 && selected < selectable;
  });

  protected toggle(event: Event): void {
    this.grid?.selection.toggleAll(this.grid.rows(), (event.target as HTMLInputElement).checked);
  }
}
