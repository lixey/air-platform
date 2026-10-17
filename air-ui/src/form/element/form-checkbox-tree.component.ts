import { Component, computed } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import { CheckboxTreeComponent } from '../../controls/checkbox-tree.component';
import type { CheckboxTreeNodeDto, FormCheckboxTreeModel } from '../../core/generated/contracts';
import { FormElementBase } from './form-element-base';
import { FormItemComponent } from './form-item.component';

/** A tree of the library's checkboxes over the element's nodes; the value is the keys of the top-most checked ones. */
@Component({
  selector: 'ui-form-checkbox-tree',
  imports: [FormItemComponent, CheckboxTreeComponent, FormField],
  template: `
    <ui-form-item [element]="element()">
      <ui-checkbox-tree [nodes]="nodes()" [label]="element().label ?? null" [formField]="field()" />
    </ui-form-item>
  `,
})
export class FormCheckboxTreeComponent extends FormElementBase<readonly string[] | null, FormCheckboxTreeModel> {
  protected readonly nodes = computed<readonly CheckboxTreeNodeDto[]>(() => this.model()?.nodes ?? []);
}
