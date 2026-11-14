import { computed, DestroyRef, inject, Injector, Service, type Signal, signal } from '@angular/core';
import type { FieldTree } from '@angular/forms/signals';
import { AlertService } from '../app/alert/alert.service';
import { LocaleService } from '../app/locale.service';
import { Api } from '../core/api';
import type { CloseApproval } from '../core/close-decision';
import type {
  AffectedDto,
  FormDataDto,
  FormElementDto,
  FormModel,
  FormNavActionModel,
  MessageDto,
  MutationOutcome,
} from '../core/generated/contracts';
import { Lifetime } from '../core/lifetime';
import { TextsService } from '../core/texts.service';
import type { Unavailable } from '../nav/nav-availability';
import { AskService } from '../overlay/ask.service';
import { ConfirmationService } from '../overlay/confirmation.service';
import { OverlayRef } from '../overlay/overlay-ref';
import { OverlayService } from '../overlay/overlay.service';
import { SCREEN_ADDRESS, SCREEN_HOST } from '../screen/screen-address';
import { ScreenLifecycle } from '../screen/screen-lifecycle';
import { SCREEN_LIFETIME } from '../screen/screen-lifetime';
import { type ScreenMutationEvent, ScreenMutations, type ScreenOperation } from '../screen/screen-mutations';
import { FormActions } from './form-actions';
import { FormDraftState, type FormInput } from './form-draft-state';
import { routeFormFailure } from './form-failure';
import { FormReader } from './form-reader';
import { FormSaveRunner } from './form-save';

export type { FormInput } from './form-draft-state';
export { isFormData } from './form-value';

/** One form owns its draft, confirmed baseline and the result of its current save. */
@Service({ autoProvided: false })
export class FormService {
  private static next = 0;

  private readonly api = inject(Api);
  private readonly alert = inject(AlertService);
  private readonly texts = inject(TextsService);
  private readonly zone = inject(LocaleService).zone;
  private readonly ref = inject(OverlayRef, { optional: true });
  private readonly lifecycle = inject(ScreenLifecycle, { optional: true });
  private readonly confirmation = inject(ConfirmationService);
  private readonly asks = inject(AskService);
  private readonly mutations = inject(ScreenMutations, { optional: true });
  private readonly address = inject(SCREEN_ADDRESS, { optional: true });
  private readonly host = inject(SCREEN_HOST, { optional: true });
  private readonly overlay = inject(OverlayService);
  private readonly injector = inject(Injector);

  private readonly _pageToOpen = signal<string | null>(null);
  readonly pageToOpen: Signal<string | null> = this._pageToOpen.asReadonly();
  private readonly _outcome = signal<MutationOutcome | null>(null);
  readonly outcome: Signal<MutationOutcome | null> = this._outcome.asReadonly();

  private operation: ScreenOperation | undefined;
  private readonly lifetime = (inject(SCREEN_LIFETIME, { optional: true }) ?? new Lifetime()).child(() => !this.ref?.isClosed);
  private readonly invalidations = new Set<() => void>();

  readonly canClose: Signal<boolean> = computed(() => this.ref !== null);
  private readonly isUnsettled: Signal<boolean> = computed(() => this.outcome() === 'conflict' || this.outcome() === 'unknown');

  private readonly draft: FormDraftState = new FormDraftState({
    instance: ++FormService.next,
    zone: this.zone,
    texts: this.texts,
    refusesInput: () => this.isBusy() || !!this.lifecycle?.isLocked(),
    canAbsorb: () => !this.isUnsettled() && !this.isSaving() && !this.isRecovering(),
  });

  readonly model: Signal<FormModel | null> = this.draft.model;
  readonly idScope: Signal<string> = this.draft.idScope;
  readonly id: Signal<number | null> = this.draft.id;
  readonly ver: Signal<number | null> = this.draft.ver;
  readonly value: Signal<Record<string, unknown>> = this.draft.value;
  readonly adoption: Signal<number> = this.draft.adoption;
  readonly dataRevision: Signal<number> = this.draft.dataRevision;
  readonly unavailable: Signal<Unavailable> = this.draft.unavailable;
  readonly messages: Signal<readonly MessageDto[]> = this.draft.messages;
  readonly service: Signal<string | null> = this.draft.service;
  readonly isReadOnly: Signal<boolean> = this.draft.isReadOnly;
  readonly elements: Signal<readonly FormElementDto[]> = this.draft.elements;
  readonly isDirty: Signal<boolean> = this.draft.isDirty;

