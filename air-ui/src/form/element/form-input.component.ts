import { Component, computed } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import { TextBoxComponent } from '../../controls/text-box.component';
import type { FormElementDto, FormFieldType, FormInputModel } from '../../core/generated/contracts';
import { FormElementBase } from './form-element-base';
import { allowsNullOf } from './form-element-field';
import { FormItemComponent } from './form-item.component';

/** The field types a text box draws as a kind of its own; any other type is text. */
const BOX_KINDS = { password: true, email: true, url: true } satisfies Record<FormFieldType, true>;

/** A text box: text, password, email or a site's address by the element's type. */
@Component({
  selector: 'ui-form-input',
  imports: [FormItemComponent, TextBoxComponent, FormField],
  template: `
    <ui-form-item [element]="element()">
      <ui-text-box
        [id]="id()"
        [kind]="kind()"
        [allowsNull]="allowsNull()"
        [placeholder]="element().placeholder ?? ''"
        [formField]="field()"
      />
    </ui-form-item>
  `,
})
export class FormInputComponent extends FormElementBase<string | null, FormInputModel> {
  protected readonly kind = computed<FormFieldType | 'text'>(() => {
    const type = this.element().type;
    return isFormFieldType(type) ? type : 'text';
  });
  protected readonly allowsNull = computed(() => allowsNullOf(this.element()));
}

function isFormFieldType(type: FormElementDto['type']): type is FormFieldType {
  return Object.hasOwn(BOX_KINDS, type);
}
