import { computed, type Injector, signal } from '@angular/core';
import type { OverlayDto } from '../core/generated/contracts';
import type { OverlayRef } from './overlay-ref';
import type { OverlayOpenOptions, OverlayService } from './overlay.service';

/**
 * One menu of an opener: at most one card open at a time, what its trigger says of it (`isOpen`, `id` for
 * `aria-expanded` and `aria-controls`), and its closing. A card that closes clears the handle only if it is still the
 * one the handle holds.
 */
export class OverlayMenu {
  /** The open card, `null` while the menu is closed. */
  private readonly _ref = signal<OverlayRef | null>(null);

  /** Whether the menu's card is open: what its trigger's `aria-expanded` says. */
  readonly isOpen = computed(() => this._ref() !== null);

  /** The id of the open card, `null` while the menu is closed: what its trigger's `aria-controls` names. */
  readonly id = computed(() => this._ref()?.id ?? null);

  constructor(private readonly overlay: Pick<OverlayService, 'open'>) {}

  /** The open card, `null` while the menu is closed. */
  get ref(): OverlayRef | null {
    return this._ref();
  }

  /**
   * Opens the card named `label` from the anchor unless one is open, which it answers instead; the options pass to
   * `OverlayService.open` as given.
   */
  open(model: OverlayDto, anchor: HTMLElement | null, injector: Injector, label: string, options?: OverlayOpenOptions): OverlayRef {
    const open = this._ref();
    if (open) {
      return open;
    }
    const ref = this.overlay.open(model, anchor, injector, options).name({ label });
    this._ref.set(ref);
    ref.closed.subscribe(() => this._ref.update(held => (held === ref ? null : held)));
    return ref;
  }

  /** Closes the open card, if any. */
  close(): void {
    void this._ref()?.dismiss();
  }
}
