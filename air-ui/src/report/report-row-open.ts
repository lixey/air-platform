import type { Injector } from '@angular/core';
import type { OpenTargetDto, OverlayDto, RowDto } from '../core/generated/contracts';
import { GridService } from '../grid/grid.service';
import type { OverlayService } from '../overlay/overlay.service';
import type { ScreenMutations } from '../screen/screen-mutations';

export function canOpenTarget(target: OpenTargetDto | null | undefined): target is OpenTargetDto {
  return (
    !!target &&
    typeof target.service === 'string' &&
    target.service.length > 0 &&
    typeof target.id === 'number' &&
    Number.isFinite(target.id) &&
    target.id > 0
  );
}

export function openRecord(
  target: OpenTargetDto | null | undefined,
  overlay: OverlayService,
  anchor: HTMLElement | null,
  injector: Injector,
  mutations?: ScreenMutations | null,
): void {
  if (!canOpenTarget(target)) {
    return;
  }
  const baseOverlay: OverlayDto = target.overlay ?? {
    key: target.service,
    presentation: 'drawer',
    closesOnEscape: true,
  };
  const baseParams =
    typeof baseOverlay.screen?.params === 'object' && baseOverlay.screen.params !== null
      ? (baseOverlay.screen.params as Record<string, unknown>)
      : {};
  const refreshServices = target.refreshServices?.filter((s): s is string => typeof s === 'string' && s.length > 0) ?? null;
  const ref = overlay.open(
    {
      ...baseOverlay,
      layout: undefined,
      screen: {
        ...(baseOverlay.screen ?? { service: target.service }),
        service: target.service,
        params: { ...baseParams, id: target.id },
      },
    },
    anchor,
    injector,
  );
  if (mutations) {
    mutations.track(ref, target.service, () => true);
    if (refreshServices && refreshServices.length > 0) {
      ref.closed.subscribe(result => {
        if (result !== undefined) {
          GridService.reloadServices(refreshServices, { notifyListeners: false });
        }
      });
    }
  } else {
    ref.closed.subscribe(result => {
      if (result !== undefined) {
        GridService.reloadServices(refreshServices && refreshServices.length > 0 ? refreshServices : [target.service]);
      }
    });
  }
}

export function openReportRow(
  row: RowDto,
  event: Event,
  overlay: OverlayService,
  injector: Injector,
  mutations?: ScreenMutations | null,
): void {
  const anchor = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
  openRecord(row.open, overlay, anchor, injector, mutations);
}
