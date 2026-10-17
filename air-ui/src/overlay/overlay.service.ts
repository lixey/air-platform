import { computed, inject, Injector, Service, signal } from '@angular/core';
import type { OverlayModelDto } from '../core/generated/contracts';
import { resolveModel } from '../core/resolve-model';
import { toBound } from '../panels/panel-size';
import { SCREEN_OVERLAY_OWNER } from '../screen/screen-address';
import { ScreenLifecycle } from '../screen/screen-lifecycle';
import { WORK_AREA } from '../theme/css-classes';
import type { OverlayRect } from './overlay-box';
import { OverlayMenu } from './overlay-menu';
import { OverlayRef, type OverlayScale } from './overlay-ref';

/** What the opener says about the overlay besides its model, on the client alone. */
export interface OverlayOpenOptions {
  /** The overlay is the dirty prompt, which the screen owners treat apart. */
  readonly isDirtyPrompt?: boolean;
  /**
   * A menu or a popover keeps its card's own width and drops the floor of its anchor's width, as the date picker, whose
   * card is as wide as its days whatever the field it hangs from.
   */
  readonly keepsOwnWidth?: boolean;
  /**
   * A menu or a popover leaves focus where it was: the host moves none at the opening, so the field that opened it goes
   * on taking the keys, unless an element of the card's own content takes the caret itself, as the search box of a
   * picker's list does. The close returns focus to the element that had it at the opening unless the user moved it on.
   */
  readonly keepsOpenerFocus?: boolean;
  /**
   * A menu or a popover hangs down from its anchor or is a dialog, never above its anchor and never over it. The height
   * its model states is the least room the card needs under its anchor, and not its size: where that height fits there
   * the card hangs, as tall as its content and at most the room between its top edge and the frame's bottom edge, and
   * otherwise it presents as a dialog as tall as the window's frame. The choice and that room read the stated height,
   * the anchor and the frame, never the size the card is measured at, so the card's header keeps its place while its
   * content grows and shrinks. The model states its height in pixels: an open without one is refused.
   */
  readonly hangsDownOnly?: boolean;
}

/** Both closing paths, open unless a model shuts one; a menu keeps both, whatever its model says. */
const CLOSE_DEFAULTS = { closesOnEscape: true, closesOnOutside: true };

/**
 * Opens overlays and keeps the stack of them.
 *
 * The frame tells it where the work area is through `setBounds`; the host reads `bounds` to
 * place every overlay. Each ref carries an injector that provides itself — on top of the
 * opener's injector when the opener passed one — which is how the panels inside an overlay
 * reach it, and reach whatever opened it.
 */
@Service()
export class OverlayService {
  private readonly injector = inject(Injector);

  /** Every open overlay, in the order opened. */
  private readonly _stack = signal<readonly OverlayRef[]>([]);
  readonly stack = this._stack.asReadonly();

  /** Whether a modal overlay is open: then the frame's panels are inert. */
  readonly hasModal = computed(() => this.stack().some(ref => ref.isModal));

  /** The work area, as the frame measures it. `null` when there is no frame. */
  private readonly _bounds = signal<OverlayRect | null>(null);
  readonly bounds = this._bounds.asReadonly();

  /**
   * Opens an overlay: one carrying a layout, or one naming a screen the host loads by address.
   * Pass the opener's own injector to let the panels inside see what it provides — a control
   * opening a menu hands it the state the panel inside reads and writes. The card's scale follows the opener
   * (`scopeOf`); no caller names it.
   */
  open<TResult = unknown>(
    model: OverlayModelDto,
    anchor?: HTMLElement | null,
    injector?: Injector,
    options?: OverlayOpenOptions,
  ): OverlayRef<TResult> {
    if (model?.screen) {
      if (model.layout) {
        throw new Error(`Cannot open overlay "${model.key}": it carries a layout and names a screen, one of the two`);
      }
    } else if (!model?.layout?.root?.children?.length) {
      throw new Error(`Cannot open overlay "${model?.key}": its layout has no panels`);
    }

    if (model.presentation === 'menu' && !anchor) {
      throw new Error(`Cannot open menu overlay "${model.key}" without an anchor element`);
    }

    const keepsOpenerFocus = options?.keepsOpenerFocus ?? false;
    if (keepsOpenerFocus && (model.presentation === 'dialog' || model.presentation === 'drawer')) {
      throw new Error(`Cannot open overlay "${model.key}" as a ${model.presentation} that leaves focus with its opener`);
    }

    const hangsDownOnly = options?.hangsDownOnly ?? false;
    const isAnchored = model.presentation === 'menu' || model.presentation === 'popover';
    if (hangsDownOnly && isAnchored && toBound(model.height) === undefined) {
      throw new Error(`Cannot open overlay "${model.key}" to hang down only without a height in pixels`);
    }

    const closes = resolveModel(CLOSE_DEFAULTS, model, model.presentation === 'menu' ? CLOSE_DEFAULTS : {});
    const ref = new OverlayRef<TResult>(
      { ...model, ...closes },
      anchor ?? null,
      injector ?? this.injector,
      scopeOf(anchor, injector),
      options?.keepsOwnWidth ?? false,
      keepsOpenerFocus,
      hangsDownOnly,
    );

    // The stack holds refs of every result type; what it does with them needs none of it.
    const entry = ref as OverlayRef;
    ref.closed.subscribe(() => this._stack.update(current => current.filter(r => r !== entry)));
    this._stack.update(current => [...current, entry]);
    const isDirtyPrompt = options?.isDirtyPrompt ?? false;
    injector?.get(ScreenLifecycle, null)?.ownOverlay(entry, isDirtyPrompt);
    injector?.get(SCREEN_OVERLAY_OWNER, null)?.ownOverlay(entry, isDirtyPrompt);

    return ref;
  }

  /**
   * A new handle of one menu, bound to this service: what an opener keeps to open its card once, and what its trigger's
   * aria reads.
   */
  menu(): OverlayMenu {
    return new OverlayMenu(this);
  }

  /** The frame's measure of the work area, or `null` when the frame goes away. */
  setBounds(rect: OverlayRect | null): void {
    this._bounds.set(rect);
  }

  /** What `Escape` does: closes the topmost overlay, if its model allows it. */
  closeTop(): void {
    const top = this.stack().at(-1);

    if (top?.model.closesOnEscape) {
      top.dismiss();
    }
  }

  /** What a pointer-down beside an overlay does: closes it, if its model allows it. */
  closeOutside(ref: OverlayRef): void {
    if (ref.model.closesOnOutside) {
      ref.dismiss();
    }
  }
}

/**
 * The scale an overlay's card reads, from where it was opened: an anchor inside the work area — the routed outlet or a
 * card of its scale — reads slate, any other anchor stands in the frame and reads the brand; without an anchor, the scale
 * of the overlay whose injector opened it, and the work area's for a screen or for no opener at all.
 */
function scopeOf(anchor: HTMLElement | null | undefined, injector: Injector | undefined): OverlayScale {
  if (anchor) {
    return anchor.closest(`.${WORK_AREA}`) ? 'work' : 'frame';
  }
  return injector?.get(OverlayRef, null)?.scope ?? 'work';
}
