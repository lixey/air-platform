import { Component } from '@angular/core';
import type { GridCellEmailModel } from '../../core/generated/contracts';
import { emailHref } from './grid-cell-href';
import { GridCellLinkBase } from './grid-cell-link-base';
import { GridCellLinkComponent } from './grid-cell-link.component';

/** An email, a `mailto:` link that writes to it; plain text in a disabled column and for a value without `@`. */
@Component({
  selector: 'ui-grid-cell-email',
  imports: [GridCellLinkComponent],
  template: '<ui-grid-cell-link [text]="text()" [href]="href()" />',
  host: { class: 'flex items-center h-full min-w-0 px-2' },
})
export class GridCellEmailComponent extends GridCellLinkBase<GridCellEmailModel> {
  protected readonly hrefOf = emailHref;
}
