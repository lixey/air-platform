import { Component } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import type { FormTimeModel } from '../../core/generated/contracts';
import { TemporalBoxComponent } from '../../datepicker/temporal-box.component';
import { FormElementBase } from './form-element-base';
import { FormItemComponent } from './form-item.component';

/** A time of day: the temporal control, which opens the picker's hours and minutes by the model's step, writing `HH:mm:00`. */
@Component({
  selector: 'ui-form-time',
  imports: [FormItemComponent, TemporalBoxComponent, FormField],
  template: `
    <ui-form-item [element]="element()">
      <ui-temporal-box
        [id]="id()"
        kind="time"
        [minuteStep]="model()?.minuteStep ?? null"
        [clearable]="!element().isRequired"
        [placeholder]="element().placeholder ?? ''"
        [formField]="field()"
      />
    </ui-form-item>
  `,
})
export class FormTimeComponent extends FormElementBase<string | null, FormTimeModel> {}
