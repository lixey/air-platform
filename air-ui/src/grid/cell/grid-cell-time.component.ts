import { Component, computed, inject } from '@angular/core';
import { LocaleService } from '../../app/locale.service';
import type { GridCellTimeModel } from '../../core/generated/contracts';
import { GridCellBase } from './grid-cell-base';
import { formatCellTime } from './grid-cell-format';

/** A time of day as the session's culture writes its hour and minute; a value that is no time of day as it is. */
@Component({
  selector: 'ui-grid-cell-time',
  template: '<span class="block truncate tabular-nums">{{ text() }}</span>',
  host: { class: 'flex items-center h-full min-w-0 px-2' },
})
export class GridCellTimeComponent extends GridCellBase<GridCellTimeModel> {
  private readonly locale = inject(LocaleService).current;

  protected readonly text = computed(() => formatCellTime(this.value(), this.locale()));
}
