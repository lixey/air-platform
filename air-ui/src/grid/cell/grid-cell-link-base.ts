import { computed, Directive } from '@angular/core';
import { GridCellBase } from './grid-cell-base';
import { formatCellText } from './grid-cell-format';

/** A cell drawn as a link: the value as text, and where it leads by `hrefOf`; no link in a disabled column. */
@Directive()
export abstract class GridCellLinkBase<M> extends GridCellBase<M> {
  protected readonly text = computed(() => formatCellText(this.value()));

  /** Where the value leads; none in a disabled column. */
  protected readonly href = computed(() => (this.isDisabled() ? null : this.hrefOf(this.text())));

  /** Where a text leads; null for one that leads nowhere. */
  protected abstract readonly hrefOf: (text: string) => string | null;
}
