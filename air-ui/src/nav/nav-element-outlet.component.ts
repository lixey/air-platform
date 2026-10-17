import { Component, computed, effect, inject, input } from '@angular/core';
import { DynamicComponentRegistry } from '../core/dynamic-component/dynamic-component-registry';
import { DynamicOutletDirective } from '../core/dynamic-component/dynamic-outlet.directive';
import type { NavElementModel } from '../core/generated/contracts';

/**
 * Mounts one nav element: the component its kind names, given the element itself as its model and nothing else.
 *
 * Fallback policy: none. An element nobody registered is skipped and said so, and the nav around it stands: a
 * nav draws what it has.
 */
@Component({
  selector: 'ui-nav-element-outlet',
  imports: [DynamicOutletDirective],
  template: `
    @if (isKnown()) {
      <ng-container [uiDynamicOutlet]="element().kind" [uiDynamicOutletInputs]="inputs()" />
    }
  `,
  host: { class: 'contents' },
})
export class NavElementOutletComponent {
  private readonly registry = inject(DynamicComponentRegistry);

  readonly element = input.required<NavElementModel>();

  protected readonly isKnown = computed(() => !!this.registry.resolve(this.element().kind));

  protected readonly inputs = computed(() => ({ model: this.element() }));

  private readonly reportUnknown = effect(() => {
    if (!this.isKnown()) {
      const { kind, key } = this.element();
      console.warn(`[ui-nav] No component registered for the kind "${kind}"; element "${key}" is skipped.`);
    }
  });
}
