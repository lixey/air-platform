import { Component, computed, inject, model } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { NavToggleModel } from '../../core/generated/contracts';
import { PANELS } from '../../panels/panels-tokens';

/**
 * A button that opens and closes a panel — of the layout the nav is in, or of the
 * outermost one when its model says `targetsRoot`, which is how a screen's nav reaches the
 * frame's navigation. It reads as pressed while the panel is open, and it is not drawn at
 * all while the engine's controls model does not list the panel — below `lg`, or for a
 * panel that cannot be closed — because a control for what cannot be shown is a lie.
 */
@Component({
  selector: 'ui-nav-toggle',
  imports: [BtnComponent],
  template: `
    @if (isListed()) {
      <ui-btn [model]="model()" [selected]="isOpen()" (click)="toggle()" />
    }
  `,
})
export class NavToggleComponent {
  private readonly panels = inject(PANELS, { optional: true });

  readonly model = model<NavToggleModel | null>();

  private readonly target = computed(() => (this.model()?.targetsRoot ? this.panels?.root : this.panels));

  protected readonly isListed = computed(() => {
    const path = this.model()?.path;
    return (
      !!path &&
      (this.target()
        ?.togglePanels()
        .some(t => t.path === path) ??
        false)
    );
  });

  protected readonly isOpen = computed(() => {
    const path = this.model()?.path;
    const target = this.target();
    return !!path && !!target && !target.isCollapsed(path);
  });

  /** A click reaches the element although its button is inert, so a toggle sent disabled is asked again. */
  protected toggle(): void {
    const path = this.model()?.path;

    if (path && !this.model()?.isDisabled) {
      this.target()?.toggle(path);
    }
  }
}