  private readonly reader: FormReader = new FormReader({
    api: this.api,
    alert: this.alert,
    confirmation: this.confirmation,
    host: this.host,
    lifecycle: this.lifecycle,
    address: () => this.address?.(),
    service: () => this.service(),
    model: () => this.model(),
    id: () => this.id(),
    adoption: () => this.adoption(),
    isDirty: () => this.isDirty(),
    isBusy: () => this.isBusy(),
    isIdle: () => this.isIdle(),
    isUnsettled: () => this.isUnsettled(),
    capture: () => this.capture(),
    adopt: data => this.adopt(data),
    bumpDataRevision: () => this.draft.bumpDataRevision(),
    setReading: method => this.actions.setReading(method),
    clearActing: () => this.actions.clearActing(),
    putBack: () => this.putBack(),
  });

  private readonly saver: FormSaveRunner = new FormSaveRunner({
    api: this.api,
    alert: this.alert,
    confirmation: this.confirmation,
    ref: this.ref,
    host: this.host,
    mutations: this.mutations,
    lifetime: this.lifetime,
    address: () => this.address?.(),
    service: () => this.service(),
    model: () => this.model(),
    id: () => this.id(),
    ver: () => this.ver(),
    value: () => this.value(),
    outcome: () => this.outcome(),
    canSave: () => this.canSave(),
    isBusy: () => this.isBusy(),
    capture: () => this.capture(),
    refuseParseErrors: () => this.refuseParseErrors(),
    setOutcome: outcome => this._outcome.set(outcome),
    adopt: data => this.adopt(data),
    bumpDataRevision: () => this.draft.bumpDataRevision(),
    setReading: method => this.actions.setReading(method),
    readInPlace: (service, id, marksStale) => this.reader.readInPlace(service, id, marksStale),
    beginOperation: () => this.beginOperation(),
    releaseOperation: op => this.releaseOperation(op),
    reportFailure: error => this.reportFailure(error),
  });

  readonly isSaving: Signal<boolean> = this.saver.isSaving;
  readonly isRecovering: Signal<boolean> = this.reader.isRecovering;
  readonly isStale: Signal<boolean> = this.reader.isStale;

  private readonly actions: FormActions = new FormActions({
    api: this.api,
    alert: this.alert,
    asks: this.asks,
    confirmation: this.confirmation,
    overlay: this.overlay,
    ref: this.ref,
    injector: this.injector,
    mutations: this.mutations,
    address: () => this.address?.(),
    service: () => this.service(),
    model: () => this.model(),
    id: () => this.id(),
    ver: () => this.ver(),
    isDirty: () => this.isDirty(),
    canAct: () => this.canAct(),
    capture: () => this.capture(),
    saveBeforeAct: () => this.saver.performSave('act'),
    beginOperation: () => this.beginOperation(),
    releaseOperation: op => this.releaseOperation(op),
    showsMessage: path => this.draft.showsMessage(path),
    reportFailure: (error, own) => this.reportFailure(error, own),
    readAgain: (service, id, method, isCurrent) => this.reader.readAgain(service, id, method, isCurrent),
  });

  readonly isActing: Signal<boolean> = this.actions.isActing;
  readonly actingMethod: Signal<string | null> = this.actions.actingMethod;

