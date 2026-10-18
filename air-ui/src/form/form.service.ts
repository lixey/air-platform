import { HttpClient } from '@angular/common/http';
import { computed, DestroyRef, inject, Injector, linkedSignal, Service, type Signal, signal } from '@angular/core';
import { type FieldTree, form } from '@angular/forms/signals';
import { firstValueFrom } from 'rxjs';
import { AlertService } from '../app/alert/alert.service';
import { shownByCaller } from '../app/alert/error-alert.interceptor';
import { withIdempotencyKey } from '../app/auth/credentials.interceptor';
import { LocaleService } from '../app/locale.service';
import { Api, type RequestOf } from '../core/api';
import type { CloseApproval } from '../core/close-decision';
import type {
  FormDataDto,
  FormElementDto,
  FormModel,
  FormNavActionModel,
  FormRequest,
  FormServiceEndpoints,
  MessageDto,
  MutationOutcome,
  ScreenDto,
} from '../core/generated/contracts';
import { Lifetime } from '../core/lifetime';
import { classifyMutationFailure, refusalDetailOf } from '../core/mutation-failure';
import { problemOf } from '../core/problem';
import { TextsService } from '../core/texts.service';
import type { Unavailable } from '../nav/nav-availability';
import { AskService } from '../overlay/ask.service';
import { ConfirmationService } from '../overlay/confirmation.service';
import { OverlayRef } from '../overlay/overlay-ref';
import { OverlayService } from '../overlay/overlay.service';
import { parentParams, SCREEN_ADDRESS, SCREEN_HOST } from '../screen/screen-address';
import { ScreenLifecycle } from '../screen/screen-lifecycle';
import { SCREEN_LIFETIME } from '../screen/screen-lifetime';
import { ScreenMutations, type ScreenOperation } from '../screen/screen-mutations';
import { FormActions } from './form-actions';
import { formFieldRules, isFieldDisabled } from './form-field-rules';
import {
  canonicalJson,
  draftOf,
  frozenCopy,
  hasSecret,
  inputModelOf,
  isFormData,
  isSecret,
  stripUntouchedSecrets,
} from './form-value';

export { isFormData } from './form-value';

/** A create whose outcome is open: its idempotency key and the submission posted under it. */
interface KeptCreate {
  readonly key: string;
  readonly submission: RequestOf<FormServiceEndpoints, 'save'>;
}

/** What a form of input alone is made of: the dialog's elements and the values its opener gives. */
export interface FormInput {
  readonly elements: readonly FormElementDto[];
  readonly value: Readonly<Record<string, unknown>>;
}

/** One form owns its draft, confirmed baseline and the result of its current save. */
@Service({ autoProvided: false })
export class FormService {
  private static next = 0;

  private readonly api = inject(Api);
  private readonly http = inject(HttpClient);
  private readonly alert = inject(AlertService);
  private readonly texts = inject(TextsService);
  /** The user's clock, on which a date cut of an instant is compared. */
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

  /** The number of this form among the forms made, which its field ids carry. */
  private readonly instance = ++FormService.next;
  /** The signal a form of input alone follows; none for a record's form. */
  private readonly input = signal<Signal<FormInput> | null>(null);
  private readonly given = computed<FormInput | null>(() => this.input()?.() ?? null);
  /** What the form of input alone opens with: its elements and the value they hold before the first edit. */
  private readonly opening = computed<FormInput | null>(() => {
    const input = this.given();
    if (!input) {
      return null;
    }
    const unchecked = Object.fromEntries(
      input.elements.filter(element => element.type === 'checkbox').map(element => [element.path, false]),
    );
    return { elements: input.elements, value: frozenCopy(draftOf({ ...unchecked, ...input.value }, input.elements)) };
  });

