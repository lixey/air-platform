import { Component } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import { NumberBoxComponent } from '../../controls/number-box.component';
import type { FormNumberModel } from '../../core/generated/contracts';
import { FormElementBase } from './form-element-base';
import { FormItemComponent } from './form-item.component';

/** A number box, left aligned in tabular figures, stepping by its model's step, with a stepper when its model says so; empty is null. */
@Component({
  selector: 'ui-form-number',
  imports: [FormItemComponent, NumberBoxComponent, FormField],
  template: `
    <ui-form-item [element]="element()">
      <ui-number-box
        [id]="id()"
        [placeholder]="element().placeholder ?? ''"
        [step]="model()?.step"
        [steppable]="model()?.hasStepper ?? false"
        [formField]="field()"
      />
    </ui-form-item>
  `,
})
export class FormNumberComponent extends FormElementBase<number | null, FormNumberModel> {}
