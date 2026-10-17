import { Component } from '@angular/core';
import type { GridCellPhoneModel } from '../../core/generated/contracts';
import { phoneHref } from './grid-cell-href';
import { GridCellLinkBase } from './grid-cell-link-base';
import { GridCellLinkComponent } from './grid-cell-link.component';

/** A phone as stored, a `tel:` link that calls it; plain text in a disabled column and for a value without digits. */
@Component({
  selector: 'ui-grid-cell-phone',
  imports: [GridCellLinkComponent],
  template: '<ui-grid-cell-link [text]="text()" [href]="href()" />',
  host: { class: 'flex items-center h-full min-w-0 px-2' },
})
export class GridCellPhoneComponent extends GridCellLinkBase<GridCellPhoneModel> {
  protected readonly hrefOf = phoneHref;
}