  private readonly _model = linkedSignal<FormInput | null, FormModel | null>({
    source: this.given,
    computation: input => (input ? frozenCopy(inputModelOf(input.elements)) : null),
  });
  readonly model = this._model.asReadonly();
  /** The prefix of the DOM ids of this form's fields, its own among the forms of the document: `form-7`, `ask-8`. */
  readonly idScope: Signal<string> = computed(() => `${this.input() ? 'ask' : 'form'}-${this.instance}`);
  private readonly _id = signal<number | null>(null);
  readonly id = this._id.asReadonly();
  private readonly _ver = signal<number | null>(null);
  readonly ver = this._ver.asReadonly();
  private readonly _value = linkedSignal<FormInput | null, Record<string, unknown>>({
    source: this.opening,
    computation: opening => opening?.value ?? frozenCopy({}),
  });
  readonly value = this._value.asReadonly();
  private readonly _adoption = signal(0);
  /** Counts the records the form adopted: one given, one read again, the answer of a save or of an action, and none. */
  readonly adoption = this._adoption.asReadonly();
  private readonly _unavailable = signal<Unavailable>(frozenCopy({}));
  /** What the record the form holds does not offer; empty for a new record and for a record that offers all. */
  readonly unavailable = this._unavailable.asReadonly();
  /** The record the form was opened or last saved with: its elements and its value, read in the current zone. */
  private readonly baseline = linkedSignal<FormInput | null, FormInput | null>({
    source: this.opening,
    computation: opening => opening,
  });
  /** The baseline's canonical text on the user's current clock; `[]` with no baseline. */
  private readonly baselineText = computed(() => {
    const baseline = this.baseline();
    return baseline ? canonicalJson(baseline.value, baseline.elements, this.zone()) : '[]';
  });
  private readonly _messages = linkedSignal<Record<string, unknown>, readonly MessageDto[]>({
    source: this._value,
    computation: (value, previous) =>
      previous?.source ? previous.value.filter(m => !m.path || value[m.path] === (previous.source as Record<string, unknown>)[m.path]) : [],
  });
  readonly messages = this._messages.asReadonly();
  private readonly _pageToOpen = signal<string | null>(null);
  /** The child page a press asked for, until the layout that holds it arrives or its save is refused. */
  readonly pageToOpen = this._pageToOpen.asReadonly();
  private readonly _isSaving = signal(false);
  readonly isSaving = this._isSaving.asReadonly();
  private readonly _isRecovering = signal(false);
  readonly isRecovering = this._isRecovering.asReadonly();
  private readonly _outcome = signal<MutationOutcome | null>(null);
  readonly outcome = this._outcome.asReadonly();
  private readonly _isStale = signal(false);
  /** Whether a change under the form found it unable to read again: the server holds a newer record than the form. */
  readonly isStale = this._isStale.asReadonly();
  /** Counts the reads in place the form asked: the answer of any but the last is dropped. */
  private reads = 0;
  private closingResult: FormDataDto | undefined;
  private operation: ScreenOperation | undefined;
  /** The create posted last while its answer has not settled it: a commit, a refusal, a roll-back or a conflict. */
  private readonly keptCreate = signal<KeptCreate | null>(null);
  /** A child of the screen's lifetime, current while the overlay the form stands in is open; every record starts an epoch. */
  private readonly lifetime = (inject(SCREEN_LIFETIME, { optional: true }) ?? new Lifetime()).child(() => !this.ref?.isClosed);
  private readonly invalidations = new Set<() => void>();
  private readonly refusals = new Set<(messages: readonly MessageDto[]) => void>();

  readonly service = computed(() => this.model()?.service ?? null);
  readonly isReadOnly = computed(() => this.model()?.isReadOnly ?? true);

  /** The elements drawn: the declared ones minus the hidden, which are saved and not shown. */
  readonly elements = computed<readonly FormElementDto[]>(() => (this.model()?.elements ?? []).filter(e => !e.isHidden));

  /** Whether the value differs from what the record was opened or last saved with. */
  readonly isDirty = computed(() => {
    const value = this.value();
    const baseline = this.baseline();
    const elements = this.model()?.elements ?? [];
    if (baseline && value === baseline.value) {
      return elements.some(e => isSecret(e) && !e.isDisabled && !e.isServerOwned && hasSecret(value[e.path]));
    }
    return (
      canonicalJson(value, elements, this.zone()) !== this.baselineText() ||
      elements.some(e => isSecret(e) && !e.isDisabled && !e.isServerOwned && hasSecret(value[e.path]))
    );
  });

