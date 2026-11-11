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
      metadata(field, REQUIRED, ctx => Boolean(source.elementOf(ctx.key())?.isRequired));
      metadata(field, MIN_LENGTH, ctx => lengthLimitOf(source.elementOf(ctx.key()), 'minLength'));
      metadata(field, MAX_LENGTH, ctx => lengthLimitOf(source.elementOf(ctx.key()), 'maxLength'));
      metadata(field, MIN_NUMBER, ctx => numericBoundOf(source.elementOf(ctx.key()), 'min'));
      metadata(field, MAX_NUMBER, ctx => numericBoundOf(source.elementOf(ctx.key()), 'max'));
      // Limit selection keys need a cast over an unknown-typed record model
      metadata(field, MIN, () => MIN_NUMBER as LimitKey<unknown>);
      metadata(field, MAX, () => MAX_NUMBER as LimitKey<unknown>);
      validate(field, ctx => {
        const errors = [...fieldErrorsOf(source.messages(), ctx.key())];
        const el = source.elementOf(ctx.key());
        const val = ctx.value();
        if (
          el?.type === 'number' &&
          el.constraints?.exclusiveMin === true &&
          typeof el.constraints.min === 'number' &&
          typeof val === 'number' &&
          val <= el.constraints.min
        ) {
          errors.push({ kind: 'min' });
        }
        return errors;
      });
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

const ISO_DATE_ONLY_RE = /^(\d{4})-(\d{2})-(\d{2})$/;
const ISO_TEMPORAL_PREFIX_RE = /^\d{4}-\d{2}-\d{2}(?:$|[T\s])/;

/** Evaluates a declarative `FilterNode` predicate against the current form draft values. */
export function evaluateFilterNode(node: FilterNode | null | undefined, values: Readonly<Record<string, unknown>>): boolean {
  if (!node) {
    return true;
  }
  if (node.kind === 'FilterGroup') {
    if (node.items.length === 0) {
      return true;
    }
    return node.logic?.toLowerCase() === 'or'
      ? node.items.some(item => evaluateFilterNode(item, values))
      : node.items.every(item => evaluateFilterNode(item, values));
  }
  if (!node.field) {
    return false;
  }
  const actual = readFieldValue(values, node.field);
  const expected = node.value;
  switch (node.operator) {
    case 'isNull':
      return isEmptyValue(actual);
    case 'isNotNull':
      return !isEmptyValue(actual);
    case 'equals':
      return areEqualValues(actual, expected);
    case 'notEquals':
      return !areEqualValues(actual, expected);
    case 'greaterThan': {
      const cmp = compareOrderedValues(actual, expected);
      return cmp !== undefined && cmp > 0;
    }
    case 'greaterThanOrEqual': {
      const cmp = compareOrderedValues(actual, expected);
      return cmp !== undefined && cmp >= 0;
    }
    case 'lessThan': {
      const cmp = compareOrderedValues(actual, expected);
      return cmp !== undefined && cmp < 0;
    }
    case 'lessThanOrEqual': {
      const cmp = compareOrderedValues(actual, expected);
      return cmp !== undefined && cmp <= 0;
    }
    case 'contains':
      return matchesContains(actual, expected);
    case 'notContains':
      return !matchesContains(actual, expected);
    case 'startsWith':
      return typeof actual === 'string' && typeof expected === 'string' && actual.toLowerCase().startsWith(expected.toLowerCase());
    case 'endsWith':
      return typeof actual === 'string' && typeof expected === 'string' && actual.toLowerCase().endsWith(expected.toLowerCase());
    case 'in':
      return matchesIn(actual, expected);
    case 'notIn':
      return !matchesIn(actual, expected);
    case 'matches':
      return matchesRegex(actual, expected);
    case 'notMatches':
      return !matchesRegex(actual, expected);
    default:
      return false;
  }
}

function readFieldValue(values: Readonly<Record<string, unknown>>, field: string): unknown {
  if (Object.prototype.hasOwnProperty.call(values, field)) {
    return values[field];
  }
  if (!field.includes('.')) {
    const lower = field.toLowerCase();
    for (const key of Object.keys(values)) {
      if (key.toLowerCase() === lower) {
        return values[key];
      }
    }
    return undefined;
  }
  let current: unknown = values;
  for (const segment of field.split('.')) {
    if (current == null || typeof current !== 'object' || Array.isArray(current)) {
      return undefined;
    }
    const rec = current as Record<string, unknown>;
    if (Object.prototype.hasOwnProperty.call(rec, segment)) {
      current = rec[segment];
      continue;
    }
    const lowerSeg = segment.toLowerCase();
    const matchedKey = Object.keys(rec).find(k => k.toLowerCase() === lowerSeg);
    if (!matchedKey) {
      return undefined;
    }
    current = rec[matchedKey];
  }
  return current;
}

function isEmptyValue(val: unknown): boolean {
  return val == null || val === '' || (Array.isArray(val) && val.length === 0);
}

function tryParseBoolean(val: unknown): boolean | undefined {
  if (typeof val === 'boolean') {
    return val;
  }
  if (typeof val === 'string') {
    const normalized = val.trim().toLowerCase();
    if (normalized === 'true') {
      return true;
    }
    if (normalized === 'false') {
      return false;
    }
  }
  return undefined;
}

