import { httpResource, type HttpResourceRef } from '@angular/common/http';
import { computed, effect, inject, type Signal, signal, untracked } from '@angular/core';
import { shownByCaller } from '../app/alert/error-alert.interceptor';
import { AuthService } from '../app/auth/auth.service';
import { browserStorage, userStorageKey } from '../app/auth/user-storage';
import { type AnswerOf, serviceUrl } from '../core/api';
import type {
  GridServiceEndpoints,
  PanelLayoutDto,
  PanelNodeDto,
  ScreenDto,
  ScreenServiceEndpoints,
  UserProfileDto,
} from '../core/generated/contracts';
import type { IsCurrent, Lifetime } from '../core/lifetime';
import { registerPrefetchedGridCount } from '../grid/grid-count';
import type { ScreenAddress } from './screen-address';
import { SCREEN_LIFETIME } from './screen-lifetime';

/** A load outside any lifetime: its answer is always current. */
const always: IsCurrent = () => true;

/** What a screen loader exposes: the layout to draw, the data to hand it, how the load is going, and a way to ask again. */
export interface ScreenLoader {
  /** The layout of the last answer that carried one; an answer with the data alone leaves it. */
  readonly layout: Signal<PanelLayoutDto | null>;
  /** What the last answer that carried a layout asked of a dialog showing the screen; null before it and for none. */
  readonly asked: Signal<{ width?: string | null; height?: string | null } | null>;
  readonly data: Signal<unknown>;
  readonly isLoading: Signal<boolean>;
  readonly error: Signal<unknown>;
  /** Asks the same address again: what a failed load's retry does. */
  reload(): void;
  /**
   * Takes `screen` as the answer of the address held: no request, and what is drawn stays mounted. A panel the shown
   * layout repeats, equal in content under the same path of keys, keeps the model object the loader holds for it.
   */
  show(screen: ScreenDto): void;
}

/**
 * Loads a screen by address, for a route and for an overlay alike: posts the address's params
 * to `/api/{service}/get-data` and exposes what comes back. When the address targets a standalone
 * grid, `/api/{service}/get-count` is started in parallel with `get-data` using the same initial
 * parameters (including remembered named filters from browser storage).
 */
export function screenLoader(
  address: () => ScreenAddress | null,
  lifetime: Lifetime | null = inject(SCREEN_LIFETIME, { optional: true }),
): ScreenLoader {
  const auth = inject(AuthService, { optional: true });
  const requested = computed(address);
  /** The address while its lifetime is current, with the epoch it is asked in: an invalidation asks it again. */
  const currentAddress = computed(
    () => {
      const epoch = lifetime?.epoch() ?? 0;
      const target = requested();
      return target && (lifetime?.isCurrent() ?? true) ? { target, epoch } : null;
    },
    { equal: (a, b) => a === b || (!!a && !!b && a.target === b.target && a.epoch === b.epoch) },
  );
  /** Whether the answer to the address asked now is still current: taken once per address and epoch. */
  const captured = computed<IsCurrent | null>(() => currentAddress() && untracked(() => lifetime?.capture() ?? always));

  const requestTarget = computed(() => {
    const target = currentAddress()?.target;
    if (!target) {
      return null;
    }
    const rawParams: Readonly<Record<string, unknown>> =
      target.params && typeof target.params === 'object' ? (target.params as Readonly<Record<string, unknown>>) : {};
    const isGrid = isCountedGridTarget(target.service, rawParams);
    const remembered =
      isGrid && rawParams['namedFilters'] === undefined
        ? rememberedNamedFiltersOf(
            untracked(() => auth?.currentUser() ?? null),
            target.service,
          )
        : undefined;
    const body = remembered ? { ...rawParams, namedFilters: remembered } : rawParams;
    return { service: target.service, body, isGrid };
  });

  const resource = httpResource<AnswerOf<ScreenServiceEndpoints, 'get-data'>>(() => {
    const req = requestTarget();
    return req
      ? {
          url: serviceUrl<ScreenServiceEndpoints>(req.service, 'get-data'),
          method: 'POST',
          body: req.body,
          ...shownByCaller(),
        }
      : undefined;
  });

  const countResource = httpResource<AnswerOf<GridServiceEndpoints, 'get-count'>>(() => {
    const req = requestTarget();
    return req?.isGrid
      ? {
          url: serviceUrl<GridServiceEndpoints>(req.service, 'get-count'),
          method: 'POST',
          body: countBodyOf(req.body),
          ...shownByCaller(),
        }
      : undefined;
  });

  effect(() => {
    const req = requestTarget();
    const isCurrent = captured();
    if (req?.isGrid && isCurrent) {
      registerPrefetchedGridCount(req.service, {
        total: computed(() => (countResource.hasValue() && isCurrent() ? countResource.value()?.total : undefined)),
        isLoading: countResource.isLoading,
        isError: computed(() => countResource.status() === 'error'),
        isCurrent,
      });
    }
  });

  const last = keptLayoutOf(currentAddress, captured, resource);

  return {
    layout: computed(() => last.shown()?.layout ?? null),
    asked: computed(() => last.shown()?.asked ?? null),
    data: computed(() => (resource.hasValue() && captured()?.() ? resource.value()?.data : undefined)),
    isLoading: resource.isLoading,
    error: resource.error,
    reload: () => void resource.reload(),
    show: screen => {
      // The loader's own answer on its way is the later one, and a screen that moved on has no answer to take
      if (currentAddress() && captured()?.() && !resource.isLoading()) {
        const held = untracked(last.kept)?.layout;
        const shown = screen.layout;
        resource.set(held && shown ? { ...screen, layout: { ...shown, root: sharingModels(held.root, shown.root) } } : screen);
      }
    },
  };
}