  /** Whether the form stands in an overlay it can close. */
  readonly canClose = computed(() => this.ref !== null);

  /** Whether the last save left the record unsettled: refused for a newer version, or of an outcome nobody knows. */
  private readonly isUnsettled = computed(() => this.outcome() === 'conflict' || this.outcome() === 'unknown');

  private readonly actions: FormActions = new FormActions({
    http: this.http,
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
    saveBeforeAct: () => this.performSave('act'),
    beginOperation: () => {
      const op = this.mutations?.begin();
      this.operation = op;
      return op;
    },
    releaseOperation: op => this.releaseOperation(op),
    showsMessage: path => this.showsMessage(path),
    reportFailure: (error, own) => this.reportFailure(error, own),
    readAgain: (service, id, method, isCurrent) => this.readAgain(service, id, method, isCurrent),
  });

  /**
   * Whether an action is under way: from its post until the screen read again after it is adopted. True also while
   * the host reads the screen of a new record after its first save.
   */
  readonly isActing = this.actions.isActing;
  /** The method of the action under way, from the press: the button that shows the busy mark. */
  readonly actingMethod = this.actions.actingMethod;

  readonly isBusy = computed(() => this.isSaving() || this.isRecovering() || this.isActing());
  readonly canSave = computed(
    () => !!this.service() && !this.isReadOnly() && !this.isBusy() && !this.lifecycle?.isLocked() && !this.isUnsettled(),
  );
  /** Whether an action may start: a read-only form acts on a persisted record alone, since it cannot save a new one first. */
  readonly canAct = computed(
    () =>
      !!this.service() &&
      !this.isBusy() &&
      !this.lifecycle?.isLocked() &&
      !this.isUnsettled() &&
      !(this.isReadOnly() && this.id() === null),
  );
  /** Whether the record can be read again after an unsettled save, or a create of unknown outcome posted again under its key. */
  readonly canRecover = computed(
    () =>
      !this.lifecycle?.isLocked() &&
      this.isUnsettled() &&
      (this.id() !== null || (this.outcome() === 'unknown' && this.keptCreate() !== null)),
  );

  private readonly elementsByPath = computed(() => {
    const map = new Map<string, FormElementDto>();
    for (const element of this.model()?.elements ?? []) {
      if (element.path) {
        map.set(element.path, element);
      }
      if (element.referencePath) {
        map.set(element.referencePath, element);
      }
    }
    return map;
  });

