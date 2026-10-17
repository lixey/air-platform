import { Component, computed } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import { TextBoxComponent } from '../../controls/text-box.component';
import type { FormTextareaModel } from '../../core/generated/contracts';
import { FormElementBase } from './form-element-base';
import { allowsNullOf } from './form-element-field';
import { FormItemComponent } from './form-item.component';

/** A text area of a few lines. */
@Component({
  selector: 'ui-form-textarea',
  imports: [FormItemComponent, TextBoxComponent, FormField],
  template: `
    <ui-form-item [element]="element()">
      <ui-text-box
        [id]="id()"
        kind="textarea"
        [allowsNull]="allowsNull()"
        [placeholder]="element().placeholder ?? ''"
        [formField]="field()"
      />
    </ui-form-item>
  `,
})
export class FormTextareaComponent extends FormElementBase<string | null, FormTextareaModel> {
  protected readonly allowsNull = computed(() => allowsNullOf(this.element()));
}
