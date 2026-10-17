import { InjectionToken, type Signal } from '@angular/core';
import type { FormRequest, ScreenDto } from '../core/generated/contracts';
import type { OverlayRef } from '../overlay/overlay-ref';

/** The address of the screen that supplied a component's model; whether work for it is still current is its `SCREEN_LIFETIME`'s. */
export interface ScreenAddress {
  service: string;
  params?: unknown;
}

/**
 * The host that loaded a screen by address, for a panel that needs the screen again with other parameters or that read
 * the screen itself and has its answer drawn.
 */
export interface ScreenHost {
  reload(patch: Record<string, unknown>): void;
  /** Shows `screen`, an answer of the address the host holds that a panel read, in place of the last one. */
  show(screen: ScreenDto): void;
}
export const SCREEN_HOST = new InjectionToken<ScreenHost>('SCREEN_HOST');

export const SCREEN_ADDRESS = new InjectionToken<Signal<ScreenAddress | null>>('SCREEN_ADDRESS');

/** Optional ownership for overlays opened from a child screen. */
export const SCREEN_OVERLAY_OWNER = new InjectionToken<{
  ownOverlay: (ref: OverlayRef, isDirtyPrompt: boolean) => void;
}>('SCREEN_OVERLAY_OWNER');

export function parentParams(
  address: ScreenAddress | null | undefined,
  service: string | null,
): Pick<FormRequest, 'parentService' | 'parentId'> {
  if (!address || address.service !== service || !address.params || typeof address.params !== 'object') {
    return {};
  }
  const params = address.params as FormRequest;
  return params.parentService && params.parentId != null ? { parentService: params.parentService, parentId: params.parentId } : {};
}
