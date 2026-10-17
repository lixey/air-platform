import { Component } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import { CheckboxComponent } from '../../controls/checkbox.component';
import type { FormCheckboxModel } from '../../core/generated/contracts';
import { FormElementBase } from './form-element-base';
import { FormItemComponent } from './form-item.component';

/** The library's checkbox with the label beside it, on the field's row. */
@Component({
  selector: 'ui-form-checkbox',
  imports: [FormItemComponent, CheckboxComponent, FormField],
  template: `
    <ui-form-item [element]="element()" labelMode="spacer">
      <ui-checkbox [id]="id()" [formField]="field()">
        <span class="truncate">{{ element().label }}</span>
        @if (element().isRequired) {
          <span class="hue-red text-(--hue-ink)" aria-hidden="true">*</span>
        }
      </ui-checkbox>
    </ui-form-item>
  `,
})
export class FormCheckboxComponent extends FormElementBase<boolean, FormCheckboxModel> {}
