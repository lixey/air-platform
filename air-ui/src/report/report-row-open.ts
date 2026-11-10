import type { Injector } from '@angular/core';
import { GridService } from '../grid/grid.service';
import type { OverlayService } from '../overlay/overlay.service';
import type { ScreenMutations } from '../screen/screen-mutations';

export interface ReportRowOpenTarget {
  readonly service: string;
  readonly id: number;
}

export function openTargetOfRow(row: Record<string, unknown>): ReportRowOpenTarget | null {
  const service = row['openForm'];
  const rawId = row['openId'];
  if (typeof service !== 'string' || !service || rawId === null || rawId === undefined) {
    return null;
  }
  const id = typeof rawId === 'number' ? rawId : Number(rawId);
  return Number.isFinite(id) && id > 0 ? { service, id } : null;
}

export function openReportRow(
  row: Record<string, unknown>,
  event: Event,
  overlay: OverlayService,
  injector: Injector,
  mutations?: ScreenMutations | null,
): void {
  const target = openTargetOfRow(row);
  if (!target) {
    return;
  }
  const refreshServices = Array.isArray(row['refreshServices'])
    ? (row['refreshServices'] as unknown[]).filter((s): s is string => typeof s === 'string' && s.length > 0)
    : null;
  const anchor = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
  const ref = overlay.open(
    {
      key: target.service,
      presentation: 'drawer',
      closesOnEscape: true,
      screen: { service: target.service, params: { id: target.id } },
    },
    anchor,
    injector,
  );
  if (mutations) {
    mutations.track(ref, target.service, () => true);
  } else {
    ref.closed.subscribe(result => {
      if (result !== undefined) {
        GridService.reloadServices(refreshServices && refreshServices.length > 0 ? refreshServices : [target.service]);
      }
    });
  }
}
