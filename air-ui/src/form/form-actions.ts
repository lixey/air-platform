import type { HttpClient } from '@angular/common/http';
import { computed, type Injector, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { AlertService } from '../app/alert/alert.service';
import { shownByCaller } from '../app/alert/error-alert.interceptor';
import { serviceUrl } from '../core/api';
import type { FormActionDto, FormDataDto, FormModel, FormNavActionModel, MessageDto, OverlayDto } from '../core/generated/contracts';
import { classifyMutationFailure } from '../core/mutation-failure';
import { type AskService, ownMessages } from '../overlay/ask.service';
import type { ConfirmationService } from '../overlay/confirmation.service';
import type { OverlayRef } from '../overlay/overlay-ref';
import type { OverlayService } from '../overlay/overlay.service';
import { parentParams, type ScreenAddress } from '../screen/screen-address';
import type { ScreenMutations, ScreenOperation } from '../screen/screen-mutations';
import { frozenCopy } from './form-value';

/**
 * The state of the action under way and the phase it is in. `method` is null for the wait a save started: the phase
 * `reading` is also the wait for the screen after the first save of a new record that stays.
 */
export interface ActingState {
  readonly method: string | null;
  readonly phase: 'asking' | 'posting' | 'reading';
}

/** One press of an action, from the press until its confirmation or its dialog is answered. */
interface ActionFlow {
  /** Whether the press saved a new record first: its screen is read again whatever the action answers. */
  savedNew: boolean;
  isCommitted: boolean;
  /** The record the action was posted for; none before the first post. */
  id: number | null;
  /** The acting state of the press's own post, which nothing else holds. */
  posting: ActingState | null;
  isCurrent: () => boolean;
}

/** A refusal's messages at the paths of the form's elements, kept for the record `id` read again. */
interface KeptMessages {
  readonly id: number;
  readonly messages: readonly MessageDto[];
}

export interface FormActionsContext {
  readonly http: HttpClient;
  readonly alert: AlertService;
  readonly asks: AskService;
  readonly confirmation: ConfirmationService;
  readonly overlay: OverlayService;
  readonly ref: OverlayRef | null;
  readonly injector: Injector;
  readonly mutations: ScreenMutations | null;
  readonly address: () => ScreenAddress | null | undefined;
  readonly service: () => string | null;
  readonly model: () => FormModel | null;
  readonly id: () => number | null;
  readonly ver: () => number | null;
  readonly isDirty: () => boolean;
  readonly canAct: () => boolean;
  readonly capture: () => () => boolean;
  readonly saveBeforeAct: () => Promise<FormDataDto | null>;
  readonly beginOperation: () => ScreenOperation | undefined;
  readonly releaseOperation: (operation: ScreenOperation | undefined) => void;
  readonly showsMessage: (path: string) => boolean;
  readonly reportFailure: (error: unknown, own?: readonly MessageDto[]) => void;
  readonly readAgain: (service: string, id: number, method: string | null, isCurrent: () => boolean) => void;
}

/**
 * Owns the form's action execution lifecycle: confirmation or ask dialog, pre-action save of a new or dirty record,
 * posting the action, routing refusal messages between the ask dialog and the form, keeping element messages across
 * a new record's screen reload, and opening any follow-up screen.
 */
export class FormActions {
  /**
   * The action under way: `asking` from the press (the confirmation, the save first), `posting` while its request or
   * the form's own read is in flight, `reading` while the screen host reads the screen the action asked for, or the
   * screen of a new record after its first save, a wait that names no method.
   */
  private readonly acting = signal<ActingState | null>(null);

  /**
   * Whether an action is under way: from its post until the screen read again after it is adopted. True also while
   * the host reads the screen of a new record after its first save.
   */
  readonly isActing = computed(() => this.acting()?.phase === 'posting' || this.acting()?.phase === 'reading');

  /** The method of the action under way, from the press: the button that shows the busy mark. */
  readonly actingMethod = computed(() => this.acting()?.method ?? null);

  /** Whether the action state is currently in the `reading` phase waiting for the screen host. */
  readonly isReading = computed(() => this.acting()?.phase === 'reading');

  /** What a refused action after a new record's first save left for the record read again; a new press or a reset drops it. */
  private kept: KeptMessages | null = null;

  constructor(private readonly ctx: FormActionsContext) {}

  setReading(method: string | null): void {
    this.acting.set({ method, phase: 'reading' });
  }

  clearActing(): void {
    this.acting.set(null);
  }

  reset(): void {
    this.acting.set(null);
    this.kept = null;
  }

  /** Consumes and returns any messages kept for the record `id` read again; they are kept once. */
  takeKeptMessages(id: number | null): readonly MessageDto[] | null {
    const kept = this.kept;
    this.kept = null;
    if (kept?.messages.length && kept.id === id) {
      return kept.messages;
    }
    return null;
  }

  /**
   * Runs an action of the nav over the open record: asks its confirmation, or its input in a dialog that stays open
   * while a refusal names one of its fields, saves a dirty or new form first, posts the record's id and version with
   * the dialog's values, and after a commit reads the screen again, busy until that screen's record is adopted.
   */
  async act(action: FormNavActionModel): Promise<void> {
    const service = this.ctx.service();
    if (!this.ctx.canAct() || !service) {
      return;
    }
    const press: ActingState = { method: action.method, phase: 'asking' };
    this.acting.set(press);
    this.kept = null;
    const flow: ActionFlow = { savedNew: false, isCommitted: false, id: null, posting: null, isCurrent: this.ctx.capture() };
    try {
      if (action.ask) {
        await this.ctx.asks.ask(
          { ...action.ask, title: action.ask.title ?? action.label },
          value => this.submit(action, service, press, flow, value),
          this.ctx.injector,
        );
      } else if (!action.confirm || (await this.ctx.confirmation.confirm(action.confirm))) {
        await this.submit(action, service, press, flow, {});
      }
    } finally {
      this.endPress(service, press, flow);
    }
  }

  /**
   * One submission of a press: the save a dirty or new form runs first, then the post of the record's id and version
   * beside `value`. Answers the messages that stay on the fields of the action's dialog; none ends the press.
   */
  private async submit(
    action: FormNavActionModel,
    service: string,
    press: ActingState,
    flow: ActionFlow,
    value: Readonly<Record<string, unknown>>,
  ): Promise<readonly MessageDto[]> {
    // The record may have been replaced, or another action pressed, while the confirmation or the dialog stood.
    if (this.acting() !== press || !flow.isCurrent() || !this.ctx.canAct()) {
      return [];
    }
    if (this.ctx.id() === null || this.ctx.isDirty()) {
      const wasNew = this.ctx.id() === null;
      if (!(await this.ctx.saveBeforeAct())) {
        return [];
      }
      flow.savedNew ||= wasNew;
      flow.isCurrent = this.ctx.capture();
    }
    const id = this.ctx.id();
    if (this.acting() !== press || id === null) {
      return [];
    }
    flow.id = id;
    const operation = this.ctx.beginOperation();
    flow.posting = { method: action.method, phase: 'posting' };
    this.acting.set(flow.posting);
    let refusal: unknown;
    try {
      const answer = await firstValueFrom(
        this.ctx.http.post<FormActionDto | null>(
          serviceUrl(action.service, action.method),
          // The record's members last, so no value of the dialog replaces one.
          { ...value, ...parentParams(this.ctx.address(), service), id, ver: this.ctx.ver() },
          shownByCaller(),
        ),
      );
      if (!flow.isCurrent()) {
        return [];
      }
      if (answer?.outcome === 'committed') {
        this.ctx.alert.warnings(answer.warnings, this.ctx.model()?.mutationTexts.committedWarningTitle);
        if (operation) {
          this.ctx.mutations?.publish(operation, { service, recordId: id, operation: 'save' });
        }
        if (answer.openScreen) {
          this.openScreen(answer.openScreen, answer.openId);
        }
        // What an earlier refusal of this press left for the record read again is of an action that has now succeeded.
        this.kept = null;
        flow.isCommitted = true;
        return [];
      }
      refusal = answer;
    } catch (error: unknown) {
      if (!flow.isCurrent()) {
        return [];
      }
      refusal = error;
    } finally {
      this.ctx.releaseOperation(operation);
    }
    return this.refuse(action, press, flow, refusal);
  }

  /**
   * A refused action: the messages at the fields of its dialog are answered to it and the press asks again; the others
   * stand on the form's fields and in the alert, and those of the form's elements are kept for the screen read again
   * after a new record's first save.
   */
  private refuse(action: FormNavActionModel, press: ActingState, flow: ActionFlow, refusal: unknown): readonly MessageDto[] {
    const failure = classifyMutationFailure(refusal);
    const own = action.ask ? ownMessages(action.ask, failure.messages) : [];
    // A refusal that names the dialog's fields alone leaves the form's own state as it is.
    if (!own.length || own.length !== failure.messages.length) {
      this.ctx.reportFailure(refusal, own);
    }
    if (flow.savedNew && flow.id !== null) {
      const messages = failure.messages.filter(m => !!m.path && !own.includes(m) && this.ctx.showsMessage(m.path));
      this.kept = { id: flow.id, messages: frozenCopy(messages) };
    }
    if (!own.length) {
      return [];
    }
    this.acting.set(press);
    return own;
  }

  /**
   * The end of a press, once its confirmation or its dialog is answered: after a commit, or after a new record's first
   * save, the screen is read again; else the acting state is left, unless another record or action took it over.
   */
  private endPress(service: string, press: ActingState, flow: ActionFlow): void {
    const state = this.acting();
    if (state !== press && (state === null || state !== flow.posting)) {
      return;
    }
    if ((flow.isCommitted || flow.savedNew) && flow.id !== null) {
      this.ctx.readAgain(service, flow.id, press.method, flow.isCurrent);
    } else {
      this.acting.set(null);
    }
  }

  /** Opens the screen an action's answer names: in the presentation of the form's own overlay, else as a dialog. */
  private openScreen(service: string, id: number | null | undefined): void {
    const params = id == null ? {} : { id };
    const template = this.ctx.ref?.model;
    const model: OverlayDto = template?.screen
      ? { ...template, key: service, screen: { ...template.screen, service, params } }
      : { key: service, presentation: 'dialog', closesOnEscape: true, screen: { service, params } };
    this.ctx.overlay.open(model, null, this.ctx.injector);
  }
}
