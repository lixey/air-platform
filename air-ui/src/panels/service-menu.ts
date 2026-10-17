import { Injector, type ProviderToken } from '@angular/core';
import type { OverlayModelDto, PanelModel } from '../core/generated/contracts';
import { singlePanelLayout } from './single-panel-layout';

/** What a menu whose body injects its opener's service is opened with: the overlay's model and the body's injector. */
export interface ServiceMenu {
  readonly model: OverlayModelDto;
  readonly injector: Injector;
}

/** The service the body injects: its token, the instance, and the opener's injector, the parent of the body's. */
export interface ProvidedService<T> {
  readonly token: ProviderToken<T>;
  readonly instance: T;
  readonly parent: Injector;
}

/**
 * A menu of one panel that closes on Escape and on a click outside, under `handle`, and the injector its body is
 * mounted with: the opener's, with the opener's service under its token.
 */
export function serviceMenu<T>(key: string, panel: PanelModel, provided: ProvidedService<T>): ServiceMenu {
  return {
    model: {
      key,
      presentation: 'menu',
      closesOnEscape: true,
      closesOnOutside: true,
      layout: singlePanelLayout(key, panel),
    },
    injector: Injector.create({ providers: [{ provide: provided.token, useValue: provided.instance }], parent: provided.parent }),
  };
}
