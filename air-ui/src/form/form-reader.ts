import { signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { AlertService } from '../app/alert/alert.service';
import { shownByCaller } from '../app/alert/error-alert.interceptor';
import type { Api } from '../core/api';
import type { FormDataDto, FormModel, FormServiceEndpoints, ScreenDto } from '../core/generated/contracts';
import type { ConfirmationService } from '../overlay/confirmation.service';
import { parentParams, type ScreenAddress, type ScreenHost } from '../screen/screen-address';
import type { ScreenLifecycle } from '../screen/screen-lifecycle';
import { reportFormReadFailure } from './form-failure';
import { confirmDraftReplacement } from './form-save';
import { isFormData } from './form-value';

export interface FormReaderOptions {
  readonly api: Api;
  readonly alert: AlertService;
  readonly confirmation: ConfirmationService;
  readonly host: ScreenHost | null;
  readonly lifecycle: ScreenLifecycle | null;
  readonly address: () => ScreenAddress | null | undefined;
  readonly service: () => string | null;
  readonly model: () => FormModel | null;
  readonly id: () => number | null;
  readonly adoption: () => number;
  readonly isDirty: () => boolean;
  readonly isBusy: () => boolean;
  readonly isIdle: () => boolean;
  readonly isUnsettled: () => boolean;
  readonly capture: () => () => boolean;
  readonly adopt: (data: FormDataDto) => void;
  readonly bumpDataRevision: () => void;
  readonly setReading: (method: string | null) => void;
  readonly clearActing: () => void;
  readonly putBack: () => void;
}

/**
 * Owns in-place record reads, stale-state detection and reload, action post-commit reloading, and record conflict/unknown
 * recovery for `FormService`.
 */
export class FormReader {
  private readonly _isRecovering = signal(false);
  readonly isRecovering = this._isRecovering.asReadonly();

  private readonly _isStale = signal(false);
  readonly isStale = this._isStale.asReadonly();

  private reads = 0;

  constructor(private readonly options: FormReaderOptions) {}

  setRecovering(recovering: boolean): void {
    this._isRecovering.set(recovering);
  }

  clearStale(): void {
    this._isStale.set(false);
  }

  resetInvalidation(): void {
    this._isRecovering.set(false);
  }

  read(service: string, id: number | null): Promise<ScreenDto | null> {
    return firstValueFrom(
      this.options.api.postTo<FormServiceEndpoints, 'get-data'>(
        service,
        'get-data',
        { ...parentParams(this.options.address(), service), id },
        shownByCaller(),
      ),
    );
  }

  async readInPlace(service: string, id: number, marksStale: boolean): Promise<void> {
    const isCurrent = this.options.capture();
    const read = ++this.reads;
    const adoption = this.options.adoption();
    const isAwaited = () => isCurrent() && read === this.reads && adoption === this.options.adoption();
    let screen: ScreenDto | null;
    try {
      screen = await this.read(service, id);
    } catch {
      if (marksStale && isAwaited() && !this.options.isUnsettled()) {
        this._isStale.set(true);
      }
      return;
    }
    const data = screen?.data;
    if (!isAwaited() || !isFormData(data) || data.id !== id || this.options.isUnsettled()) {
      return;
    }
    if (this.options.isDirty() || !this.options.isIdle()) {
      if (marksStale) {
        this._isStale.set(true);
      }
      return;
    }
    this.take(screen, data);
  }

  readAgain(service: string, id: number, method: string | null, isCurrent: () => boolean): void {
    this.options.bumpDataRevision();
    if (this.options.host) {
      this.options.setReading(method);
      this.options.host.reload({ id });
    } else {
      void this.readRecord(service, id, isCurrent);
    }
  }

  refresh(): void {
    const service = this.options.service();
    const id = this.options.id();
    if (!service || id === null || this.options.isUnsettled()) {
      return;
    }
    if (this.options.isDirty() || !this.options.isIdle()) {
      this._isStale.set(true);
      return;
    }
    void this.readInPlace(service, id, true);
  }

  async reload(): Promise<void> {
    const isCurrent = this.options.capture();
    const service = this.options.service();
    const id = this.options.id();
    if (!this.isStale() || !service || id === null || this.options.isBusy() || this.options.lifecycle?.isLocked() || !isCurrent()) {
      return;
    }
    this._isRecovering.set(true);
    try {
      if (this.options.isDirty() && !(await confirmDraftReplacement(this.options.confirmation, this.options.model()))) {
        return;
      }
      if (!isCurrent()) {
        return;
      }
      const read = ++this.reads;
      const screen = await this.read(service, id);
      const data = screen?.data;
      if (isCurrent() && read === this.reads && isFormData(data) && data.id === id) {
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

  async recoverRecord(service: string, id: number, uncertain: boolean, isCurrent: () => boolean): Promise<void> {
    this._isRecovering.set(true);
    try {
      if (!uncertain && !(await confirmDraftReplacement(this.options.confirmation, this.options.model()))) {
        return;
      }
      if (!isCurrent()) {
        return;
      }
      const data = (await this.read(service, id))?.data;
      if (!isCurrent() || !isFormData(data) || data.id !== id) {
        return;
      }
      if (uncertain && !(await confirmDraftReplacement(this.options.confirmation, this.options.model()))) {
        return;
      }
      if (isCurrent()) {
        this.options.adopt(data);
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

  private take(screen: ScreenDto | null, data: FormDataDto): void {
    this.options.adopt(data);
    if (screen) {
      this.options.host?.show(screen);
    }
  }

  private async readRecord(service: string, id: number, isCurrent: () => boolean): Promise<void> {
    try {
      const data = (await this.read(service, id))?.data;
      if (isCurrent() && isFormData(data) && data.id === id) {
        this.options.adopt(data);
        this.options.putBack();
      }
    } catch (error: unknown) {
      if (isCurrent()) {
        this.reportReadFailure(error);
      }
    } finally {
      if (isCurrent()) {
        this.options.clearActing();
      }
    }
  }

  private reportReadFailure(error: unknown): void {
    reportFormReadFailure(error, this.options.alert, this.options.model()?.mutationTexts.unknownTitle);
  }
}