function tryParseFiniteNumber(val: unknown): number | undefined {
  if (typeof val === 'number') {
    return Number.isFinite(val) ? val : undefined;
  }
  if (typeof val === 'string') {
    const trimmed = val.trim();
    if (!trimmed) {
      return undefined;
    }
    const num = Number(trimmed);
    return Number.isFinite(num) ? num : undefined;
  }
  return undefined;
}

function tryParseTemporalMs(val: unknown): number | undefined {
  if (val instanceof Date) {
    const ms = val.getTime();
    return Number.isFinite(ms) ? ms : undefined;
  }
  if (typeof val !== 'string' || !ISO_TEMPORAL_PREFIX_RE.test(val)) {
    return undefined;
  }
  const dateOnlyMatch = ISO_DATE_ONLY_RE.exec(val);
  if (dateOnlyMatch) {
    const year = Number(dateOnlyMatch[1]);
    const month = Number(dateOnlyMatch[2]);
    const day = Number(dateOnlyMatch[3]);
    const ms = Date.UTC(year, month - 1, day);
    const check = new Date(ms);
    return check.getUTCFullYear() === year && check.getUTCMonth() === month - 1 && check.getUTCDate() === day ? ms : undefined;
  }
  const parsed = Date.parse(val);
  return Number.isFinite(parsed) ? parsed : undefined;
}

function areEqualValues(left: unknown, right: unknown): boolean {
  if (left == null && right == null) {
    return true;
  }
  if (left == null || right == null) {
    return false;
  }
  if (typeof left === 'boolean' || typeof right === 'boolean') {
    const lb = tryParseBoolean(left);
    const rb = tryParseBoolean(right);
    return lb !== undefined && rb !== undefined && lb === rb;
  }
  if (typeof left === 'number' || typeof right === 'number') {
    const ln = tryParseFiniteNumber(left);
    const rn = tryParseFiniteNumber(right);
    return ln !== undefined && rn !== undefined && ln === rn;
  }
  const lt = tryParseTemporalMs(left);
  const rt = tryParseTemporalMs(right);
  if (lt !== undefined && rt !== undefined) {
    return lt === rt;
  }
  return String(left).toLowerCase() === String(right).toLowerCase();
}

function compareOrderedValues(left: unknown, right: unknown): number | undefined {
  if (isEmptyValue(left) || isEmptyValue(right)) {
    return undefined;
  }
  if (typeof left === 'boolean' || typeof right === 'boolean') {
    return undefined;
  }
  if (typeof left === 'number' || typeof right === 'number') {
    const ln = tryParseFiniteNumber(left);
    const rn = tryParseFiniteNumber(right);
    if (ln === undefined || rn === undefined) {
      return undefined;
    }
    return ln < rn ? -1 : ln > rn ? 1 : 0;
  }
  const lt = tryParseTemporalMs(left);
  const rt = tryParseTemporalMs(right);
  if (lt !== undefined && rt !== undefined) {
    return lt < rt ? -1 : lt > rt ? 1 : 0;
  }
  if (typeof left === 'string' && typeof right === 'string') {
    const ls = left.toLowerCase();
    const rs = right.toLowerCase();
    return ls < rs ? -1 : ls > rs ? 1 : 0;
  }
  return undefined;
}

function matchesContains(actual: unknown, expected: unknown): boolean {
  if (actual == null || expected == null) {
    return false;
  }
  if (typeof actual === 'string' && typeof expected === 'string') {
    return actual.toLowerCase().includes(expected.toLowerCase());
  }
  if (Array.isArray(actual)) {
    return actual.some(item => areEqualValues(item, expected));
  }
  return false;
}

function matchesIn(actual: unknown, expected: unknown): boolean {
  if (actual == null || !Array.isArray(expected)) {
    return false;
  }
  if (Array.isArray(actual)) {
    return actual.some(item => expected.some(exp => areEqualValues(item, exp)));
  }
  return expected.some(exp => areEqualValues(actual, exp));
}

function matchesRegex(actual: unknown, expected: unknown): boolean {
  if (typeof actual !== 'string' || typeof expected !== 'string' || expected.length === 0) {
    return false;
  }
  try {
    return new RegExp(expected, 'i').test(actual);
  } catch {
    return false;
  }
}

/** The length limit of a text counted in UTF-16 units, the one the browser counts; none for other units. */
export function lengthLimitOf(element: FormElementDto | undefined, limit: 'minLength' | 'maxLength'): number | undefined {
  if (!element || element.constraints?.lengthUnit !== 'utf16') {
    return undefined;
  }
  return element.constraints[limit] ?? undefined;
}

/** The bound of a number element; none for other types or exclusive bounds handled in `validate`. */
export function numericBoundOf(element: FormElementDto | undefined, bound: 'min' | 'max'): number | undefined {
  if (!element || element.type !== 'number' || (bound === 'min' && element.constraints?.exclusiveMin === true)) {
    return undefined;
  }
  const boundVal = element.constraints?.[bound];
  return typeof boundVal === 'number' ? boundVal : undefined;
}

/** The server's messages that name a path, as the field's errors: each message's title, or its detail when it has none. */
export function fieldErrorsOf(messages: readonly MessageDto[], path: string): readonly ValidationError.WithoutFieldTree[] {
  return messages.filter(m => m.path === path).map(m => ({ kind: 'server', message: (m.title ?? m.detail) || undefined }));
}
