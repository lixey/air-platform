import { Component } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import type { FormDateModel } from '../../core/generated/contracts';
import { TemporalBoxComponent } from '../../datepicker/temporal-box.component';
import { FormElementBase } from './form-element-base';
import { FormItemComponent } from './form-item.component';

/** A calendar date: the temporal control, which opens the date picker, over the member's wire semantics. */
@Component({
  selector: 'ui-form-date',
  imports: [FormItemComponent, TemporalBoxComponent, FormField],
  template: `
    <ui-form-item [element]="element()">
      <ui-temporal-box
        [id]="id()"
        kind="date"
        [temporalType]="element().temporalType ?? null"
        [clearable]="!element().isRequired"
        [placeholder]="element().placeholder ?? ''"
        [formField]="field()"
      />
    </ui-form-item>
  `,
})
export class FormDateComponent extends FormElementBase<string | null, FormDateModel> {}
