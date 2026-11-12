import { computed, linkedSignal, type Signal, signal, untracked } from '@angular/core';
import { type FieldTree, form } from '@angular/forms/signals';
import type { AffectedDto, FormDataDto, FormElementDto, FormModel, MessageDto } from '../core/generated/contracts';
import { isRecord } from '../core/json';
import type { TextsService } from '../core/texts.service';
import type { Unavailable } from '../nav/nav-availability';
import { evaluateFilterNode, formFieldRules, isFieldDisabled } from './form-field-rules';
import {
  absorbDraft,
  canonicalJson,
  frozenCopy,
  hasSecret,
  indexFormElements,
  inputModelOf,
  isSecret,
  retainFieldMessages,
  type SplitDraft,
  splitDraft,
  splitInputDraft,
} from './form-value';

/** What a form of input alone is made of: the dialog's elements and the values its opener gives. */
export interface FormInput {
  readonly elements: readonly FormElementDto[];
  readonly value: Readonly<Record<string, unknown>>;
}

export interface FormDraftStateOptions {
  readonly instance: number;
  readonly zone: Signal<string>;
  readonly texts: TextsService;
  readonly refusesInput: () => boolean;
  readonly canAbsorb: () => boolean;
}

/**
 * Owns the reactive draft fields, reference fields, confirmed baseline, Signal Form `FieldTree`, dirty state, and
 * field/parse validation messages for one `FormService`. Constructed in the service's injection context.
 */
export class FormDraftState {
  private readonly input = signal<Signal<FormInput> | null>(null);
  private readonly given = computed<FormInput | null>(() => this.input()?.() ?? null);
  private readonly opening = computed<(FormInput & SplitDraft) | null>(() => {
    const input = this.given();
    return input ? { elements: input.elements, ...splitInputDraft(input.elements, input.value) } : null;
  });

  private readonly _model = linkedSignal<FormInput | null, FormModel | null>({
    source: this.given,
    computation: input => (input ? frozenCopy(inputModelOf(input.elements)) : null),
  });
  readonly model: Signal<FormModel | null> = this._model.asReadonly();
  readonly idScope: Signal<string>;
  private readonly _id = signal<number | null>(null);
  readonly id: Signal<number | null> = this._id.asReadonly();
  private readonly _ver = signal<number | null>(null);
  readonly ver: Signal<number | null> = this._ver.asReadonly();
  private readonly _fields = linkedSignal<(FormInput & SplitDraft) | null, Record<string, unknown>>({
    source: this.opening,
    computation: opening => opening?.fields ?? frozenCopy({}),
  });
  private readonly _references = linkedSignal<(FormInput & SplitDraft) | null, Record<string, unknown>>({
    source: this.opening,
    computation: opening => opening?.references ?? frozenCopy({}),
  });
  readonly value: Signal<Record<string, unknown>> = computed(() => frozenCopy({ ...this._fields(), ...this._references() }));
  private readonly _adoption = signal(0);
  readonly adoption: Signal<number> = this._adoption.asReadonly();
  private readonly _dataRevision = signal(0);
  readonly dataRevision: Signal<number> = this._dataRevision.asReadonly();
  private readonly _unavailable = signal<Unavailable>(frozenCopy({}));
  readonly unavailable: Signal<Unavailable> = this._unavailable.asReadonly();
  private readonly baseline = linkedSignal<(FormInput & SplitDraft) | null, FormInput | null>({
    source: this.opening,
    computation: opening => (opening ? { elements: opening.elements, value: untracked(this.value) } : null),
  });
  private readonly baselineText: Signal<string>;
  private readonly _messages = linkedSignal<Record<string, unknown>, readonly MessageDto[]>({
    source: this.value,
    computation: (value, previous) =>
      previous?.source ? retainFieldMessages(value, previous.value, previous.source as Record<string, unknown>) : [],
  });
  readonly messages: Signal<readonly MessageDto[]> = this._messages.asReadonly();

  readonly service = computed(() => this.model()?.service ?? null);
  readonly isReadOnly = computed(() => this.model()?.isReadOnly ?? true);
  readonly elements = computed<readonly FormElementDto[]>(() => (this.model()?.elements ?? []).filter(e => !e.isHidden));
  private readonly elementsByPath = computed(() => indexFormElements(this.model()?.elements ?? []));
  readonly isDirty: Signal<boolean>;

  private readonly refusals = new Set<(messages: readonly MessageDto[]) => void>();
  private readonly tree: FieldTree<Record<string, unknown>>;

  constructor(private readonly options: FormDraftStateOptions) {
    this.idScope = computed(() => `${this.input() ? 'ask' : 'form'}-${options.instance}`);
    this.baselineText = computed(() => {
      const baseline = this.baseline();
      return baseline ? canonicalJson(baseline.value, baseline.elements, options.zone()) : '[]';
    });
    this.isDirty = computed(() => {
      const value = this.value();
      const baseline = this.baseline();
      const elements = this.model()?.elements ?? [];
      if (this.elements().some(e => this.hasParseError(e))) {
        return true;
      }
      if (baseline && value === baseline.value) {
        return elements.some(e => isSecret(e) && !e.isDisabled && !e.isServerOwned && hasSecret(value[e.path]));
      }
      return (
        canonicalJson(value, elements, options.zone()) !== this.baselineText() ||
        elements.some(e => isSecret(e) && !e.isDisabled && !e.isServerOwned && hasSecret(value[e.path]))
      );
    });
    this.tree = form(
      this._fields,
      formFieldRules({
        elementOf: path => this.elementOf(path),
        isReadOnly: () => this.isReadOnly(),
        refusesInput: () => options.refusesInput(),
        messages: () => this.messages(),
        values: () => this.value(),
      }),
      { name: 'form' },
    );
  }

