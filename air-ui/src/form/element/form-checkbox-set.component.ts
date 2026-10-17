import { Component, computed } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import { CheckboxSetComponent } from '../../controls/checkbox-set.component';
import type { CheckboxOptionDto, FormCheckboxSetModel } from '../../core/generated/contracts';
import { FormElementBase } from './form-element-base';
import { FormItemComponent } from './form-item.component';

/** A set of the library's checkboxes over the element's options; the value is the ids of the checked ones. */
@Component({
  selector: 'ui-form-checkbox-set',
  imports: [FormItemComponent, CheckboxSetComponent, FormField],
  template: `
    <ui-form-item [element]="element()">
      <ui-checkbox-set [options]="options()" [label]="element().label ?? null" [formField]="field()" />
    </ui-form-item>
  `,
})
export class FormCheckboxSetComponent extends FormElementBase<readonly number[] | null, FormCheckboxSetModel> {
  protected readonly options = computed<readonly CheckboxOptionDto[]>(() => this.model()?.options ?? []);
}