  private readonly tree = form(
    this._value,
    formFieldRules({
      elementOf: path => this.elementOf(path),
      isReadOnly: () => this.isReadOnly(),
      refusesInput: () => this.isBusy() || !!this.lifecycle?.isLocked(),
      messages: () => this.messages(),
    }),
    { name: 'form' },
  );

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.reset();
      this.lifetime.end();
    });
  }

  setModel(model: FormModel | null): void {
    this._model.set(frozenCopy(model));
    this.setData(null);
  }

  /** The record as the server answered it: the value becomes the one the user edits and the one changes are measured from. */
  setData(data: FormDataDto | null | undefined): void {
    this.invalidate();
    this.adopt(data);
    // No record is the window in which the screen loads again, after an action or after the first save of a new
    // record: the form stays busy through it.
    if (data) {
      this.actions.clearActing();
      this.putBack();
    }
  }

  /** Makes this a form of input alone that follows `input`: no record and no service, so it never saves, recovers or acts. */
  setInput(input: Signal<FormInput>): void {
    this.input.set(input);
  }

  /** Puts a refusal's messages on the fields their paths name; a message leaves when its field's value changes. */
  setMessages(messages: readonly MessageDto[]): void {
    this._messages.set(frozenCopy(messages));
    this.tellRefused();
  }

  reset(): void {
    this.invalidate();
    this.actions.reset();
    this._model.set(null);
    this.adopt(null);
  }

  onInvalidate(action: () => void): () => void {
    this.invalidations.add(action);
    return () => this.invalidations.delete(action);
  }

  /**
   * Hands `handler` the messages of a refusal that stand on the fields, once they do: of a save, an action or a status
   * item. A message the alert says is not among them, and a refusal the alert says whole tells nobody.
   */
  onRefused(handler: (messages: readonly MessageDto[]) => void): () => void {
    this.refusals.add(handler);
    return () => this.refusals.delete(handler);
  }

  /** Whether what is captured now is still current: this record's epoch, the screen's lifetime and the overlay still open. */
  capture(): () => boolean {
    return this.lifetime.capture();
  }

  fieldOf<T>(path: string): FieldTree<T> {
    const field = this.tree[path];
    if (!field) {
      throw new Error(`The form has no field at "${path}"`);
    }
    return field as unknown as FieldTree<T>;
  }

  valueOf(path: string): unknown {
    return this.value()[path];
  }

  /** Writes one field through the field tree; refused when absent, disabled or readonly. */
  setValueAt(path: string, value: unknown): void {
    const field = this.tree[path];
    if (!field) {
      return;
    }
    const state = field();
    if (state.disabled() || state.readonly()) {
      return;
    }
    state.value.set(frozenCopy(value));
  }

  /** Only a confirmed commit replaces the draft or permits save-driven closure. */
  save(): Promise<FormDataDto | null> {
    return this.performSave('stay');
  }

  async saveAndClose(): Promise<void> {
    await this.performSave('close');
  }

  /**
   * The save of `save()`, asked by a child's button of a new record: after its commit the page named is opened. Nothing
   * for a form that holds an id or that may not save.
   */
  async saveAndOpen(page: string): Promise<FormDataDto | null> {
    if (this.id() !== null || !this.canSave()) {
      return null;
    }
    this._pageToOpen.set(page);
    const saved = await this.performSave('stay');
    if (!saved) {
      this._pageToOpen.set(null);
    }
    return saved;
  }

  /** The form panel opened the page: the wish is spent. */
  pageOpened(): void {
    this._pageToOpen.set(null);
  }

  saveForClose(approval: CloseApproval): Promise<FormDataDto | null> {
    return this.performSave({ approval });
  }

  /**
   * Runs an action of the nav over the open record: asks its confirmation, or its input in a dialog that stays open
   * while a refusal names one of its fields, saves a dirty or new form first, posts the record's id and version with
   * the dialog's values, and after a commit reads the screen again, busy until that screen's record is adopted.
   */
  act(action: FormNavActionModel): Promise<void> {
    return this.actions.act(action);
  }

  /**
   * Conflict reload discards explicitly; an uncertain result is read and then offered for adoption; an uncertain create
   * is posted again under its key.
   */
  async recover(): Promise<void> {
    const isCurrent = this.capture();
    const service = this.service();
    if (!this.canRecover() || this.isBusy() || !isCurrent() || !service) {
      return;
    }
    const kept = this.keptCreate();
    if (this.id() === null) {
      if (kept) {
        await this.recoverCreate(service, kept, isCurrent);
      }
      return;
    }
    const id = this.id();
    const uncertain = this.outcome() === 'unknown';
    this._isRecovering.set(true);
    try {
      if (!uncertain && !(await this.confirmReplacement())) {
        return;
      }
      if (!isCurrent()) {
        return;
      }
      const data = (await this.read(service, id))?.data;
      if (!isCurrent() || !isFormData(data) || data.id !== id) {
        return;
      }
      if (uncertain && !(await this.confirmReplacement())) {
        return;
      }
      if (isCurrent()) {
        // A read is a new baseline, never evidence that the previous save committed.
        this.adopt(data);
      }
    } catch (error: unknown) {
      // A failure the form no longer waits for is dropped: its record was replaced or its overlay closed.
      if (isCurrent()) {
        this.reportReadFailure(error);
      }
    } finally {
      if (isCurrent()) {
        this._isRecovering.set(false);
      }
    }
  }

  /** Whether the form currently holds changes or active operations needing protection. */
  needsProtection(): boolean {
    return this.isDirty() || this.isUnsettled() || (this.isBusy() && !this.awaitsScreen());
  }

  /** What a change under the form asks: a clean idle form reads its screen again in place, any other turns stale. */
  refresh(): void {
    const service = this.service();
    const id = this.id();
    if (!service || id === null || this.isUnsettled()) {
      return;
    }
    if (this.isDirty() || !this.isIdle()) {
      this._isStale.set(true);
      return;
    }
    void this.readInPlace(service, id, true);
  }

  /** Reads the screen again for the stale notice, in place; over a draft it first asks that the draft be replaced. */
  async reload(): Promise<void> {
    const isCurrent = this.capture();
    const service = this.service();
    const id = this.id();
    if (!this.isStale() || !service || id === null || this.isBusy() || this.lifecycle?.isLocked() || !isCurrent()) {
      return;
    }
    this._isRecovering.set(true);
    try {
      if (this.isDirty() && !(await this.confirmReplacement())) {
        return;
      }
      if (!isCurrent()) {
        return;
      }
      const read = ++this.reads;
      const screen = await this.read(service, id);
      const data = screen?.data;
      if (isCurrent() && read === this.reads && isFormData(data) && data.id === id) {
        // Nothing of the form is busy when its host hands the answer back: the draft was given up above
        this._isRecovering.set(false);
        this.take(screen, data);
      }
    } catch (error: unknown) {
      if (isCurrent()) {
        this.reportReadFailure(error);
      }
    } finally {
      if (isCurrent()) {
        this._isRecovering.set(false);
      }
    }
  }

  /** Whether the result of an attempted closure is acceptable to the form. */
  acceptsClose(result: unknown): boolean {
    return !this.isBusy() || this.awaitsScreen() || (!!this.closingResult && result === this.closingResult);
  }

  /**
   * Nothing of the form is in flight: it waits for the screen an action of its own, or the first save of a new record,
   * asked its host for.
   */
  private awaitsScreen(): boolean {
    return this.actions.isReading() && !this.isSaving() && !this.isRecovering();
  }

  /** Whether nothing of the form is under way: no save, recovery or action from its press on, and the screen not locked. */
  private isIdle(): boolean {
    return !this.isBusy() && this.actingMethod() === null && !this.lifecycle?.isLocked();
  }

  /** Every record the form adopts is its new baseline and ends the stale state. */
  private adopt(data: FormDataDto | null | undefined): void {
    // A record adopted settles the create kept: its own commit, another record or none
    this.keptCreate.set(null);
    this._outcome.set(null);
    this._isStale.set(false);
    this._id.set(data?.id ?? null);
    this._ver.set(data?.ver ?? null);
    this._unavailable.set(frozenCopy(data?.unavailable ?? {}));
    const elements = this.model()?.elements ?? [];
    const value = draftOf(data?.value ?? {}, elements);
    this._value.set(frozenCopy(value));
    this.baseline.set({ elements, value: frozenCopy(value) });
    this._messages.set([]);
    this._adoption.update(count => count + 1);
  }

  /** Starts a new epoch of the record: every capture taken before goes stale, the operation ends and nothing is busy. */
  private invalidate(): void {
    this.finishOperation();
    this.lifetime.invalidate();
    for (const action of this.invalidations) {
      action();
    }
    this._isSaving.set(false);
    this._isRecovering.set(false);
  }

  private finishOperation(): void {
    if (this.operation) {
      this.mutations?.finish(this.operation);
      this.operation = undefined;
    }
  }

  private elementOf(path: string): FormElementDto | undefined {
    return this.elementsByPath().get(path);
  }

  /** Whether the element at `path` can show a message: the form draws it and its field takes errors. */
  private showsMessage(path: string): boolean {
    const element = this.elementOf(path);
    return !!element && !element.isHidden && !isFieldDisabled(element, () => this.isReadOnly());
  }

  /** The form's screen, its layout and its record, as the server holds it now. */
  private read(service: string, id: number | null): Promise<ScreenDto | null> {
    return firstValueFrom(
      this.api.postTo<FormServiceEndpoints, 'get-data'>(
        service,
        'get-data',
        { ...parentParams(this.address?.(), service), id },
        shownByCaller(),
      ),
    );
  }

  /**
   * The read in place: posts the screen of record `id` and takes the answer while the form is the one that asked, holds
   * no draft and is idle. `marksStale` is the read a change under the form asked: an answer the form cannot take and a
   * failed read turn it stale, with no alert; the read after the form's own save leaves it as the save did.
   */
  private async readInPlace(service: string, id: number, marksStale: boolean): Promise<void> {
    const isCurrent = this.capture();
    const read = ++this.reads;
    const adoption = this.adoption();
    // Dropped: another record or a closed overlay, a later read, and a record adopted since, which is the newer one
    const isAwaited = () => isCurrent() && read === this.reads && adoption === this.adoption();
    let screen: ScreenDto | null;
    try {
      screen = await this.read(service, id);
    } catch {
      // An unsettled save is left by an explicit decision alone: a read that failed meanwhile marks nothing
      if (marksStale && isAwaited() && !this.isUnsettled()) {
        this._isStale.set(true);
      }
      return;
    }
    const data = screen?.data;
    // A conflict and an unknown outcome are left by an explicit decision alone: the answer is dropped, the form not stale
    if (!isAwaited() || !isFormData(data) || data.id !== id || this.isUnsettled()) {
      return;
    }
    if (this.isDirty() || !this.isIdle()) {
      if (marksStale) {
        this._isStale.set(true);
      }
      return;
    }
    this.take(screen, data);
  }

  /**
   * Takes the answer of a read in place: the form adopts the record, so that it is clean and idle, and its host shows
   * the screen, whose navs and elements come back through the form panel's inputs; without a host the record is all.
   */
  private take(screen: ScreenDto | null, data: FormDataDto): void {
    this.adopt(data);
    if (screen) {
      this.host?.show(screen);
    }
  }

  private reportReadFailure(error: unknown): void {
    const problem = problemOf(error);
    const title = problem?.title ?? this.model()?.mutationTexts.unknownTitle;
    if (title) {
      this.alert.error(title, problem?.detail);
    }
  }

  /**
   * Reads the screen again after an action, the form busy until its record is adopted: the host loads the screen, nav
   * included, and `setData` ends the action, the form only waiting meanwhile; without a host the form reads its
   * record itself, its read in flight as the post was.
   */
  private readAgain(service: string, id: number, method: string | null, isCurrent: () => boolean): void {
    if (this.host) {
      this.actions.setReading(method);
      this.host.reload({ id });
    } else {
      void this.readRecord(service, id, isCurrent);
    }
  }

  private async readRecord(service: string, id: number, isCurrent: () => boolean): Promise<void> {
    try {
      const data = (await this.read(service, id))?.data;
      if (isCurrent() && isFormData(data) && data.id === id) {
        this.adopt(data);
        this.putBack();
      }
    } catch (error: unknown) {
      if (isCurrent()) {
        this.reportReadFailure(error);
      }
    } finally {
      // A read that is no longer current leaves the acting state: it is of the record the form shows now.
      if (isCurrent()) {
        this.actions.clearActing();
      }
    }
  }

  /** `'act'` is the save an action asks for first: it stays, and leaves the reading of a new record's screen to the action. */
  private async performSave(mode: 'stay' | 'close' | 'act' | { approval: CloseApproval }): Promise<FormDataDto | null> {
    const isCurrent = this.capture();
    const service = this.service();
    if (!this.canSave() || !isCurrent() || !service) {
      return null;
    }
    const wasNew = this.id() === null;
    const version = this.lifetime.epoch();
    const operation = this.mutations?.begin();
    this.operation = operation;
    const submitted = this.buildSubmission();
    // A create travels under a key kept until its answer settles it
    const key = wasNew ? (this.keptCreate()?.key ?? crypto.randomUUID()) : null;
    if (key !== null) {
      this.keptCreate.set({ key, submission: submitted });
    }
    this._isSaving.set(true);
    let isHandedOff = false;
    try {
      const options = key === null ? shownByCaller() : withIdempotencyKey(key);
      const answer = await firstValueFrom(this.api.postTo<FormServiceEndpoints, 'save'>(service, 'save', submitted, options));
      if (!isCurrent()) {
        return null;
      }
      if (answer?.outcome !== 'committed' || !isFormData(answer)) {
        // An answer without a commit is a refusal, shown by the one rule; the server sends none in a 2xx.
        this.reportFailure(answer);
        this.settleCreate();
        return null;
      }
      const saved = this.adoptCommit(answer, service, operation, wasNew && mode === 'stay');
      if (mode === 'stay' && !wasNew && this.host && saved.id != null) {
        // The navs read the record as the save and its hooks left it
        void this.readInPlace(service, saved.id, false);
      }
      if (mode === 'close' && isCurrent()) {
        this.closingResult = saved;
        await this.ref?.close(saved, operation?.id);
      } else if (typeof mode === 'object') {
        this.handOff(mode.approval, saved, operation, isCurrent);
        isHandedOff = true;
      }
      return saved;
    } catch (error: unknown) {
      if (isCurrent()) {
        this.reportFailure(error);
        this.settleCreate();
      }
      return null;
    } finally {
      if (!isHandedOff) {
        this.releaseOperation(operation);
      }
      // Reset isSaving and closingResult for this record generation even if the overlay closed meanwhile.
      if (!this.lifetime.isEnded && this.lifetime.epoch() === version) {
        this.closingResult = undefined;
        this._isSaving.set(false);
      }
    }
  }

  /**
   * Posts the create kept under its key again, after the draft-loss decision when the value shown is no longer the
   * submission kept, and takes its answer as a save that stays takes its own: a commit becomes the record, any other
   * answer is reported as a save's failure, and an unknown one keeps the create for another recovery.
   */
  private async recoverCreate(service: string, kept: KeptCreate, isCurrent: () => boolean): Promise<void> {
    this._isRecovering.set(true);
    let operation: ScreenOperation | undefined;
    try {
      if (JSON.stringify(this.buildSubmission()) !== JSON.stringify(kept.submission) && !(await this.confirmReplacement())) {
        return;
      }
      if (!isCurrent()) {
        return;
      }
      operation = this.mutations?.begin();
      this.operation = operation;
      const answer = await firstValueFrom(
        this.api.postTo<FormServiceEndpoints, 'save'>(service, 'save', kept.submission, withIdempotencyKey(kept.key)),
      );
      if (!isCurrent()) {
        return;
      }
      if (answer?.outcome !== 'committed' || !isFormData(answer)) {
        this.reportFailure(answer);
        this.settleCreate();
        return;
      }
      this.adoptCommit(answer, service, operation, true);
    } catch (error: unknown) {
      if (isCurrent()) {
        this.reportFailure(error);
        this.settleCreate();
      }
    } finally {
      this.releaseOperation(operation);
      if (isCurrent()) {
        this._isRecovering.set(false);
      }
    }
  }

  /** A create's answer other than a commit drops its key unless its outcome is unknown, so the next create takes a new one. */
  private settleCreate(): void {
    if (this.outcome() !== 'unknown') {
      this.keptCreate.set(null);
    }
  }

  /**
   * A committed answer becomes the record: its value the new baseline, its warnings shown, a new record's screen
   * reloaded at its id when the form stays, and the save published to the screen. Where a host holds the screen, the
   * form waits for that screen in the acting state of phase `reading`, with no method, until its record is adopted.
   * Answers the record as saved; the save of a stored record that stays under a host is followed by the read in
   * place, which its caller starts.
   */
  private adoptCommit(answer: FormDataDto, service: string, operation: ScreenOperation | undefined, reloads: boolean): FormDataDto {
    this.adopt(answer);
    this.alert.warnings(answer.warnings, this.model()?.mutationTexts.committedWarningTitle);
    if (reloads && this.host) {
      this.actions.setReading(null);
      this.host.reload({ id: answer.id });
    }
    const saved = { ...answer, value: this.value() };
    if (operation) {
      this.mutations?.publish(operation, { service, recordId: saved.id, operation: 'save' });
    }
    return saved;
  }

  /**
   * A save asked for by a close hands the close its result, the operation that caused it and the release of that
   * operation; the approval holds while the value saved is the value shown and nothing is busy.
   */
  private handOff(approval: CloseApproval, saved: FormDataDto, operation: ScreenOperation | undefined, isCurrent: () => boolean): void {
    const value = this.value();
    approval.result = saved;
    approval.cause = operation?.id;
    approval.isCurrent = () => isCurrent() && this.value() === value && !this.isBusy();
    approval.release = () => this.releaseOperation(operation);
  }

  /** What a save posts: the value without the secrets the user did not type, with the parent parameters, `id` and `ver`. */
  private buildSubmission(): FormRequest {
    const service = this.service();
    const value = stripUntouchedSecrets(this.value(), this.model()?.elements ?? []);
    return frozenCopy({ ...parentParams(this.address?.(), service), id: this.id(), ver: this.ver(), value });
  }

  private releaseOperation(operation: ScreenOperation | undefined): void {
    if (operation) {
      this.mutations?.finish(operation);
      if (this.operation === operation) {
        this.operation = undefined;
      }
    }
  }

  private confirmReplacement(): Promise<boolean> {
    const model = this.model();
    if (!model) {
      return Promise.resolve(false);
    }
    return this.confirmation.confirm({
      title: model.dirtyDialog.title,
      message: model.dirtyDialog.description,
      confirmLabel: model.mutationTexts.reloadLabel,
      cancelLabel: model.dirtyDialog.continueLabel,
    });
  }

  /** The messages kept for the record read again stand on its fields, when it is that record; they are kept once. */
  private putBack(): void {
    const messages = this.actions.takeKeptMessages(this.id());
    if (messages) {
      this._messages.set(messages);
      this.tellRefused();
    }
  }

  /** Hands the messages that stand on the fields to whoever asked for a refusal's. */
  private tellRefused(): void {
    if (this.messages().length) {
      for (const handler of this.refusals) {
        handler(this.messages());
      }
    }
  }

  /**
   * Shows a failure, each message once, by one rule for a save, an action and a status item: on the
   * field of its element when the element can show it, else in the alert, led by the element's label, a line a
   * message. An action passes `own`, the messages its dialog shows: they are left out. The form keeps only the
   * messages that stand on its fields.
   */
  private reportFailure(error: unknown, own: readonly MessageDto[] = []): void {
    const failure = classifyMutationFailure(error);
    this._outcome.set(failure.outcome);
    const messages = own.length ? failure.messages.filter(m => !own.includes(m)) : failure.messages;
    const shown = messages.filter(m => !!m.path && this.showsMessage(m.path));
    this._messages.set(frozenCopy(shown));
    this.tellRefused();
    const general = messages.filter(m => !shown.includes(m)).map(m => this.namedOf(m));
    if (failure.outcome === 'unknown') {
      if (general.length) {
        this.alert.open(general);
      }
      return;
    }
    if (failure.outcome === 'conflict' || general.length || !failure.messages.length) {
      const fallback = failure.outcome === 'conflict' ? this.model()?.mutationTexts.conflictTitle : this.texts.of('RequestFailed');
      const title = failure.title ?? fallback;
      if (title) {
        this.alert.error(title, refusalDetailOf(failure, general, '\n'));
      }
    }
  }

  /** A message the alert takes from an element of the form, led by that element's label; any other as it is. */
  private namedOf(message: MessageDto): MessageDto {
    const label = message.path ? this.elementOf(message.path)?.label : undefined;
    const text = message.title ?? message.detail;
    return label && text ? { ...message, title: `${label}: ${text}` } : message;
  }
}
