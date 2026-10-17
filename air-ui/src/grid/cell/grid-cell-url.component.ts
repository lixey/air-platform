import { Component } from '@angular/core';
import type { GridCellUrlModel } from '../../core/generated/contracts';
import { siteHref } from './grid-cell-href';
import { GridCellLinkBase } from './grid-cell-link-base';
import { GridCellLinkComponent } from './grid-cell-link.component';

/**
 * A site, a link to its page in a new tab, `https://` before an address without a
 * scheme; plain text for any scheme but `http` and `https`, and in a disabled column.
 */
@Component({
  selector: 'ui-grid-cell-url',
  imports: [GridCellLinkComponent],
  template: '<ui-grid-cell-link [text]="text()" [href]="href()" />',
  host: { class: 'flex items-center h-full min-w-0 px-2' },
})
export class GridCellUrlComponent extends GridCellLinkBase<GridCellUrlModel> {
  protected readonly hrefOf = siteHref;
}
