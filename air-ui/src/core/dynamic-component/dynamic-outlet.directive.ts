import type { ComponentRef, Type } from '@angular/core';
import {
  computed,
  DestroyRef,
  Directive,
  effect,
  ErrorHandler,
  inject,
  input,
  reflectComponentType,
  signal,
  ViewContainerRef,
} from '@angular/core';
import type { ResolvedComponent } from './component-map';
import { isLoader, MODEL_INPUT } from './component-map';
import { DynamicComponentRegistry } from './dynamic-component-registry';

export type DynamicOutletInputs = Readonly<Record<string, unknown>>;

/** A component to mount, and whether it stands for the fallback kind rather than the kind asked for. */
interface Resolution<T> {
  readonly component: T;
  readonly isFallback: boolean;
}

/**
 * Mounts the component registered under a kind. What to mount is derived from the kind and the fallback kind; a
 * lazy loader lands in `mounted` when its promise settles, and the inputs are re-applied whenever they change. A
 * fallback is given every input but `model`: the model is the unregistered kind's, not the fallback's.
 */
@Directive({
  selector: '[uiDynamicOutlet]',
  exportAs: 'uiDynamicOutlet',
})
export class DynamicOutletDirective {
  private readonly container = inject(ViewContainerRef);
  private readonly registry = inject(DynamicComponentRegistry);
  private readonly errorHandler = inject(ErrorHandler);

  readonly uiDynamicOutlet = input<string | null | undefined>();
  readonly uiDynamicOutletFallback = input<string | null | undefined>();
  readonly uiDynamicOutletInputs = input<DynamicOutletInputs | null | undefined>();

  private readonly mounted = signal<Resolution<Type<unknown>> | undefined>(undefined);

  private readonly resolved = computed(() => resolutionOf(this.registry, this.uiDynamicOutlet(), this.uiDynamicOutletFallback()), {
    equal: (a, b) => a?.component === b?.component && a?.isFallback === b?.isFallback,
  });

  private requestId = 0;
  private ref?: ComponentRef<unknown>;
  private inputNames: ReadonlyMap<string, string> = new Map();
  private appliedInputs = new Map<string, unknown>();

  private readonly resolveEffect = effect(() => {
    const resolved = this.resolved();
    const requestId = ++this.requestId;
    if (resolved === undefined) {
      this.mounted.set(undefined);
      return;
    }
    const { component, isFallback } = resolved;
    if (!isLoader(component)) {
      this.mounted.set({ component, isFallback });
      return;
    }
    this.mounted.set(undefined);
    component().then(
      loaded => {
        if (this.requestId === requestId) {
          this.mounted.set({ component: loaded, isFallback });
        }
      },
      (error: unknown) => {
        if (this.requestId === requestId) {
          this.errorHandler.handleError(error);
        }
      },
    );
  });

  private readonly renderEffect = effect(() => {
    const mounted = this.mounted();
    const inputs = this.uiDynamicOutletInputs() ?? {};
    if (mounted === undefined) {
      this.destroyComponent();
      return;
    }
    if (this.ref?.componentType !== mounted.component) {
      this.createComponent(mounted.component);
    }
    this.applyInputs(mounted.isFallback ? withoutModel(inputs) : inputs);
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.destroyComponent());
  }

  get componentRef(): ComponentRef<unknown> | undefined {
    return this.ref;
  }

  private createComponent(type: Type<unknown>): void {
    this.destroyComponent();
    this.ref = this.container.createComponent(type);
    this.inputNames = inputNamesOf(type);
  }

  private applyInputs(inputs: DynamicOutletInputs): void {
    if (this.ref === undefined) {
      return;
    }
    const next = new Map<string, unknown>();
    for (const [name, value] of Object.entries(inputs)) {
      const templateName = this.inputNames.get(name);
      if (templateName !== undefined) {
        next.set(templateName, value);
      }
    }
    for (const name of this.appliedInputs.keys()) {
      if (!next.has(name)) {
        this.ref.setInput(name, undefined);
      }
    }
    for (const [name, value] of next) {
      if (!this.appliedInputs.has(name) || !Object.is(this.appliedInputs.get(name), value)) {
        this.ref.setInput(name, value);
      }
    }
    this.appliedInputs = next;
  }

  private destroyComponent(): void {
    this.container.clear();
    this.ref = undefined;
    this.inputNames = new Map();
    this.appliedInputs.clear();
  }
}

/** The kind's component, or the fallback kind's when the kind resolves to nothing. */
function resolutionOf(
  registry: DynamicComponentRegistry,
  kind: string | null | undefined,
  fallback: string | null | undefined,
): Resolution<ResolvedComponent> | undefined {
  const component = registry.resolve(kind);
  if (component !== undefined) {
    return { component, isFallback: false };
  }
  const fallbackComponent = registry.resolve(fallback);
  return fallbackComponent === undefined ? undefined : { component: fallbackComponent, isFallback: true };
}

function withoutModel(inputs: DynamicOutletInputs): DynamicOutletInputs {
  const { [MODEL_INPUT]: _model, ...rest } = inputs;
  return rest;
}

const INPUT_NAMES_CACHE = new WeakMap<Type<unknown>, ReadonlyMap<string, string>>();

/** Both the property and the template name of every input map to the name `setInput` expects. */
function inputNamesOf(type: Type<unknown>): ReadonlyMap<string, string> {
  const cached = INPUT_NAMES_CACHE.get(type);
  if (cached !== undefined) {
    return cached;
  }
  const names = new Map<string, string>();
  for (const { propName, templateName } of reflectComponentType(type)?.inputs ?? []) {
    names.set(propName, templateName);
    names.set(templateName, templateName);
  }
  INPUT_NAMES_CACHE.set(type, names);
  return names;
}
