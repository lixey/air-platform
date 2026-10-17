import { Component } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import type { FormDatetimeModel } from '../../core/generated/contracts';
import { TemporalBoxComponent } from '../../datepicker/temporal-box.component';
import { FormElementBase } from './form-element-base';
import { FormItemComponent } from './form-item.component';

/**
 * A date and time: the temporal control, which opens the date picker
 * with minutes by the model's step, over the member's wire semantics.
 */
@Component({
  selector: 'ui-form-datetime',
  imports: [FormItemComponent, TemporalBoxComponent, FormField],
  template: `
    <ui-form-item [element]="element()">
      <ui-temporal-box
        [id]="id()"
        kind="datetime"
        [temporalType]="element().temporalType ?? null"
        [minuteStep]="model()?.minuteStep ?? null"
        [clearable]="!element().isRequired"
        [placeholder]="element().placeholder ?? ''"
        [formField]="field()"
      />
    </ui-form-item>
  `,
})
export class FormDatetimeComponent extends FormElementBase<string | null, FormDatetimeModel> {}
