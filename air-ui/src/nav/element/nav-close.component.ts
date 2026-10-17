import { Component, inject, model } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { NavCloseModel } from '../../core/generated/contracts';
import { OverlayRef } from '../../overlay/overlay-ref';

/**
 * A button that closes the overlay it is in. Outside an overlay there is nothing to close, and it
 * draws nothing; inside one it is always drawn, and the overlay's own leave decision — the form's
 * included — is what a dismissal has to pass.
 */
@Component({
  selector: 'ui-nav-close',
  imports: [BtnComponent],
  template: `
    @if (ref) {
      <ui-btn [model]="model()" (click)="ref.dismiss()" />
    }
  `,
})
export class NavCloseComponent {
  protected readonly ref = inject(OverlayRef, { optional: true });

  readonly model = model<NavCloseModel | null>();
}
