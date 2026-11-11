import { hasZone, temporalText } from '../controls/temporal-text';
import type { FormDataDto, FormElementDto, FormModel, MessageDto } from '../core/generated/contracts';
import { isRecord } from '../core/json';

/**
 * The form service owns JSON values; callers cannot mutate nested state behind its guards.
 * Objects inside arrays stay extensible so Angular Signal Forms can stamp its private `identitySymbol` on array items.
 */
export function frozenCopy<T>(value: T, inArray = false): T {
  if (Array.isArray(value)) {
    return Object.freeze(value.map(item => frozenCopy(item, true))) as T;
  }
  if (value !== null && typeof value === 'object') {
    const copy = Object.fromEntries(Object.entries(value).map(([key, item]) => [key, frozenCopy(item, false)]));
    return (inArray ? copy : Object.freeze(copy)) as T;
  }
  return value;
}

export function isSecret(element: FormElementDto): boolean {
  return Boolean(element.isSecret || element.type === 'password');
}

export function hasSecret(value: unknown): boolean {
  return typeof value === 'string' && value.length > 0;
}

/**
 * Sanitizes form draft key-values before sending across reference queries:
 * strips elements marked secret (e.secret) (Security Rules 2 & 3).
 */
export function sanitizeFormDraft(value: Record<string, unknown>, elements?: readonly FormElementDto[]): Record<string, unknown> {
  const secretPaths = new Set((elements ?? []).filter(isSecret).map(e => e.path));
  const sanitized: Record<string, unknown> = {};
  for (const [key, val] of Object.entries(value)) {
    if (secretPaths.has(key)) {
      continue;
    }
    sanitized[key] = val;
  }
  return sanitized;
}

/**
 * Removes secret fields that the user did not type into before posting a form save.
 */
export function stripUntouchedSecrets(
  rawValue: Readonly<Record<string, unknown>>,
  elements: readonly FormElementDto[],
): Record<string, unknown> {
  const value = { ...rawValue };
  for (const element of elements.filter(isSecret)) {
    if (!hasSecret(rawValue[element.path])) {
      delete value[element.path];
    }
  }
  return value;
}

/**
 * A form record by the contract: an object whose `value` is an object. The server always answers a
 * `value` (`FormDataDto.Value` is a dictionary it initialises), so `{ value: null }` and an array
 * are not a form record.
 */
export function isFormData(value: unknown): value is FormDataDto {
  return isRecord(value) && isRecord(value['value']);
}

/**
 * The draft a form over `elements` holds for `value`: no secret path, and null at every path and caption path the
 * value does not carry.
 */
export function draftOf(value: Readonly<Record<string, unknown>>, elements: readonly FormElementDto[]): Record<string, unknown> {
  const draft = { ...value };
  for (const element of elements.filter(isSecret)) {
    delete draft[element.path];
  }
  for (const element of elements) {
    if (!Object.hasOwn(draft, element.path)) {
      draft[element.path] = null;
    }
    if (element.referencePath && !Object.hasOwn(draft, element.referencePath)) {
      draft[element.referencePath] = null;
    }
  }
  return draft;
}

export interface SplitDraft {
  readonly fields: Record<string, unknown>;
  readonly references: Record<string, unknown>;
  readonly value: Record<string, unknown>;
}

/**
 * Splits a form draft into the editable field values (passed to Angular Signal Forms `form()`)
 * and the display-only reference captions (`referencePath`), so Signal Forms never walks `{ id, name }` objects.
 */
export function splitDraft(value: Readonly<Record<string, unknown>>, elements: readonly FormElementDto[]): SplitDraft {
  const draft = draftOf(value, elements);
  const elementPaths = new Set(elements.map(e => e.path));
  const referencePaths = new Set(elements.map(e => e.referencePath).filter((p): p is string => Boolean(p) && !elementPaths.has(p!)));
  const fields: Record<string, unknown> = {};
  const references: Record<string, unknown> = {};
  for (const [key, item] of Object.entries(draft)) {
    if (referencePaths.has(key)) {
      references[key] = item;
    } else {
      fields[key] = item;
    }
  }
  const frozenFields = frozenCopy(fields);
  const frozenRefs = frozenCopy(references);
  return {
    fields: frozenFields,
    references: frozenRefs,
    value: frozenCopy({ ...frozenFields, ...frozenRefs }),
  };
}

