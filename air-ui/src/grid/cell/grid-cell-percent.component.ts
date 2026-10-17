import { Component } from '@angular/core';
import type { GridCellPercentModel } from '../../core/generated/contracts';
import { GridCellAmountBase } from './grid-cell-amount-base';
import { percentFormatOf } from './grid-cell-format';

/**
 * A percent, right aligned in tabular figures: the stored number itself (`15` is 15 %), grouped by the locale with its
 * percent sign, and exactly the fraction digits its model declares, two at most when it declares none.
 */
@Component({
  selector: 'ui-grid-cell-percent',
  template: '<span class="block truncate tabular-nums">{{ text() }}</span>',
  host: { class: 'flex items-center justify-end h-full min-w-0 px-2 text-right' },
})
export class GridCellPercentComponent extends GridCellAmountBase<GridCellPercentModel> {
  protected readonly formatOf = percentFormatOf;
}
