import { Component, computed, inject } from '@angular/core';
import { LocaleService } from '../../app/locale.service';
import type { GridCellDatetimeModel } from '../../core/generated/contracts';
import { GridCellBase } from './grid-cell-base';
import { formatCellDate } from './grid-cell-format';

/** A stamp as the session's culture writes a day with its hour and minute, an instant on the user's clock. */
@Component({
  selector: 'ui-grid-cell-datetime',
  template: '<span class="block truncate tabular-nums">{{ text() }}</span>',
  host: { class: 'flex items-center h-full min-w-0 px-2' },
})
export class GridCellDatetimeComponent extends GridCellBase<GridCellDatetimeModel> {
  private readonly locale = inject(LocaleService).current;
  private readonly zone = inject(LocaleService).zone;

  protected readonly text = computed(() => formatCellDate(this.value(), 'datetime', this.locale(), this.zone()));
}