/** The model of a form of input alone over `elements`: no service, no text of a save. */
export function inputModelOf(elements: readonly FormElementDto[]): FormModel {
  return {
    kind: 'Form',
    service: '',
    isReadOnly: false,
    elements: [...elements],
    dirtyDialog: { kind: 'OverlayDirty', title: '', description: '', saveLabel: '', discardLabel: '', continueLabel: '' },
    mutationTexts: {
      committedWarningTitle: '',
      conflictTitle: '',
      reloadLabel: '',
      selectionCorrectionLabel: '',
      unknownDescription: '',
      unknownTitle: '',
      verifyLabel: '',
    },
  };
}

/**
 * Only editable inputs contribute; reference captions and secret commands have no baseline. A date cut of an instant
 * is its day on the clock of `zone`, the user's, as the form shows it.
 */
export function canonicalJson(value: Record<string, unknown>, elements: readonly FormElementDto[], zone: string): string {
  const elementPaths = new Set(elements.map(e => e.path));
  const ignoredPaths = new Set<string>();
  for (const e of elements) {
    if (e.isDisabled || e.isServerOwned || isSecret(e)) {
      ignoredPaths.add(e.path);
    }
    if (e.referencePath && !elementPaths.has(e.referencePath)) {
      ignoredPaths.add(e.referencePath);
    }
  }

  const entries: unknown[] = elements
    .filter(e => !e.isDisabled && !e.isServerOwned && !isSecret(e))
    .map(element => {
      let field = value[element.path];
      if (element.valueKind === 'list' && Array.isArray(field)) {
        field = [...new Set(field.map(item => JSON.stringify(stable(item))))].sort();
      } else if (element.temporalType && typeof field === 'string') {
        field = temporalKey(field, element, zone);
      }
      return [element.path, Object.hasOwn(value, element.path), stable(field)];
    });

  const extraKeys = Object.keys(value)
    .filter(k => !elementPaths.has(k) && !ignoredPaths.has(k))
    .sort();
  for (const key of extraKeys) {
    entries.push([key, true, stable(value[key])]);
  }

  return JSON.stringify(entries);
}

export function stableJson(value: unknown): string {
  return JSON.stringify(stable(value));
}

export function sameFieldValue(a: unknown, b: unknown): boolean {
  return Object.is(a, b) || (typeof a === 'object' && a !== null && typeof b === 'object' && b !== null && stableJson(a) === stableJson(b));
}

/** Keeps field messages whose target field value has not structurally changed since `previousSource`. */
export function retainFieldMessages(
  value: Readonly<Record<string, unknown>>,
  previousValue: readonly MessageDto[],
  previousSource: Readonly<Record<string, unknown>>,
): readonly MessageDto[] {
  return previousValue.filter(m => !m.path || sameFieldValue(value[m.path], previousSource[m.path]));
}

/** Indexes form elements by both `path` and `referencePath` for O(1) lookup. */
export function indexFormElements(elements: readonly FormElementDto[]): ReadonlyMap<string, FormElementDto> {
  const map = new Map<string, FormElementDto>();
  for (const element of elements) {
    if (element.path) {
      map.set(element.path, element);
    }
    if (element.referencePath) {
      map.set(element.referencePath, element);
    }
  }
  return map;
}

/** Prepares the initial split draft for an input-only form, defaulting missing checkboxes to `false`. */
export function splitInputDraft(elements: readonly FormElementDto[], value: Readonly<Record<string, unknown>>): SplitDraft {
  const unchecked = Object.fromEntries(elements.filter(element => element.type === 'checkbox').map(element => [element.path, false]));
  return splitDraft({ ...unchecked, ...value }, elements);
}

