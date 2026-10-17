import type { InputSignalWithTransform, Type } from '@angular/core';
import { InjectionToken } from '@angular/core';
import type { CellKinds, FormElementKinds, HeaderKinds, NavElementKinds, PanelKinds } from '../generated/contracts';

/**
 * Every kind of every slot with its model: the platform's, generated; the library's own and an application's, added
 * by augmenting the slot maps of the generated contracts.
 */
export type ComponentModels = PanelKinds & NavElementKinds & CellKinds & HeaderKinds & FormElementKinds;

/** A lazy loader: an arrow or async function answering the component class or a `{ default }` module of it. */
export type DynamicComponentLoader = () => Promise<Type<unknown> | { default: Type<unknown> }>;

/** What a kind is registered to: a component class or a lazy loader of one. */
export type DynamicComponentType = Type<unknown> | DynamicComponentLoader;

/** What the registry resolves a kind to: a component class, or a loader answering the class. */
export type ResolvedComponent = Type<unknown> | (() => Promise<Type<unknown>>);

/** The input through which an outlet hands a component the model of its kind. */
export const MODEL_INPUT = 'model';

/** A flat map from kind to component, the shape every registration has. */
export type ComponentMap = Readonly<Record<string, DynamicComponentType>>;

declare const CHECKED: unique symbol;

/**
 * A map `defineComponents` checked, the only map `provideDynamicComponents` and `overriding` take: a raw object literal
 * does not compile there. The brand is a type alone; at runtime the map is the object written.
 */
export type CheckedComponentMap<R extends object = ComponentMap> = R & { readonly [CHECKED]: true };

/** A map whose kinds replace those an earlier registration holds; see {@link overriding}. */
export interface OverridingComponentMap {
  readonly components: CheckedComponentMap;
  readonly overrides: readonly string[];
}

export class ComponentMapError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'ComponentMapError';
  }
}

/**
 * The maps of every `provideDynamicComponents` call in declaration order: the calls in provider order, the maps of a
 * call in argument order. The registry applies them by that order when it is created.
 */
export const DYNAMIC_COMPONENT_MAPS = new InjectionToken<readonly (ComponentMap | OverridingComponentMap)[]>('DYNAMIC_COMPONENT_MAPS', {
  providedIn: 'root',
  factory: () => [],
});

/**
 * What a `model` input takes: the write type of an `input()`, an `input.required()` or a `model()`. The read type is
 * inferred too, since an input signal is invariant in it and `unknown` would match none.
 */
export type ModelInputAccepts<S> = S extends InputSignalWithTransform<infer _Read, infer W> ? W : never;

/** The component a registry entry stands for: the class itself, or what a lazy loader resolves to. */
export type LoadedComponent<E> = E extends () => Promise<infer C> ? (C extends { default: infer D } ? D : C) : E;

/** Whether `T` is `any`, which would pass every check below. */
type IsAny<T> = 0 extends 1 & T ? true : false;

/**
 * A registry checked against a map from kind to model `M`: every key is a kind of `M`, every value a component or a
 * lazy loader of one, and a component that declares a `model` input takes the kind's model. A component without a
 * `model` input takes any kind: it reads its state elsewhere. An entry that fails turns into a message type, so the
 * compiler names the kind and the reason.
 */
export type CheckedComponents<R, M = ComponentModels> = {
  [K in keyof R]: K extends keyof M ? CheckedEntry<K & string, R[K], M[K]> : `${K & string}: no such kind`;
};

/**
 * One entry against its kind's model: the entry when it passes, a message naming the kind when it fails. `any` in
 * place of the component or of what its `model` input takes is refused, and so is a `model` that is not public: the
 * outlet sets it all the same, and the check could not see it.
 */
type CheckedEntry<K extends string, E, Model> =
  IsAny<LoadedComponent<E>> extends true
    ? `${K}: typed any`
    : LoadedComponent<E> extends Type<infer I>
      ? IsAny<I> extends true
        ? `${K}: typed any`
        : I extends Omit<I, 'model'> & { model?: unknown }
          ? I extends { model: infer S }
            ? IsAny<ModelInputAccepts<S>> extends true
              ? `${K}: the model input takes any`
              : [Model] extends [ModelInputAccepts<S>]
                ? E
                : `${K}: the model input takes another type`
            : E
          : `${K}: the model input is not public`
      : `${K}: not a component or a loader`;

/**
 * A registry checked by the compiler: an unknown kind, a value that is neither a component nor a loader, and a
 * component whose `model` input does not take its kind's model fail the build. State that it covers a set of kinds
 * with `satisfies Record<SomeKind, unknown>`. The map it answers is branded as checked.
 */
export function defineComponents<const R extends object>(registry: R & CheckedComponents<R>): CheckedComponentMap<R> {
  return registry as CheckedComponentMap<R>;
}

/**
 * A registration whose kinds replace those an earlier one holds, each named: a kind registered twice without being
 * named here fails when the maps are composed.
 */
export function overriding<const R extends ComponentMap>(
  components: CheckedComponentMap<R>,
  overrides: readonly (keyof R & string)[],
): OverridingComponentMap {
  return { components, overrides };
}

/** Whether a registered value is a lazy loader: an arrow or async function has no prototype, a component class has. */
export function isLoader(component: DynamicComponentType): component is DynamicComponentLoader {
  return component.prototype === undefined;
}