function isCountedGridTarget(service: string, params: Readonly<Record<string, unknown>>): boolean {
  return service.toLowerCase().endsWith('-grid') && params['isReferencePicker'] !== true && !Array.isArray(params['referenceLabelPaths']);
}

function rememberedNamedFiltersOf(user: Pick<UserProfileDto, 'id'> | null, service: string): readonly string[] | undefined {
  if (!user || !service) {
    return undefined;
  }
  try {
    const raw = browserStorage()?.getItem(userStorageKey(user, `named-filters:${service}`));
    const parsed: unknown = raw ? JSON.parse(raw) : null;
    const keys = Array.isArray(parsed) ? parsed.filter((item): item is string => typeof item === 'string') : [];
    return keys.length > 0 ? keys : undefined;
  } catch {
    return undefined;
  }
}

function countBodyOf(params: Readonly<Record<string, unknown>>): Record<string, unknown> {
  const body: Record<string, unknown> = {};
  for (const key of ['parentId', 'parentService', 'filter', 'columnFilter', 'search', 'namedFilters']) {
    if (params[key] !== undefined) {
      body[key] = params[key];
    }
  }
  return body;
}

/** What a screen asks of a dialog showing it: the shape `ScreenLoader.asked` declares, written there alone. */
type AskedSize = NonNullable<ReturnType<ScreenLoader['asked']>>;

/** What the last answer that carried a layout gave: its layout, and what it asked of a dialog or null. */
interface KeptLayout {
  readonly layout: PanelLayoutDto;
  readonly asked: AskedSize | null;
}

/**
 * Keeps what the last answer with a layout gave, for the service of the address: dropped for another service and
 * once the screen that answered moved on. `kept` is what is held; `shown` is it while that screen is still current.
 * The same effect idles the request when the address goes, before it reads the answer.
 */
function keptLayoutOf(
  address: Signal<{ readonly target: ScreenAddress } | null>,
  captured: Signal<IsCurrent | null>,
  resource: HttpResourceRef<AnswerOf<ScreenServiceEndpoints, 'get-data'> | undefined>,
): { readonly kept: Signal<KeptLayout | null>; readonly shown: Signal<KeptLayout | null> } {
  const kept = signal<KeptLayout | null>(null);
  let layoutOf: string | undefined;
  let hadRequest = false;
  let isLayoutCurrent: IsCurrent | undefined;

  // An effect rather than a derived signal, so the layout is kept whether or not anything read it between answers
  effect(() => {
    const target = address()?.target ?? null;
    const service = target?.service;
    // An undefined resource request becomes idle but does not abort an in-flight HTTP load.
    if (!target && hadRequest) {
      resource.set(undefined);
    }
    hadRequest = target !== null;
    if (service !== layoutOf || isLayoutCurrent?.() === false) {
      layoutOf = service;
      kept.set(null);
    }

    const isCurrent = captured();
    const answer = resource.hasValue() ? resource.value() : undefined;
    if (answer?.layout && isCurrent?.()) {
      isLayoutCurrent = isCurrent;
      const { overlayWidth: width, overlayHeight: height } = answer;
      kept.set({ layout: answer.layout, asked: width == null && height == null ? null : { width, height } });
    }
  });

  const shown = computed(() => {
    const value = kept();
    return isLayoutCurrent?.() !== false ? value : null;
  });
  return { kept, shown };
}

/**
 * The `shown` node with, for it and for every node under it, the model object `held` carries under the same path of
 * keys where the two models are equal in content; a model the answer changed and a new node keep the shown one.
 */
function sharingModels(held: PanelNodeDto | undefined, shown: PanelNodeDto): PanelNodeDto {
  const isRepeated = !!held?.model && !!shown.model && JSON.stringify(held.model) === JSON.stringify(shown.model);
  const children = shown.children?.map(child =>
    sharingModels(
      held?.children?.find(candidate => candidate.key === child.key),
      child,
    ),
  );
  return { ...shown, ...(isRepeated ? { model: held.model } : {}), ...(children ? { children } : {}) };
}