/**
 * Merges a server-answered record (`serverValue`) into an open form's current draft (`currentValue`) relative to its
 * previous confirmed `baselineValue`: server-owned and disabled elements, as well as editable elements whose current
 * value still matches `baselineValue` in `zone` (and carry no parse error), adopt `serverValue`, while user-edited
 * elements and typed secrets stay as the user holds them.
 */
export function absorbDraft(
  currentValue: Readonly<Record<string, unknown>>,
  baselineValue: Readonly<Record<string, unknown>>,
  serverValue: Readonly<Record<string, unknown>>,
  elements: readonly FormElementDto[],
  zone: string,
  hasParseError: (element: FormElementDto) => boolean,
): { readonly baseline: SplitDraft; readonly merged: SplitDraft } {
  const baseline = splitDraft(serverValue, elements);
  const mergedRaw: Record<string, unknown> = { ...baseline.fields, ...baseline.references };
  const elementPaths = new Set(elements.map(e => e.path));
  const referencePaths = new Set(elements.map(e => e.referencePath).filter((p): p is string => Boolean(p) && !elementPaths.has(p!)));

  for (const element of elements) {
    if (element.isDisabled || element.isServerOwned) {
      continue;
    }
    if (isSecret(element)) {
      if (hasSecret(currentValue[element.path])) {
        mergedRaw[element.path] = currentValue[element.path];
      }
      continue;
    }
    const isElementDirty =
      hasParseError(element) ||
      canonicalJson(pickElementValue(currentValue, element.path), [element], zone) !==
        canonicalJson(pickElementValue(baselineValue, element.path), [element], zone);
    if (isElementDirty) {
      mergedRaw[element.path] = currentValue[element.path];
      if (element.referencePath && !elementPaths.has(element.referencePath) && Object.hasOwn(currentValue, element.referencePath)) {
        mergedRaw[element.referencePath] = currentValue[element.referencePath];
      }
    }
  }

  for (const key of Object.keys(currentValue)) {
    if (elementPaths.has(key) || referencePaths.has(key)) {
      continue;
    }
    if (stableJson(currentValue[key]) !== stableJson(baselineValue[key])) {
      mergedRaw[key] = currentValue[key];
    }
  }

  const mergedFields: Record<string, unknown> = {};
  const mergedRefs: Record<string, unknown> = {};
  for (const [key, item] of Object.entries(mergedRaw)) {
    if (referencePaths.has(key)) {
      mergedRefs[key] = item;
    } else {
      mergedFields[key] = item;
    }
  }
  const fields = frozenCopy(mergedFields);
  const references = frozenCopy(mergedRefs);
  return {
    baseline,
    merged: { fields, references, value: frozenCopy({ ...fields, ...references }) },
  };
}

function pickElementValue(source: Readonly<Record<string, unknown>>, path: string): Record<string, unknown> {
  return Object.hasOwn(source, path) ? { [path]: source[path] } : {};
}

function stable(value: unknown): unknown {
  if (Array.isArray(value)) {
    return ['array', ...value.map(stable)];
  }
  if (value !== null && typeof value === 'object') {
    return [
      'object',
      ...Object.keys(value)
        .sort()
        .map(key => [key, stable((value as Record<string, unknown>)[key])]),
    ];
  }
  return [typeof value, value];
}

function temporalKey(value: string, element: FormElementDto, zone: string): string {
  if (element.temporalType === 'dateOnly' || element.type === 'date') {
    return temporalText(value, element, zone).slice(0, 10);
  }
  if (hasZone(value)) {
    const instant = Date.parse(value);
    if (Number.isFinite(instant)) {
      // JS dates retain milliseconds; keep the remaining server precision separately.
      const remainder = (value.match(/\.(\d+)/)?.[1].slice(3) ?? '').replace(/0+$/, '');
      return `${instant}:${remainder}`;
    }
  }
  return value
    .replace(/\.0+$/, '')
    .replace(/(\.\d*?)0+$/, '$1')
    .replace(/T(\d{2}:\d{2})$/, 'T$1:00');
}
