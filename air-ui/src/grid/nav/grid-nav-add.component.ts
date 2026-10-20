import { Component, ElementRef, inject, model } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { GridNavAddModel } from '../../core/generated/contracts';
import { GridService } from '../grid.service';

/**
 * The grid's `Add`: it opens the grid's own form for a new record through the service, hung from
 * itself, so the form is bound to the grid's parent and the grid refreshes when it closes. The
 * model the server sends describes the button only: the form is the grid's, which the service
 * knows without being told. Outside a grid there is no form to open, and it draws nothing.
 */
@Component({
  selector: 'ui-grid-nav-add',
  imports: [BtnComponent],
  template: `
    @if (grid) {
      <ui-btn [model]="model()" (click)="add()" />
    }
  `,
})
export class GridNavAddComponent {
  private readonly host = inject(ElementRef<HTMLElement>);
  protected readonly grid = inject(GridService, { optional: true });

  readonly model = model<GridNavAddModel | null>();

  protected add(): void {
    const m = this.model();
    this.grid?.openForm(null, this.host.nativeElement, m?.service ?? m?.overlay?.screen?.service, m?.overlay);
  }
}
