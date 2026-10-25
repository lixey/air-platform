import { inject, Injector } from '@angular/core';
import type { GridModel, OverlayDto } from '../core/generated/contracts';
import type { IsCurrent } from '../core/lifetime';
import type { OverlayRef } from '../overlay/overlay-ref';
import { OverlayService } from '../overlay/overlay.service';
import { parentParams, SCREEN_ADDRESS } from '../screen/screen-address';
import { ScreenMutations } from '../screen/screen-mutations';
import type { GridRowKey } from './grid-row';

/** What the form half is given of its grid. */
export interface GridFormsOptions {
  /** The grid's model, or null while there is none: it declares the form. */
  readonly model: () => GridModel | null;
  /** The grid's service, or null while there is none: the parent the form's params name. */
  readonly service: () => string | null;
  /** Captures the grid's lifetime for an opened form, so a close after the grid moved on refreshes nothing. */
  readonly capture: () => IsCurrent;
  /** Reloads the grid's page: a closed form refreshes it when no screen mutations are there to say so. */
  readonly reload: () => void;
}

/**
 * The form half of one grid: opening the form the grid declares, by address, with the verified parent params, and
 * tracking the forms it opened, so their saves refresh it — through the screen's mutations when there are any, else
 * by a reload when the form closes. A form the model does not declare is the grid's while the grid has it open and
 * no longer: nothing of a closed form is kept. The grid service keeps its `openForm`, `withParentParams` and
 * `trackForm` and hands them here; the pick of a select stays the list's rule, in the service.
 *
 * A form opens in the first overlay of three: the one the page carries for the form the opened row names, the one
 * the model carries as the grid's own form, addressed to the form being opened, and a dialog built here.
 *
 * Created in a field of the grid service, so its injections are the service's. It knows nothing of the service: it
 * is given its options.
 */
export class GridForms {
  private readonly address = inject(SCREEN_ADDRESS, { optional: true });
  private readonly overlay = inject(OverlayService);
  private readonly injector = inject(Injector);
  private readonly mutations = inject(ScreenMutations, { optional: true });
  /** The tracked forms that are still open: how to stop watching each, and its form service. */
  private readonly watchers = new Map<() => void, string>();

  constructor(private readonly options: GridFormsOptions) {}

  /**
   * Opens the declared or resolved form for the row with this key, or an empty one for none; `false` when the grid
   * names no form. `rowOverlay` is the overlay the page carries for the form the row names, or null when the row
   * names another form or none: the form opens in it, else in the overlay of the grid's own form, else in a dialog,
   * the first two addressed to the form being opened.
   */
  open(id: GridRowKey | null, anchor?: HTMLElement | null, formService?: string | null, rowOverlay?: OverlayDto | null): boolean {
    const defaultOverlay = this.options.model()?.form;
    const targetService = formService ?? defaultOverlay?.screen?.service;
    if (!targetService) {
      return false;
    }

    const declared = rowOverlay ?? defaultOverlay;
    const overlay: OverlayDto = declared
      ? {
          ...declared,
          key: targetService,
          screen: { ...declared.screen, service: targetService, params: id === null ? {} : { id } },
        }
      : {
          key: targetService,
          presentation: 'drawer',
          closesOnEscape: true,
          screen: { service: targetService, params: id === null ? {} : { id } },
        };

    this.watch(this.overlay.open(this.addParentParams(overlay), anchor ?? null, this.injector), targetService);
    return true;
  }

  /** Whether a form of this service is the grid's: the one its model declares, or one it opened that is still open. */
  follows(service: string | null | undefined): boolean {
    if (!service) {
      return false;
    }
    return service === this.options.model()?.form?.screen?.service || [...this.watchers.values()].includes(service);
  }

  /** The model with the grid's parent params added, when it opens a form; else as it is. */
  withParentParams(model: OverlayDto): OverlayDto {
    return this.follows(model.screen?.service) ? this.addParentParams(model) : model;
  }

  /** Watches an opened overlay when it is a form the grid opened; any other overlay is left alone. */
  track(ref: OverlayRef): void {
    const service = ref.model.screen?.service;
    if (service && this.follows(service)) {
      this.watch(ref, service);
    }
  }

  /** Whether a form the grid opened is open and watched. */
  isTracking(): boolean {
    return this.watchers.size > 0;
  }

  /** Stops watching every tracked form: the grid is released. */
  stop(): void {
    for (const stop of this.watchers.keys()) stop();
    this.watchers.clear();
  }

  /** The model of a form of the grid with the grid's parent params added. */
  private addParentParams(model: OverlayDto): OverlayDto {
    const screen = model.screen;
    if (!screen) {
      return model;
    }
    const params = screen.params && typeof screen.params === 'object' ? screen.params : {};
    return { ...model, screen: { ...screen, params: { ...params, ...parentParams(this.address?.(), this.options.service()) } } };
  }

  /**
   * Watches a form of the grid until it closes. In a scope the scope's close is subscribed first and the entry leaves
   * after it: the scope asks the grid whether it follows the form's service while the entry still stands.
   */
  private watch(ref: OverlayRef, service: string): void {
    const isCurrent = this.options.capture();
    if (this.mutations) {
      const untrack = this.mutations.track(ref, service, isCurrent);
      const closed = ref.closed.subscribe(() => this.watchers.delete(stop));
      const stop = () => {
        untrack();
        closed.unsubscribe();
      };
      this.watchers.set(stop, service);
    } else {
      const subscription = ref.closed.subscribe(() => {
        if (isCurrent()) {
          this.options.reload();
        }
      });
      const stop = () => subscription.unsubscribe();
      this.watchers.set(stop, service);
      subscription.add(() => this.watchers.delete(stop));
    }
  }
}
