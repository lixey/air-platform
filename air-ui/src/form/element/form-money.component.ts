import { Component, inject } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import { LocaleService } from '../../app/locale.service';
import { AmountBoxComponent } from '../../controls/amount-box.component';
import type { FormMoneyModel } from '../../core/generated/contracts';
import { FormElementBase } from './form-element-base';
import { FormItemComponent } from './form-item.component';

/** Money: the amount box in the session's culture, with the sign of the model's currency and the field's scale; empty is null. */
@Component({
  selector: 'ui-form-money',
  imports: [FormItemComponent, AmountBoxComponent, FormField],
  template: `
    <ui-form-item [element]="element()">
      <ui-amount-box
        [id]="id()"
        kind="money"
        [locale]="locale()"
        [currency]="model()?.currency ?? null"
        [digits]="element().constraints.scale"
        [placeholder]="element().placeholder ?? ''"
        [formField]="field()"
      />
    </ui-form-item>
  `,
})
export class FormMoneyComponent extends FormElementBase<number | null, FormMoneyModel> {
  protected readonly locale = inject(LocaleService).current;
}
