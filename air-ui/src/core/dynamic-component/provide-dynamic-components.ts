import type { EnvironmentProviders, Type } from '@angular/core';
import { makeEnvironmentProviders, reflectComponentType } from '@angular/core';
import type { CheckedComponentMap, ComponentMap, DynamicComponentType, OverridingComponentMap } from './component-map';
import { ComponentMapError, DYNAMIC_COMPONENT_MAPS, isLoader, MODEL_INPUT } from './component-map';

/** A kind is PascalCase, without dots: the name of its model without `Model`. */
const KIND = /^[A-Z][A-Za-z0-9]*$/;

/**
 * Registers maps in declaration order: the calls in provider order, the maps of a call in argument order. A kind
 * registered twice throws unless the later registration names it in its overrides. Only a map `defineComponents`
 * checked compiles here; an entry may be a loader, whose component loads when its kind is first mounted.
 */
export function provideDynamicComponents(...maps: readonly (CheckedComponentMap | OverridingComponentMap)[]): EnvironmentProviders {
  composeComponentMaps(maps);
  return makeEnvironmentProviders(maps.map(map => ({ provide: DYNAMIC_COMPONENT_MAPS, multi: true, useValue: map })));
}

/**
 * The maps merged in declaration order. `provideDynamicComponents` composes them as well, so a map that fails throws
 * where it is provided rather than on the first kind resolved.
 */
export function composeComponentMaps(maps: readonly (ComponentMap | OverridingComponentMap)[]): Map<string, DynamicComponentType> {
  const composed = new Map<string, DynamicComponentType>();
  for (const map of maps) {
    mergeComponentMaps(composed, map);
  }
  return composed;
}

/**
 * Applies a map to the target: a kind already there throws, unless the map names it in its overrides. The whole map is
 * checked before any entry is written, so a map that fails leaves the target as it was.
 */
export function mergeComponentMaps(target: Map<string, DynamicComponentType>, map: ComponentMap | OverridingComponentMap): void {
  const { entries, overrides } = componentEntries(map);
  const twice = entries.find(([kind]) => target.has(kind) && !overrides.includes(kind));
  if (twice !== undefined) {
    throw new ComponentMapError(`Kind "${twice[0]}" is registered twice; name it in the overrides of the later registration`);
  }
  for (const [kind, value] of entries) {
    target.set(kind, value);
  }
}

/**
 * The entries of a map and the kinds it names as overrides. Every key must be a kind and every value a component class
 * or a loader; the first that is not throws, naming the kind.
 */
function componentEntries(map: ComponentMap | OverridingComponentMap): {
  readonly entries: readonly (readonly [string, DynamicComponentType])[];
  readonly overrides: readonly string[];
} {
  const { components, overrides } = isOverriding(map) ? map : { components: map, overrides: [] };
  const entries = Object.entries(components).map(([kind, value]): [string, DynamicComponentType] => {
    if (!KIND.test(kind)) {
      throw new ComponentMapError(`"${kind}" is not a kind: a kind is PascalCase and has no dots`);
    }
    return [kind, entryOf(kind, value)];
  });
  return { entries, overrides };
}

function entryOf(kind: string, value: unknown): DynamicComponentType {
  if (typeof value !== 'function') {
    throw new ComponentMapError(`Kind "${kind}" must be registered to a component class or a loader function`);
  }
  const entry = value as DynamicComponentType;
  if (isLoader(entry)) {
    return entry;
  }
  if (reflectComponentType(entry) === null) {
    throw new ComponentMapError(`Kind "${kind}" is neither a component class nor a loader; write a loader as an arrow function`);
  }
  return checkedModelInput(kind, entry);
}

/**
 * The component, unless one of its inputs is aliased to `model`: the compiler checks the property `model`, and an input
 * aliased to it would take the model unchecked. A class a map holds is checked when the map is merged, a class a
 * loader answers when it loads.
 */
export function checkedModelInput(kind: string, component: Type<unknown>): Type<unknown> {
  const aliased = reflectComponentType(component)?.inputs.find(
    ({ propName, templateName }) => templateName === MODEL_INPUT && propName !== MODEL_INPUT,
  );
  if (aliased !== undefined) {
    throw new ComponentMapError(`Kind "${kind}": the input "${aliased.propName}" is aliased to "model"; name the model input model`);
  }
  return component;
}

function isOverriding(value: ComponentMap | OverridingComponentMap): value is OverridingComponentMap {
  const keys = Object.keys(value);
  return keys.length === 2 && keys.includes('components') && keys.includes('overrides') && Array.isArray(value['overrides']);
}
