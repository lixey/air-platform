import { Component, computed, inject } from '@angular/core';
import type { GridCellCheckboxModel } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import { UiIconDirective } from '../../icon/ui-icon.directive';
import { GRID_CHECKBOX_CLASSES } from '../../theme/css-classes';
import { SELECTION_PATH } from '../column/grid-selection-column';
import { keyOf } from '../grid-row';
import { GridCellBase } from './grid-cell-base';

/**
 * A checkbox. In the selection column it selects the row — with shift, the range from the
 * last one clicked — and shows nothing for a pinned row; in a data column it shows the value
 * and changes nothing: a check for true and a cross for false, in the ink of the hue the outlet paints the cell in
 * (`green`, `red`), and nothing for null.
 */
@Component({
  selector: 'ui-grid-cell-checkbox',
  imports: [UiIconDirective],
  template: `
    @if (isSelection() && isPinned()) {
      <span class="sr-only">{{ texts.of('Pinned') }}</span>
    } @else if (isSelection()) {
      <input
        type="checkbox"
        class="${GRID_CHECKBOX_CLASSES}"
        [checked]="isChecked()"
        [attr.aria-label]="texts.of('SelectRow', key() ?? '')"
        (click)="onClick($event)"
      />
    } @else if (value() === true) {
      <i icon="checkmark" aria-hidden="true"></i>
      <span class="sr-only">{{ texts.of('Yes') }}</span>
    } @else if (value() === false) {
      <i icon="close" aria-hidden="true"></i>
      <span class="sr-only">{{ texts.of('No') }}</span>
    }
  `,
  host: { class: 'flex items-center justify-center h-full' },
})
export class GridCellCheckboxComponent extends GridCellBase<GridCellCheckboxModel> {
  protected readonly texts = inject(TextsService);

  protected readonly isSelection = computed(() => this.cell().column.path === SELECTION_PATH && !!this.grid);

  protected readonly key = computed(() => keyOf(this.cell().row));

  protected readonly isChecked = computed(() => !!this.grid?.selection.isSelected(this.cell().row));

  protected onClick(event: MouseEvent): void {
    event.stopPropagation();
    const row = this.cell().row;
    if (this.cell().column.isDisabled) {
      event.preventDefault();
      return;
    }
    if (this.grid && row) {
      this.grid.selection.toggle(row, this.grid.rows(), event.shiftKey);
    }
  }
}
