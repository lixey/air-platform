import { computed, ErrorHandler, Injector, type Signal, signal } from '@angular/core';
import { type Observable, Subject } from 'rxjs';
import type { CloseAttempt, CloseParticipant } from '../core/close-decision';
import { CloseDecision } from '../core/close-decision';
import type { OverlayDto } from '../core/generated/contracts';
import { Lifetime } from '../core/lifetime';
import { PANEL_DENSITY, PANEL_WINDOW_RULE } from '../panels/panels-tokens';
import type { OverlayAsk } from './overlay-box';

/**
 * What an overlay's card says it is to assistive technology: its role — a dialog, or the alert dialog of a
 * confirmation — and its name and description, as a text or as the ids of the elements that hold them.
 */
export interface OverlayNaming {
  readonly role?: 'dialog' | 'alertdialog';
  readonly label?: string;
  readonly labelledBy?: string;
  readonly describedBy?: string;
}

/**
 * The scale an overlay's card reads, derived from where it was opened: the work area's slate, or the brand of the frame
 * for an overlay opened from the navigation, its switchers or the mega menu.
 */
export type OverlayScale = 'work' | 'frame';

/**
 * One open overlay, from the side of whoever opened it and of whatever is inside it.
 *
 * The panels inside an overlay reach it by injecting this class: the host mounts them
 * through `injector`, which provides the ref itself — and the flush density, so an overlay's
 * panels stand edge to edge inside its one card — on top of the parent it was given: the
 * opener's injector, or the root. A component that is also used in a routed screen
 * injects it optionally and finds nothing there.
 *
 * The injector also says the window's `lg` rule does not reach the layout: an overlay is a
 * window of its own, opened for a task, and holds what its model says at every width — a
 * menu its one note, a dialog its buttons, a drawer the nav that closes it. What the device
 * can show of a routed screen is decided by that screen's own layout, outside.
 */
export class OverlayRef<TResult = unknown> {
  private static next = 0;

  /** The card's id, which a trigger names as the element it controls. */
  readonly id = `ui-overlay-${++OverlayRef.next}`;

  /** Whether the card is a modal dialog: a `dialog` or a `drawer`; a `menu` and a `popover` are not. */
  readonly isModal: boolean;

  private readonly _naming = signal<OverlayNaming>({});
  readonly naming = this._naming.asReadonly();

  private readonly askedFrom = signal<Signal<OverlayAsk | null> | null>(null);
  /** The size the screen inside asks with its last answer; null while no screen said where it is read. */
  readonly asked: Signal<OverlayAsk | null> = computed(() => this.askedFrom()?.() ?? null);

  private readonly closedSubject = new Subject<TResult | undefined>();
  private _isClosed = false;
  private _closeCause: symbol | undefined;
  private readonly participantsLifetime = new Lifetime(() => !this.isClosed);
  private readonly closeParticipants = new Set<CloseParticipant>();
  private pending: CloseDecision | undefined;
  /** The attempt a caller of `prepareClose` holds: it commits it, so `close` leaves it alone. */
  private held: CloseDecision | undefined;
  private readonly errorHandler: ErrorHandler;

  /** Emits the result once, when the overlay closes, then completes. */
  readonly closed: Observable<TResult | undefined> = this.closedSubject.asObservable();

  /** What the panels inside are mounted with: the parent injector, this ref, `flush`, and no window rule. */
  readonly injector: Injector;

  constructor(
    readonly model: OverlayDto,
    readonly anchor: HTMLElement | null,
    parent: Injector,
    /** The scale the card reads, derived from where the overlay was opened; the model never names it. */
    readonly scope: OverlayScale,
    /** The card keeps its own width, without the floor of its anchor's (`OverlayOpenOptions.keepsOwnWidth`). */
    readonly keepsOwnWidth = false,
    /**
     * The host moves no focus into the card at the opening: the opener keeps it unless the card's own content takes the
     * caret itself (`OverlayOpenOptions.keepsOpenerFocus`).
     */
    readonly keepsOpenerFocus = false,
    /**
     * The card hangs down from its anchor or is a dialog, by the height its model states and never by its measured size
     * (`OverlayOpenOptions.hangsDownOnly`).
     */
    readonly hangsDownOnly = false,
  ) {
    this.isModal = model.presentation === 'dialog' || model.presentation === 'drawer';
    this.errorHandler = parent.get(ErrorHandler);
    this.injector = Injector.create({
      providers: [
        { provide: OverlayRef, useValue: this },
        { provide: PANEL_DENSITY, useValue: 'flush' },
        { provide: PANEL_WINDOW_RULE, useValue: false },
      ],
      parent,
    });
  }

  get isClosed(): boolean {
    return this._isClosed;
  }

  get closeCause(): symbol | undefined {
    return this._closeCause;
  }

  registerCloseParticipant(participant: CloseParticipant): () => void {
    this.closeParticipants.add(participant);
    this.participantsLifetime.invalidate();
    return () => {
      if (this.closeParticipants.delete(participant)) {
        this.participantsLifetime.invalidate();
      }
    };
  }

  close(result?: TResult, cause?: symbol): Promise<boolean> {
    if (this.pending) {
      return this.pending.done;
    }
    if (this._isClosed) {
      return Promise.resolve(true);
    }
    if (!this.closeParticipants.size) {
      this.emitClosed(result, cause);
      return Promise.resolve(true);
    }
    const attempt = this.begin({ result, cause });
    attempt.ready.then(approved => {
      if (approved && this.pending === attempt && this.held !== attempt) {
        attempt.commit();
      }
    });
    return attempt.done;
  }

  /** Holds an existing close, or prepares one without emitting until the route settles. */
  prepareClose(request: CloseAttempt = {}): CloseDecision {
    const attempt = this.pending ?? this.begin(request);
    this.held = attempt;
    return attempt;
  }

  /** Successful screen teardown or forced identity loss has already invalidated owners. */
  forceClose(request: CloseAttempt = {}): void {
    this.pending?.cancel();
    if (!this.isClosed) {
      this.emitClosed(request.result as TResult, request.cause);
    }
  }

  /**
   * Names the card: what opened the overlay names it by its label, a caption or a dialog inside it by its elements. The
   * first to name a part keeps it, so a later panel does not rename a card the screen reader has announced.
   */
  name(naming: OverlayNaming): this {
    const given = Object.fromEntries(Object.entries(naming).filter(([, value]) => value !== undefined && value !== ''));
    this._naming.update(current => ({ ...given, ...current }));
    return this;
  }

  /** Called once by the overlay's screen: the signal the asked size is read from. */
  ask(size: Signal<OverlayAsk | null>): void {
    this.askedFrom.set(size);
  }

  /** Closes with no result. */
  dismiss(): Promise<boolean> {
    return this.close(undefined);
  }

  /** Starts the one pending attempt: whoever holds or awaits the close shares it until it settles. */
  private begin(request: CloseAttempt): CloseDecision {
    const attempt = new CloseDecision({
      participants: [...this.closeParticipants],
      request,
      isOwnerCurrent: this.participantsLifetime.capture(),
      onCommit: answer => this.emitClosed(answer.result as TResult, answer.cause),
      errorHandler: this.errorHandler,
    });
    this.pending = attempt;
    attempt.done.then(() => {
      if (this.pending === attempt) {
        this.pending = undefined;
      }
      if (this.held === attempt) {
        this.held = undefined;
      }
    });
    return attempt;
  }

  private emitClosed(result: TResult | undefined, cause: symbol | undefined): void {
    this._isClosed = true;
    this._closeCause = cause;
    this.closedSubject.next(result);
    this.closedSubject.complete();
  }
}