  readonly isBusy: Signal<boolean> = computed(() => this.isSaving() || this.isRecovering() || this.isActing());
  readonly canSave: Signal<boolean> = computed(
    () => !!this.service() && !this.isReadOnly() && !this.isBusy() && !this.lifecycle?.isLocked() && !this.isUnsettled(),
  );
  readonly canAct: Signal<boolean> = computed(
    () =>
      !!this.service() &&
      !this.isBusy() &&
      !this.lifecycle?.isLocked() &&
      !this.isUnsettled() &&
      !(this.isReadOnly() && this.id() === null),
  );
  readonly canRecover: Signal<boolean> = computed(
    () =>
      !this.lifecycle?.isLocked() &&
      this.isUnsettled() &&
      (this.id() !== null || (this.outcome() === 'unknown' && this.saver.hasKeptCreate())),
  );

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.reset();
      this.lifetime.end();
    });
  }

  setModel(model: FormModel | null): void {
    this.draft.setModel(model);
    this.setData(null);
  }

  setData(data: FormDataDto | null | undefined): void {
    this.invalidate();
    this.adopt(data);
    if (data) {
      this.actions.clearActing();
      this.putBack();
    }
  }

  setInput(input: Signal<FormInput>): void {
    this.draft.setInput(input);
  }

  setMessages(messages: readonly MessageDto[]): void {
    this.draft.setMessages(messages);
  }

  reset(): void {
    this.invalidate();
    this.actions.reset();
    this.draft.setModel(null);
    this.adopt(null);
  }

  onInvalidate(action: () => void): () => void {
    this.invalidations.add(action);
    return () => this.invalidations.delete(action);
  }

  onRefused(handler: (messages: readonly MessageDto[]) => void): () => void {
    return this.draft.onRefused(handler);
  }

  capture(): () => boolean {
    return this.lifetime.capture();
  }

  fieldOf<T>(path: string): FieldTree<T> {
    return this.draft.fieldOf<T>(path);
  }

  valueOf(path: string): unknown {
    return this.draft.valueOf(path);
  }

  setValueAt(path: string, value: unknown): void {
    this.draft.setValueAt(path, value);
  }

  patchValueAt(path: string, value: unknown): void {
    this.draft.patchValueAt(path, value);
  }

  save(): Promise<FormDataDto | null> {
    return this.saver.performSave('stay');
  }

  async saveAndClose(): Promise<void> {
    await this.saver.performSave('close');
  }

  async saveAndOpen(page: string): Promise<FormDataDto | null> {
    if (this.id() !== null || !this.canSave()) {
      return null;
    }
    this._pageToOpen.set(page);
    const saved = await this.saver.performSave('stay');
    if (!saved) {
      this._pageToOpen.set(null);
    }
    return saved;
  }

  pageOpened(): void {
    this._pageToOpen.set(null);
  }

  saveForClose(approval: CloseApproval): Promise<FormDataDto | null> {
    return this.saver.performSave({ approval });
  }

  act(action: FormNavActionModel): Promise<void> {
    return this.actions.act(action);
  }

  async recover(): Promise<void> {
    const isCurrent = this.capture();
    const service = this.service();
    if (!this.canRecover() || this.isBusy() || !isCurrent() || !service) {
      return;
    }
    const id = this.id();
    if (id === null) {
      await this.saver.recoverCreate(service, isCurrent, (recovering: boolean) => this.reader.setRecovering(recovering));
      return;
    }
    await this.reader.recoverRecord(service, id, this.outcome() === 'unknown', isCurrent);
  }

  needsProtection(): boolean {
    return this.isDirty() || this.isUnsettled() || (this.isBusy() && !this.awaitsScreen());
  }

  absorb(affected: AffectedDto): boolean {
    return this.draft.absorb(affected);
  }

  notifyChildMutated(event?: ScreenMutationEvent): void {
    this.draft.bumpDataRevision();
    const service = this.service();
    const id = this.id();
    const match = service && id !== null ? event?.affected?.find(a => a.service === service && a.id === id) : undefined;
    if (match && this.absorb(match)) {
      if (!this.isDirty() && this.isIdle() && this.host && service && id !== null) {
        void this.reader.readInPlace(service, id, false);
      }
      return;
    }
    this.refresh();
  }

  refresh(): void {
    this.reader.refresh();
  }

  reload(): Promise<void> {
    return this.reader.reload();
  }

  acceptsClose(result: unknown): boolean {
    return this.saver.acceptsClose(result, this.isBusy(), this.awaitsScreen());
  }

  refuseParseErrors(): readonly MessageDto[] {
    return this.draft.refuseParseErrors();
  }

  private awaitsScreen(): boolean {
    return this.actions.isReading() && !this.isSaving() && !this.isRecovering();
  }

  private isIdle(): boolean {
    return !this.isBusy() && this.actingMethod() === null && !this.lifecycle?.isLocked();
  }

  private adopt(data: FormDataDto | null | undefined): void {
    this.saver.clearKeptCreate();
    this._outcome.set(null);
    this.reader.clearStale();
    this.draft.adoptData(data);
  }

  private invalidate(): void {
    this.finishOperation();
    this.lifetime.invalidate();
    for (const action of this.invalidations) {
      action();
    }
    this.saver.resetInvalidation();
    this.reader.resetInvalidation();
  }

  private beginOperation(): ScreenOperation | undefined {
    const op = this.mutations?.begin();
    this.operation = op;
    return op;
  }

  private finishOperation(): void {
    if (this.operation) {
      this.mutations?.finish(this.operation);
      this.operation = undefined;
    }
  }

  private releaseOperation(operation: ScreenOperation | undefined): void {
    if (operation) {
      this.mutations?.finish(operation);
      if (this.operation === operation) {
        this.operation = undefined;
      }
    }
  }

  private putBack(): void {
    const messages = this.actions.takeKeptMessages(this.id());
    if (messages) {
      this.draft.setMessages(messages);
    }
  }

  private reportFailure(error: unknown, own: readonly MessageDto[] = []): void {
    const { outcome, fieldMessages } = routeFormFailure({
      error,
      own,
      alert: this.alert,
      texts: this.texts,
      conflictTitle: this.model()?.mutationTexts.conflictTitle,
      showsMessage: path => this.draft.showsMessage(path),
      elementOf: path => this.draft.elementOf(path),
    });
    this._outcome.set(outcome);
    this.draft.setMessages(fieldMessages);
  }
}