  setModel(model: FormModel | null): void {
    this._model.set(frozenCopy(model));
  }

  setInput(input: Signal<FormInput>): void {
    this.input.set(input);
  }

  setMessages(messages: readonly MessageDto[]): void {
    this._messages.set(frozenCopy(messages));
    this.tellRefused();
  }

  onRefused(handler: (messages: readonly MessageDto[]) => void): () => void {
    this.refusals.add(handler);
    return () => this.refusals.delete(handler);
  }

  bumpDataRevision(): void {
    this._dataRevision.update(v => v + 1);
  }

  elementOf(path: string): FormElementDto | undefined {
    return this.elementsByPath().get(path);
  }

  fieldOf<T>(path: string): FieldTree<T> {
    const element = this.elementOf(path);
    const key = element && element.referencePath === path ? element.path : path;
    const field = this.tree[key];
    if (!field) {
      throw new Error(`The form has no field at "${path}"`);
    }
    return field as unknown as FieldTree<T>;
  }

  valueOf(path: string): unknown {
    return this.value()[path];
  }

  setValueAt(path: string, value: unknown): void {
    const element = this.elementOf(path);
    if (element && element.referencePath === path && element.path !== path) {
      const field = this.tree[element.path];
      if (!field) {
        return;
      }
      const state = field();
      if (state.disabled() || state.readonly()) {
        return;
      }
      this._references.update(refs => frozenCopy({ ...refs, [path]: value }));
      return;
    }
    const field = this.tree[path];
    if (!field) {
      return;
    }
    const state = field();
    if (state.disabled() || state.readonly()) {
      return;
    }
    state.value.set(frozenCopy(value));
  }

  patchValueAt(path: string, value: unknown): void {
    if (this.isReadOnly() || this.options.refusesInput()) {
      return;
    }
    const element = this.elementOf(path);
    if (!element || element.isServerOwned || element.model.kind === 'FormInfo') {
      return;
    }
    if (!element.isDisabled && element.disabledWhen && evaluateFilterNode(element.disabledWhen, this.value())) {
      return;
    }
    if (element.referencePath === path && element.path !== path) {
      this._references.update(refs => frozenCopy({ ...refs, [path]: frozenCopy(value) }));
      return;
    }
    const field = this.tree[path];
    if (field) {
      field().value.set(frozenCopy(value));
    }
  }

  adoptData(data: FormDataDto | null | undefined): void {
    this._id.set(data?.id ?? null);
    this._ver.set(data?.ver ?? null);
    this._unavailable.set(frozenCopy(data?.unavailable ?? {}));
    const elements = this.model()?.elements ?? [];
    const { fields, references } = splitDraft(data?.value ?? {}, elements);
    this._fields.set(fields);
    this._references.set(references);
    this.baseline.set({ elements, value: untracked(this.value) });
    this._messages.set([]);
    this._adoption.update(count => count + 1);
  }

  absorb(affected: AffectedDto): boolean {
    const service = this.service();
    const id = this.id();
    const ver = this.ver();
    if (
      !service ||
      id === null ||
      ver === null ||
      !this.options.canAbsorb() ||
      affected.service !== service ||
      affected.id !== id ||
      affected.prevVer == null ||
      affected.ver == null ||
      affected.prevVer !== ver ||
      !isRecord(affected.value)
    ) {
      return false;
    }
    const elements = this.model()?.elements ?? [];
    const baselineValue = this.baseline()?.value ?? {};
    const { baseline, merged } = absorbDraft(untracked(this.value), baselineValue, affected.value, elements, this.options.zone(), e =>
      this.hasParseError(e),
    );
    this._ver.set(affected.ver);
    this._unavailable.set(frozenCopy(affected.unavailable ?? {}));
    this._fields.set(merged.fields);
    this._references.set(merged.references);
    this.baseline.set({ elements, value: baseline.value });
    return true;
  }

  refuseParseErrors(): readonly MessageDto[] {
    const messages = this.elements()
      .filter(e => this.hasParseError(e))
      .map<MessageDto>(e => ({ type: 'error', path: e.path, title: this.options.texts.of('FormBindingValue') }));
    if (messages.length) {
      for (const handler of this.refusals) {
        handler(messages);
      }
    }
    return messages;
  }

  showsMessage(path: string): boolean {
    const element = this.elementOf(path);
    return (
      !!element &&
      !element.isHidden &&
      (!element.visibleWhen || evaluateFilterNode(element.visibleWhen, this.value())) &&
      !isFieldDisabled(
        element,
        () => this.isReadOnly(),
        () => this.value(),
      )
    );
  }

  private hasParseError(element: FormElementDto): boolean {
    return (
      !element.isDisabled &&
      !element.isServerOwned &&
      (this.tree[element.path]?.()
        .errors()
        .some(err => err.kind === 'parse') ??
        false)
    );
  }

  private tellRefused(): void {
    if (this.messages().length) {
      for (const handler of this.refusals) {
        handler(this.messages());
      }
    }
  }
}
