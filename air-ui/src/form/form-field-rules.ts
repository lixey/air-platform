import {
  applyEach,
  disabled,
  type LimitKey,
  MAX,
  MAX_LENGTH,
  MAX_NUMBER,
  metadata,
  MIN,
  MIN_LENGTH,
  MIN_NUMBER,
  readonly,
  REQUIRED,
  type SchemaFn,
  validate,
  type ValidationError,
} from '@angular/forms/signals';
import type { FilterNode, FormElementDto, MessageDto } from '../core/generated/contracts';

/** What the field rules read of a form, as functions, so the rules follow the form without holding it. */
export interface FormFieldRulesSource {
  /** The element a field path names, by its own path or by its reference path. */
  readonly elementOf: (path: string) => FormElementDto | undefined;
  /** Whether the record is read only as a whole. */
  readonly isReadOnly: () => boolean;
  /** Whether the form refuses input now: a save or a recovery runs, or the screen is locked. */
  readonly refusesInput: () => boolean;
  /** The server's messages the form holds. */
  readonly messages: () => readonly MessageDto[];
  /** The current draft values of the form for evaluating dynamic field predicates. */
  readonly values?: () => Readonly<Record<string, unknown>>;
}

/**
 * The Signal Forms rules of a form's fields, the schema `form(...)` is given: the whole tree is
 * read only while the form refuses input; each field is disabled, required and limited as its
 * element says, and carries the server's messages that name its path.
 */
export function formFieldRules(source: FormFieldRulesSource): SchemaFn<Record<string, unknown>> {
  return root => {
    readonly(root, { when: () => source.refusesInput() });
    applyEach(root, field => {
      disabled(field, { when: ctx => isFieldDisabled(source.elementOf(ctx.key()), source.isReadOnly, source.values) });
      metadata(field, REQUIRED, ctx => isFieldRequired(source.elementOf(ctx.key()), source.values));
      metadata(field, MIN_LENGTH, ctx => lengthLimitOf(source.elementOf(ctx.key()), 'minLength'));
      metadata(field, MAX_LENGTH, ctx => lengthLimitOf(source.elementOf(ctx.key()), 'maxLength'));
      metadata(field, MIN_NUMBER, ctx => numericBoundOf(source.elementOf(ctx.key()), 'min'));
      metadata(field, MAX_NUMBER, ctx => numericBoundOf(source.elementOf(ctx.key()), 'max'));
      // Limit selection keys need a cast over an unknown-typed record model
      metadata(field, MIN, () => MIN_NUMBER as LimitKey<unknown>);
      metadata(field, MAX, () => MAX_NUMBER as LimitKey<unknown>);
      validate(field, ctx => fieldErrorsOf(source.messages(), ctx.key()));
    });
  };
}

/** Whether a field refuses input for good: no element names it, or the element or the record is closed to the user. */
export function isFieldDisabled(
  element: FormElementDto | undefined,
  isReadOnly: () => boolean,
  values?: () => Readonly<Record<string, unknown>>,
): boolean {
  if (!element || element.isDisabled || element.isServerOwned || isReadOnly()) {
    return true;
  }
  return Boolean(element.disabledWhen && values && evaluateFilterNode(element.disabledWhen, values()));
}

/** Whether a field is currently required: statically (`isRequired`) or dynamically (`requiredWhen`). */
export function isFieldRequired(element: FormElementDto | undefined, values?: () => Readonly<Record<string, unknown>>): boolean {
  if (!element) {
    return false;
  }
  if (element.isRequired) {
    return true;
  }
  return Boolean(element.requiredWhen && values && evaluateFilterNode(element.requiredWhen, values()));
}

/** Evaluates a declarative `FilterNode` predicate against the current form draft values. */
export function evaluateFilterNode(node: FilterNode | null | undefined, values: Readonly<Record<string, unknown>>): boolean {
  if (!node) {
    return true;
  }
  if (node.kind === 'FilterGroup') {
    return node.logic === 'or'
      ? node.items.some(item => evaluateFilterNode(item, values))
      : node.items.every(item => evaluateFilterNode(item, values));
  }
  const actual = values[node.field];
  const expected = node.value;
  switch (node.operator) {
    case 'isNull':
      return actual == null || actual === '';
    case 'isNotNull':
      return actual != null && actual !== '';
    case 'eq':
      return actual == expected || (actual != null && expected != null && String(actual) === String(expected));
    case 'neq':
      return actual != expected && (actual == null || expected == null || String(actual) !== String(expected));
    case 'gt':
      return Number(actual) > Number(expected);
    case 'gte':
      return Number(actual) >= Number(expected);
    case 'lt':
      return Number(actual) < Number(expected);
    case 'lte':
      return Number(actual) <= Number(expected);
    case 'contains':
      return typeof actual === 'string' && typeof expected === 'string' && actual.toLowerCase().includes(expected.toLowerCase());
    case 'in':
      return Array.isArray(expected) && expected.some(v => v == actual || String(v) === String(actual));
    case 'notIn':
      return !Array.isArray(expected) || !expected.some(v => v == actual || String(v) === String(actual));
    default:
      return true;
  }
}

/** The length limit of a text counted in UTF-16 units, the one the browser counts; none for other units. */
export function lengthLimitOf(element: FormElementDto | undefined, limit: 'minLength' | 'maxLength'): number | undefined {
  if (!element || element.constraints?.lengthUnit !== 'utf16') {
    return undefined;
  }
  return element.constraints[limit] ?? undefined;
}

/** The bound of a number element; none for other types. */
export function numericBoundOf(element: FormElementDto | undefined, bound: 'min' | 'max'): number | undefined {
  if (!element || element.type !== 'number') {
    return undefined;
  }
  const boundVal = element.constraints?.[bound];
  return typeof boundVal === 'number' ? boundVal : undefined;
}

/** The server's messages that name a path, as the field's errors: each message's title, or its detail when it has none. */
export function fieldErrorsOf(messages: readonly MessageDto[], path: string): readonly ValidationError.WithoutFieldTree[] {
  return messages.filter(m => m.path === path).map(m => ({ kind: 'server', message: (m.title ?? m.detail) || undefined }));
}
