import { Component, computed, inject } from '@angular/core';
import { LocaleService } from '../../app/locale.service';
import type { GridCellDateModel } from '../../core/generated/contracts';
import { GridCellBase } from './grid-cell-base';
import { formatCellDate } from './grid-cell-format';

/** A date as the session's culture writes a day, the day of an instant on the user's clock. */
@Component({
  selector: 'ui-grid-cell-date',
  template: '<span class="block truncate tabular-nums">{{ text() }}</span>',
  host: { class: 'flex items-center h-full min-w-0 px-2' },
})
export class GridCellDateComponent extends GridCellBase<GridCellDateModel> {
  private readonly locale = inject(LocaleService).current;
  private readonly zone = inject(LocaleService).zone;

  protected readonly text = computed(() => formatCellDate(this.value(), 'date', this.locale(), this.zone()));
}
