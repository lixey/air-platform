/**
 * How a component takes a server model and a template's inputs.
 *
 * Every component with a server contract accepts the whole contract through a `model` input
 * and each of its fields through an input of its own, and resolves them by one rule: a
 * field the template sets wins over the model, and the model wins over the library's
 * default. The server describes; the caller's template adjusts.
 *
 * ```ts
 * protected readonly view = computed(() =>
 *   resolveModel(BTN_DEFAULTS, this.model(), {label: this.label(), color: this.color()}),
 * );
 * ```
 *
 * `undefined` and `null` both mean "not said", on either side — a template input left
 * unbound and a JSON field a server left null are the same absence.
 */
export function resolveModel<T extends object>(defaults: T, model: PartialNullable<T> | null | undefined, inputs: PartialNullable<T>): T {
  return { ...defaults, ...definedOnly(model), ...definedOnly(inputs) };
}

/** A model or a set of inputs: every field optional, and `null` allowed where JSON may put it. */
export type PartialNullable<T> = { [K in keyof T]?: T[K] | null | undefined };

function definedOnly<T extends object>(source: PartialNullable<T> | null | undefined): Partial<T> {
  const result: Partial<T> = {};

  for (const key in source) {
    const value = source[key];

    if (value !== undefined && value !== null) {
      result[key] = value as T[typeof key];
    }
  }

  return result;
}
