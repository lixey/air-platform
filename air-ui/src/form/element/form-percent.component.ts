import { Component, inject } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import { LocaleService } from '../../app/locale.service';
import { AmountBoxComponent } from '../../controls/amount-box.component';
import type { FormPercentModel } from '../../core/generated/contracts';
import { FormElementBase } from './form-element-base';
import { FormItemComponent } from './form-item.component';

/** A percent, the stored number itself: the amount box in the session's culture with `%` beside it and the field's scale; empty is null. */
@Component({
  selector: 'ui-form-percent',
  imports: [FormItemComponent, AmountBoxComponent, FormField],
  template: `
    <ui-form-item [element]="element()">
      <ui-amount-box
        [id]="id()"
        kind="percent"
        [locale]="locale()"
        [digits]="element().constraints.scale"
        [placeholder]="element().placeholder ?? ''"
        [formField]="field()"
      />
    </ui-form-item>
  `,
})
export class FormPercentComponent extends FormElementBase<number | null, FormPercentModel> {
  protected readonly locale = inject(LocaleService).current;
}
