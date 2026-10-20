import { Component, computed, effect, linkedSignal, untracked } from '@angular/core';
import { FormField } from '@angular/forms/signals';
import type { FilterNode, FormSelectModel, Reference } from '../../core/generated/contracts';
import { valueAt } from '../../core/json';
import type { SelectOption } from '../../select/select-option';
import { labelPathsOf, optionOfReference, referenceOfOption } from '../../select/select-option';
import type { SelectSource } from '../../select/select-source';
import { SelectComponent } from '../../select/select.component';
import { FIELD_CLASSES } from '../../theme/css-classes';
import { sanitizeFormDraft } from '../form-value';
import { FormElementBase } from './form-element-base';
import { FormItemComponent } from './form-item.component';

/**
 * A reference picked from a grid, or a key picked from fixed options, the select bound to its field like every
 * element's control: the key it picks goes through the field, which also gives it the disabled, readonly and invalid
 * states. Over a grid: the select control over the reference service and the label columns its model names, the
 * default ones when it names none; a pick also writes the reference (`{ id, name }`) at the reference's path. Without
 * a reference service (the principal may not view that grid) the reference's name is shown and nothing opens. Over
 * the options its model carries: they are offered without a request, the name of the option the value holds is shown,
 * and a pick writes the key alone.
 */
@Component({
  selector: 'ui-form-select',
  imports: [FormItemComponent, SelectComponent, FormField],
  template: `
    <ui-form-item [element]="element()">
      @if (select(); as select) {
        <ui-select
          [model]="select"
          [multiple]="isMultiple()"
          [createOverlay]="model()?.createOverlay ?? null"
          [picked]="selected()"
          [clearable]="!element().isRequired"
          [placeholder]="element().placeholder ?? ''"
          [formField]="field()"
          (pickedChange)="caption($event)"
        />
      } @else {
        <div class="${FIELD_CLASSES} border-line flex items-center text-muted">{{ referenceText() }}</div>
      }
    </ui-form-item>
  `,
})
export class FormSelectComponent extends FormElementBase<unknown, FormSelectModel> {
  protected readonly isMultiple = computed(() => Boolean(this.model()?.isMultiple));

  protected readonly references = computed<readonly Reference[]>(() => {
    const path = this.element().referencePath;
    if (!path) {
      return [];
    }
    const raw = this.form.value()[path];
    if (Array.isArray(raw)) {
      return raw as Reference[];
    }
    return raw ? [raw as Reference] : [];
  });

  protected readonly referenceText = computed(() =>
    this.references()
      .map(r => r.name)
      .join(', '),
  );

  /** The fixed options the model carries; `null` for a select over a reference service. */
  protected readonly options = computed<readonly SelectOption[] | null>(() => this.model()?.options?.map(optionOfReference) ?? null);

  protected readonly selected = computed<readonly SelectOption[]>(() => {
    const options = this.options();
    if (options) {
      const val = this.field()().value();
      if (this.isMultiple() && Array.isArray(val)) {
        return val.flatMap(k => {
          const opt = options.find(o => o.id === k);
          return opt ? [opt] : [];
        });
      }
      const option = options.find(o => o.id === val);
      return option ? [option] : [];
    }
    return this.references().map(optionOfReference);
  });

  protected readonly referenceFilter = computed<FilterNode | null>(() => {
    const model = this.model();
    if (!model) {
      return null;
    }
    const nodes: FilterNode[] = [];
    if (model.referenceFilter) {
      nodes.push(model.referenceFilter);
    }
    const rules = model.referenceDraftRules;
    if (rules && rules.length > 0) {
      const formVal = this.form.value();
      for (const rule of rules) {
        const val = formVal[rule.draftField];
        if (val !== undefined && val !== null) {
          nodes.push({
            kind: 'FilterRule',
            field: rule.sourceField,
            operator: 'equals',
            value: val,
          });
        }
      }
    }
    if (nodes.length === 0) {
      return null;
    }
    if (nodes.length === 1) {
      return nodes[0];
    }
    return {
      kind: 'FilterGroup',
      logic: 'and',
      items: nodes,
    };
  });

  protected readonly select = computed<SelectSource | null>(() => {
    const element = this.element();
    const label = element.label ?? element.path;
    const options = this.options();
    if (options) {
      return { label, options };
    }
    const service = this.model()?.referenceService;
    const patchSourceColumns = element.selectPatches?.map(p => p.sourceColumn).filter(Boolean);
    const referenceFilter = this.referenceFilter();
    return service
      ? {
          service,
          labelPaths: labelPathsOf(this.model()?.referenceLabelPaths),
          label,
          formDraft: sanitizeFormDraft(this.form.value(), this.form.model()?.elements),
          ...(patchSourceColumns?.length ? { patchSourceColumns } : {}),
          ...(referenceFilter ? { referenceFilter } : {}),
        }
      : null;
  });

  /** The draft's value at each dependency of the narrowing rules, the same list while every value is. */
  private readonly dependencies = computed<readonly unknown[]>(
    () => {
      const value = this.form.value();
      return (this.model()?.referenceDraftRules ?? []).map(rule => value[rule.draftField]);
    },
    { equal: hasSameItems },
  );

  /** The dependencies the picked reference is settled under: those every record the form adopts carries, then those of the last edit. */
  private readonly settled = linkedSignal<number, readonly unknown[]>({
    source: this.form.adoption,
    computation: () => untracked(this.dependencies),
  });

  constructor() {
    super();
    effect(() => {
      const dependencies = this.dependencies();
      if (hasSameItems(dependencies, this.settled())) {
        return;
      }
      untracked(() => {
        this.settled.set(dependencies);
        const curVal = this.field()().value() ?? null;
        if (curVal === null || (Array.isArray(curVal) && curVal.length === 0)) {
          return;
        }
        this.form.setValueAt(this.element().path, this.isMultiple() ? [] : null);
        const referencePath = this.element().referencePath;
        if (referencePath) {
          this.form.setValueAt(referencePath, this.isMultiple() ? [] : null);
        }
      });
    });
  }

  /** The caption of a reference picked, beside the key the field received: fixed options have none to write. */
  protected caption(options: readonly SelectOption[]): void {
    const picked = options[0] ?? null;
    const path = this.element().referencePath;
    if (path && !this.options()) {
      this.form.setValueAt(
        path,
        this.isMultiple() ? options.map(referenceOfOption) : picked ? referenceOfOption(picked) : null,
      );
    }
    const patches = this.element().selectPatches;
    if (patches && patches.length > 0 && picked?.row && typeof picked.row === 'object') {
      const row = picked.row as Record<string, unknown>;
      for (const patch of patches) {
        const val = valueAt(row, patch.sourceColumn);
        if (val !== undefined) {
          this.form.setValueAt(patch.targetField, val);
        }
      }
    }
  }
}

/** Whether two lists of dependency values hold the same value at every place. */
function hasSameItems(a: readonly unknown[], b: readonly unknown[]): boolean {
  return a.length === b.length && a.every((item, index) => item === b[index]);
}
