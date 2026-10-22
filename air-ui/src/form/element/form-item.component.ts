import { Component, computed, inject, input } from '@angular/core';
import { FIELD_DESCRIPTION } from '../../controls/field-description';
import type { FormElementDto } from '../../core/generated/contracts';
import { FormService } from '../form.service';
import { fieldIdOf } from './form-element-field';

/**
 * What stands above the field: `above` the label with the required mark; `spacer` an invisible
 * line that keeps the rhythm of a neighbouring label, for an element that draws its own label
 * beside the field, as the checkbox does; `none` nothing.
 */
export type FormItemLabelMode = 'above' | 'none' | 'spacer';

/**
 * What every element shares: the label above with the required mark, the field, the help below, and the messages that
 * name the path in the error's hue. The help and the messages describe the control: the item provides their ids, which
 * the control writes as its `aria-describedby`.
 */
@Component({
  selector: 'ui-form-item',
  template: `
    @if (labelMode() === 'above') {
      <label
        class="block mb-1 text-sm font-medium text-muted truncate"
        [attr.for]="id()"
        [attr.title]="element().title || element().label || null"
      >
        {{ element().label }}
        @if (element().isRequired) {
          <span class="hue-red text-(--hue-ink)" aria-hidden="true">*</span>
        }
      </label>
    } @else if (labelMode() === 'spacer') {
      <div class="block mb-1 text-sm font-medium text-muted invisible select-none" aria-hidden="true">&nbsp;</div>
    }
    <ng-content />
    @if (element().help; as help) {
      <p class="m-0 mt-1 text-xs text-muted" [id]="id() + '-help'">{{ help }}</p>
    }
    @for (message of errors(); track $index) {
      <p class="hue-red m-0 mt-1 text-xs text-(--hue-ink)" role="alert" [id]="id() + '-error-' + $index">{{ message }}</p>
    }
  `,
  host: { class: 'block min-w-0' },
  providers: [{ provide: FIELD_DESCRIPTION, useFactory: () => inject(FormItemComponent).describedBy }],
})
export class FormItemComponent {
  private readonly form = inject(FormService);

  readonly element = input.required<FormElementDto>();

  /** What stands above the field; the label unless the element says otherwise. */
  readonly labelMode = input<FormItemLabelMode>('above');

  readonly id = computed(() => fieldIdOf(this.element().path, this.form.idScope()));

  /** The ids of the help and of every message under the field, or null when there is none. */
  readonly describedBy = computed(() => {
    const ids = [...(this.element().help ? [`${this.id()}-help`] : []), ...this.errors().map((_, index) => `${this.id()}-error-${index}`)];
    return ids.length ? ids.join(' ') : null;
  });

  protected readonly errors = computed(() =>
    this.form
      .fieldOf(this.element().path)()
      .errors()
      .flatMap(e => (e.message ? [e.message] : [])),
  );
}
