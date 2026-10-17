import { Component, inject, model } from '@angular/core';
import { Router } from '@angular/router';
import { BtnComponent } from '../../btn/btn.component';
import type { NavLinkModel } from '../../core/generated/contracts';
import { isExternalPath } from '../../core/url';

/** A button that navigates: through the router, or outside the application for an absolute URL. */
@Component({
  selector: 'ui-nav-link',
  imports: [BtnComponent],
  template: '<ui-btn [model]="model()" (click)="go()" />',
})
export class NavLinkComponent {
  private readonly router = inject(Router);

  readonly model = model<NavLinkModel | null>();

  protected go(): void {
    const path = this.model()?.path;

    if (!path) {
      return;
    }

    if (isExternalPath(path)) {
      window.open(path, '_blank', 'noopener');
    } else {
      void this.router.navigateByUrl(path);
    }
  }
}
