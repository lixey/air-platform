import { computed, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { AlertService } from '../app/alert/alert.service';
import { shownByCaller } from '../app/alert/error-alert.interceptor';
import { withIdempotencyKey } from '../app/auth/credentials.interceptor';
import type { Api, RequestOf } from '../core/api';
import type { CloseApproval } from '../core/close-decision';
import type { FormDataDto, FormModel, FormRequest, FormServiceEndpoints, MessageDto, MutationOutcome } from '../core/generated/contracts';
import type { Lifetime } from '../core/lifetime';
import { newUuid } from '../core/uuid';
import type { ConfirmationService } from '../overlay/confirmation.service';
import type { OverlayRef } from '../overlay/overlay-ref';
import { parentParams, type ScreenAddress, type ScreenHost } from '../screen/screen-address';
import type { ScreenMutations, ScreenOperation } from '../screen/screen-mutations';
import { frozenCopy, isFormData, stableJson, stripUntouchedSecrets } from './form-value';

/** A create whose outcome is open: its idempotency key and the submission posted under it. */
export interface KeptCreate {
  readonly key: string;
  readonly submission: RequestOf<FormServiceEndpoints, 'save'>;
}

export type SaveMode = 'stay' | 'close' | 'act' | { approval: CloseApproval };

/**
 * A save asked for by a close hands the close its result, the operation that caused it and the release of that
 * operation; the approval holds while the value saved is the value shown and nothing is busy.
 */
export function handOffCloseApproval(
  approval: CloseApproval,
  saved: FormDataDto,
  operation: ScreenOperation | undefined,
  currentValue: () => Record<string, unknown>,
  isBusy: () => boolean,
  isCurrent: () => boolean,
  release: () => void,
): void {
  const value = currentValue();
  approval.result = saved;
  approval.cause = operation?.id;
  approval.isCurrent = () => isCurrent() && currentValue() === value && !isBusy();
  approval.release = release;
}

/** Asks the user whether to replace the held form draft with the server's record. */
export function confirmDraftReplacement(confirmation: ConfirmationService, model: FormModel | null): Promise<boolean> {
  if (!model) {
    return Promise.resolve(false);
  }
  return confirmation.confirm({
    title: model.dirtyDialog.title,
    message: model.dirtyDialog.description,
    confirmLabel: model.mutationTexts.reloadLabel,
    cancelLabel: model.dirtyDialog.continueLabel,
  });
}

export interface FormSaveRunnerOptions {
  readonly api: Api;
  readonly alert: AlertService;
  readonly confirmation: ConfirmationService;
  readonly ref: OverlayRef | null;
  readonly host: ScreenHost | null;
  readonly mutations: ScreenMutations | null;
  readonly lifetime: Lifetime;
  readonly address: () => ScreenAddress | null | undefined;
  readonly service: () => string | null;
  readonly model: () => FormModel | null;
  readonly id: () => number | null;
  readonly ver: () => number | null;
  readonly value: () => Record<string, unknown>;
  readonly outcome: () => MutationOutcome | null;
  readonly canSave: () => boolean;
  readonly isBusy: () => boolean;
  readonly capture: () => () => boolean;
  readonly refuseParseErrors: () => readonly MessageDto[];
  readonly setOutcome: (outcome: MutationOutcome | null) => void;
  readonly adopt: (data: FormDataDto) => void;
  readonly bumpDataRevision: () => void;
  readonly setReading: (method: string | null) => void;
  readonly readInPlace: (service: string, id: number, marksStale: boolean) => Promise<void>;
  readonly beginOperation: () => ScreenOperation | undefined;
  readonly releaseOperation: (operation: ScreenOperation | undefined) => void;
  readonly reportFailure: (error: unknown) => void;
}

/**
 * Owns the form's save lifecycle, create-idempotency retention (`KeptCreate`), close-approval handoff, and uncertain
 * create recovery for `FormService`.
 */
export class FormSaveRunner {
  private readonly _isSaving = signal(false);
  readonly isSaving = this._isSaving.asReadonly();

  private readonly keptCreate = signal<KeptCreate | null>(null);
  readonly hasKeptCreate = computed(() => this.keptCreate() !== null);

  private closingResult: FormDataDto | undefined;

  constructor(private readonly options: FormSaveRunnerOptions) {}

  clearKeptCreate(): void {
    this.keptCreate.set(null);
  }

  resetInvalidation(): void {
    this._isSaving.set(false);
  }

  acceptsClose(result: unknown, isBusy: boolean, awaitsScreen: boolean): boolean {
    return !isBusy || awaitsScreen || (!!this.closingResult && result === this.closingResult);
  }

  async performSave(mode: SaveMode): Promise<FormDataDto | null> {
    const isCurrent = this.options.capture();
    const service = this.options.service();
    if (!this.options.canSave() || !isCurrent() || !service) {
      return null;
    }
    if (this.options.refuseParseErrors().length) {
      this.options.setOutcome('refused');
      return null;
    }
    const wasNew = this.options.id() === null;
    const version = this.options.lifetime.epoch();
    const operation = this.options.beginOperation();
    const submitted = this.buildSubmission();
    const key = wasNew ? (this.keptCreate()?.key ?? newUuid()) : null;
    if (key !== null) {
      this.keptCreate.set({ key, submission: submitted });
    }
    this._isSaving.set(true);
    let isHandedOff = false;
    try {
      const httpOptions = key === null ? shownByCaller() : withIdempotencyKey(key);
      const answer = await firstValueFrom(this.options.api.postTo<FormServiceEndpoints, 'save'>(service, 'save', submitted, httpOptions));
      if (!isCurrent()) {
        return null;
      }
      if (answer?.outcome !== 'committed' || !isFormData(answer)) {
        this.options.reportFailure(answer);
        this.settleCreate();
        return null;
      }
      const saved = this.adoptCommit(answer, service, operation, wasNew && mode === 'stay');
      if (mode === 'stay' && !wasNew && this.options.host && saved.id != null) {
        void this.options.readInPlace(service, saved.id, false);
      }
      if (mode === 'close' && isCurrent()) {
        this.closingResult = saved;
        await this.options.ref?.close(saved, operation?.id);
      } else if (typeof mode === 'object') {
        handOffCloseApproval(
          mode.approval,
          saved,
          operation,
          () => this.options.value(),
          () => this.options.isBusy(),
          isCurrent,
          () => this.options.releaseOperation(operation),
        );
        isHandedOff = true;
      }
      return saved;
    } catch (error: unknown) {
      if (isCurrent()) {
        this.options.reportFailure(error);
        this.settleCreate();
      }
      return null;
    } finally {
      if (!isHandedOff) {
        this.options.releaseOperation(operation);
      }
      if (!this.options.lifetime.isEnded && this.options.lifetime.epoch() === version) {
        this.closingResult = undefined;
        this._isSaving.set(false);
      }
    }
  }

  async recoverCreate(service: string, isCurrent: () => boolean, setRecovering: (recovering: boolean) => void): Promise<void> {
    const kept = this.keptCreate();
    if (!kept) {
      return;
    }
    setRecovering(true);
    let operation: ScreenOperation | undefined;
    try {
      if (
        stableJson(this.buildSubmission()) !== stableJson(kept.submission) &&
        !(await confirmDraftReplacement(this.options.confirmation, this.options.model()))
      ) {
        return;
      }
      if (!isCurrent()) {
        return;
      }
      operation = this.options.beginOperation();
      const answer = await firstValueFrom(
        this.options.api.postTo<FormServiceEndpoints, 'save'>(service, 'save', kept.submission, withIdempotencyKey(kept.key)),
      );
      if (!isCurrent()) {
        return;
      }
      if (answer?.outcome !== 'committed' || !isFormData(answer)) {
        this.options.reportFailure(answer);
        this.settleCreate();
        return;
      }
      this.adoptCommit(answer, service, operation, true);
    } catch (error: unknown) {
      if (isCurrent()) {
        this.options.reportFailure(error);
        this.settleCreate();
      }
    } finally {
      this.options.releaseOperation(operation);
      if (isCurrent()) {
        setRecovering(false);
      }
    }
  }

  private settleCreate(): void {
    if (this.options.outcome() !== 'unknown') {
      this.keptCreate.set(null);
    }
  }

  private adoptCommit(answer: FormDataDto, service: string, operation: ScreenOperation | undefined, reloads: boolean): FormDataDto {
    this.options.adopt(answer);
    this.options.bumpDataRevision();
    this.options.alert.warnings(answer.warnings, this.options.model()?.mutationTexts.committedWarningTitle);
    if (reloads && this.options.host) {
      this.options.setReading(null);
      this.options.host.reload({ id: answer.id });
    }
    const saved = { ...answer, value: this.options.value() };
    if (operation) {
      this.options.mutations?.publish(operation, {
        service,
        recordId: saved.id,
        data: saved,
        affected: answer.affected ?? undefined,
        operation: 'save',
      });
    }
    return saved;
  }

  private buildSubmission(): FormRequest {
    const service = this.options.service();
    const value = stripUntouchedSecrets(this.options.value(), this.options.model()?.elements ?? []);
    return frozenCopy({ ...parentParams(this.options.address(), service), id: this.options.id(), ver: this.options.ver(), value });
  }
}
