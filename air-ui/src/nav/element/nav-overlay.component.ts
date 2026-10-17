import { Component, ElementRef, inject, Injector, model } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { NavOverlayModel } from '../../core/generated/contracts';
import { OverlayService } from '../../overlay/overlay.service';

/**
 * A button that opens the overlay its model describes, hung from itself when that overlay is a
 * menu, and names it by its own label. The overlay is mounted through this element's injector, so its panels see what the
 * nav's surroundings provide — a grid's service, for the filters it opens. It binds nothing
 * and follows nothing: a grid's own form is opened by `GridNavAdd`, and an application's own
 * element reaches the same through `GridService.withParentParams` and `trackForm`.
 */
@Component({
  selector: 'ui-nav-overlay',
  imports: [BtnComponent],
  template: '<ui-btn [model]="model()" (click)="open()" />',
})
export class NavOverlayComponent {
  private readonly overlay = inject(OverlayService);
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly injector = inject(Injector);

  readonly model = model<NavOverlayModel | null>();

  protected open(): void {
    const model = this.model();

    if (model?.overlay) {
      this.overlay.open(model.overlay, this.host.nativeElement, this.injector).name({ label: model.label ?? model.title ?? undefined });
    }
  }
}
