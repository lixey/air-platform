import type { Type } from '@angular/core';
import { inject, Service } from '@angular/core';
import type { DynamicComponentLoader, ResolvedComponent } from './component-map';
import { DYNAMIC_COMPONENT_MAPS, isLoader } from './component-map';
import { checkedModelInput, composeComponentMaps } from './provide-dynamic-components';

/**
 * Resolves a kind against the maps of every `provideDynamicComponents` call, merged in declaration order when the
 * registry is created. A kind registered to a loader resolves to one loader until it settles: the class it answers,
 * checked for an input aliased to `model`, then replaces the loader, and a rejection is retried on the next resolve.
 */
@Service()
export class DynamicComponentRegistry {
  private readonly components = composeComponentMaps(inject(DYNAMIC_COMPONENT_MAPS));
  private readonly leafLoaders = new Map<string, () => Promise<Type<unknown>>>();

  resolve(kind: string | null | undefined): ResolvedComponent | undefined {
    if (!kind) {
      return undefined;
    }
    const component = this.components.get(kind);
    if (component === undefined) {
      return undefined;
    }
    return isLoader(component) ? this.leafLoader(kind, component) : component;
  }

  private leafLoader(kind: string, registered: DynamicComponentLoader): () => Promise<Type<unknown>> {
    const memo = this.leafLoaders.get(kind);
    if (memo !== undefined) {
      return memo;
    }
    const loader = async (): Promise<Type<unknown>> => {
      try {
        const loaded = await registered();
        const type = checkedModelInput(kind, 'default' in loaded ? loaded.default : loaded);
        this.components.set(kind, type);
        return type;
      } finally {
        if (this.leafLoaders.get(kind) === loader) {
          this.leafLoaders.delete(kind);
        }
      }
    };
    this.leafLoaders.set(kind, loader);
    return loader;
  }
}
