import { computed, Directive, inject, type Signal } from '@angular/core';
import { LocaleService } from '../../app/locale.service';
import type { AmountFormat } from '../../controls/amount-text';
import { GridCellBase } from './grid-cell-base';
import { formatCellAmount } from './grid-cell-format';

/** A cell that writes an amount: its value by the format `formatOf` reads of its model, in the session's culture. */
@Directive()
export abstract class GridCellAmountBase<M> extends GridCellBase<M> {
  private readonly locale = inject(LocaleService).current;

  protected readonly text: Signal<string> = computed(() => formatCellAmount(this.value(), this.locale(), this.formatOf(this.model())));

  /** How the kind's model writes an amount. */
  protected abstract readonly formatOf: (model: M | undefined) => AmountFormat;
}
