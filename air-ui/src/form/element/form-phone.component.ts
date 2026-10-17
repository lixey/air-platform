import { Component } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import { PhoneBoxComponent } from '../../controls/phone-box.component';
import type { FormPhoneModel } from '../../core/generated/contracts';
import { FormElementBase } from './form-element-base';
import { FormItemComponent } from './form-item.component';

/** A phone: the phone box, writing the number by the model's masks and storing `+` and the digits; empty is null. */
@Component({
  selector: 'ui-form-phone',
  imports: [FormItemComponent, PhoneBoxComponent, FormField],
  template: `
    <ui-form-item [element]="element()">
      <ui-phone-box [id]="id()" [masks]="model()?.masks ?? []" [placeholder]="element().placeholder ?? ''" [formField]="field()" />
    </ui-form-item>
  `,
})
export class FormPhoneComponent extends FormElementBase<string | null, FormPhoneModel> {}
