import { Component, computed } from '@angular/core';
import type { GridCellPickerModel } from '../../core/generated/contracts';
import { GridCellBase } from './grid-cell-base';
import { formatCellText } from './grid-cell-format';

@Component({
  selector: 'ui-grid-cell-picker',
  template: '<span class="block max-w-full truncate">{{ text() }}</span>',
  host: { class: 'flex items-center h-full min-w-0 px-2' },
})
export class GridCellPickerComponent extends GridCellBase<GridCellPickerModel> {
  protected readonly text = computed(() => formatCellText(this.cell().value));
}
