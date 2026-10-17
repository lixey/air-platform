import { Component } from '@angular/core';
import type { GridCellMoneyModel } from '../../core/generated/contracts';
import { GridCellAmountBase } from './grid-cell-amount-base';
import { moneyFormatOf } from './grid-cell-format';

/**
 * Money, right aligned in tabular figures, grouped by the locale with the sign of the currency its model names, and
 * exactly the fraction digits it declares: two when it declares none. Without a currency, a grouped number.
 */
@Component({
  selector: 'ui-grid-cell-money',
  template: '<span class="block truncate tabular-nums">{{ text() }}</span>',
  host: { class: 'flex items-center justify-end h-full min-w-0 px-2 text-right' },
})
export class GridCellMoneyComponent extends GridCellAmountBase<GridCellMoneyModel> {
  protected readonly formatOf = moneyFormatOf;
}
