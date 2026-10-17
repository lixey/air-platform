import { Component, computed, inject } from '@angular/core';
import { LocaleService } from '../../app/locale.service';
import type { GridCellNumberModel } from '../../core/generated/contracts';
import { GridCellBase } from './grid-cell-base';
import { formatCellNumber } from './grid-cell-format';

/**
 * A number, right aligned in tabular figures, with the fraction digits its model declares, grouped by the locale unless
 * its model switches the groups off: a key is written in digits alone.
 */
@Component({
  selector: 'ui-grid-cell-number',
  template: '<span class="block truncate tabular-nums">{{ text() }}</span>',
  host: { class: 'flex items-center justify-end h-full min-w-0 px-2 text-right' },
})
export class GridCellNumberComponent extends GridCellBase<GridCellNumberModel> {
  private readonly locale = inject(LocaleService).current;

  protected readonly text = computed(() => formatCellNumber(this.value(), this.locale(), this.model()?.decimals, this.model()?.isGrouped));
}
