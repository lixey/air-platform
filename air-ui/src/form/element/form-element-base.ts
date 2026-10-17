import { computed, Directive, inject, input } from '@angular/core';
import type { FieldTree } from '@angular/forms/signals';
import type { FormElementDto } from '../../core/generated/contracts';
import { FormService } from '../form.service';
import { fieldIdOf } from './form-element-field';

/**
 * What every element has: the form, the element, the model of its kind, its path and DOM id, and its field in the
 * form tree. `T` is the field's value and `M` the model of the kind it draws; neither has a default, so the registry
 * checks the class against its kind.
 */
@Directive()
export abstract class FormElementBase<T, M> {
  protected readonly form = inject(FormService);

  readonly element = input.required<FormElementDto>();

  /** The model of the kind the element is drawn by: what its component reads beside the element. */
  readonly model = input<M>();

  protected readonly path = computed(() => this.element().path);
  protected readonly id = computed(() => fieldIdOf(this.path(), this.form.idScope()));
  protected readonly field = computed<FieldTree<T>>(() => this.form.fieldOf<T>(this.path()));
}
