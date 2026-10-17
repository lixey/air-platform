import { Component, computed } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import type { FormTimeZoneModel } from '../../core/generated/contracts';
import { TimeZoneSelectComponent } from '../../select/time-zone-select.component';
import { FormElementBase } from './form-element-base';
import { FormItemComponent } from './form-item.component';

/**
 * A time zone, the time zone control bound to its field like every element's control: the zones its model carries,
 * searchable, each by its IANA id, the empty choice first labelled by the model's `inherit`, which names what the empty
 * value resolves to; a pick writes the zone's id, the empty choice `null`. The field gives it the disabled, readonly
 * and invalid states, so a read-only form shows the value, or `inherit` for none, and offers no change.
 */
@Component({
  selector: 'ui-form-time-zone',
  imports: [FormItemComponent, TimeZoneSelectComponent, FormField],
  template: `
    <ui-form-item [element]="element()">
      <ui-time-zone-select
        [zones]="zones()"
        [inherit]="model()?.inherit ?? ''"
        [label]="element().label ?? element().path"
        [clearable]="!element().isRequired"
        [formField]="field()"
      />
    </ui-form-item>
  `,
})
export class FormTimeZoneComponent extends FormElementBase<string | null, FormTimeZoneModel> {
  protected readonly zones = computed<readonly string[]>(() => this.model()?.zones ?? []);
}
