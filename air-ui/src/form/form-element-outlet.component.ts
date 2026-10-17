import { Component, computed, input } from '@angular/core';
import { DynamicOutletDirective } from '../core/dynamic-component/dynamic-outlet.directive';
import type { FormElementDto, FormElementModel } from '../core/generated/contracts';

/**
 * One element of the form: mounts the component its model's kind names, given the element and the model.
 *
 * Fallback policy: an element whose kind nobody registered is drawn as `FormInput`: a form always has a field to
 * type in.
 */
@Component({
  selector: 'ui-form-element-outlet',
  imports: [DynamicOutletDirective],
  template: `
    <ng-container [uiDynamicOutlet]="element().model.kind" [uiDynamicOutletFallback]="fallback" [uiDynamicOutletInputs]="inputs()" />
  `,
  host: { class: 'block min-w-0' },
})
export class FormElementOutletComponent {
  readonly element = input.required<FormElementDto>();

  protected readonly fallback = 'FormInput' satisfies FormElementModel['kind'];
  protected readonly inputs = computed(() => ({ element: this.element(), model: this.element().model }));
}
