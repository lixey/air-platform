import { DestroyRef, ErrorHandler, inject, Injector, Service } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { CloseApproval, CloseAttempt } from '../core/close-decision';
import { CloseDecision } from '../core/close-decision';
import type { FormDataDto, FormModel } from '../core/generated/contracts';
import { DirtyDialogService } from '../overlay/dirty-dialog.service';
import type { DirtyChoice } from '../overlay/overlay-dirty.component';
import { OverlayRef } from '../overlay/overlay-ref';
import { ScreenLifecycle } from '../screen/screen-lifecycle';
import { FormService } from './form.service';

/**
 * Arbitrates form closure, replacement and dirty draft prompt for a FormService.
 */
@Service({ autoProvided: false })
export class FormCloseGuard {
  private readonly form = inject(FormService);
  private readonly ref = inject(OverlayRef, { optional: true });
  private readonly lifecycle = inject(ScreenLifecycle, { optional: true });
  private readonly dirtyDialog = inject(DirtyDialogService);
  private readonly injector = inject(Injector);
  private readonly errorHandler = inject(ErrorHandler);
  private prompt: OverlayRef<DirtyChoice> | undefined;
  private replacement: CloseDecision | undefined;

  constructor() {
    const unbind = this.form.onInvalidate(() => this.dismissPrompt());
    const unregister = this.ref?.registerCloseParticipant(request => this.approveClose(request));
    const unregisterOwner = this.lifecycle?.registerOwner({
      closeParticipant: this.ref ? undefined : request => this.approveClose(request),
      needsProtection: () => this.form.needsProtection(),
      reset: () => {
        this.reset();
        this.form.reset();
      },
    });
    inject(DestroyRef).onDestroy(() => {
      unbind();
      unregister?.();
      unregisterOwner?.();
      this.reset();
    });
  }

  reset(): void {
    this.replacement?.cancel();
    this.replacement = undefined;
    this.dismissPrompt();
  }

  /** Keep the displayed record until every affected owner agrees to its replacement. */
  replace(model: FormModel | null, data: FormDataDto | null): void {
    this.replacement?.cancel();
    if (!this.form.model() || !this.form.needsProtection()) {
      this.form.setModel(model);
      this.form.setData(data);
      return;
    }
    const isCurrent = this.form.capture();
    const attempt = new CloseDecision({
      participants: [request => this.approveClose(request)],
      isOwnerCurrent: isCurrent,
      onCommit: () => {
        this.form.setModel(model);
        this.form.setData(data);
      },
      errorHandler: this.errorHandler,
    });
    this.replacement = attempt;
    attempt.ready.then(ready => {
      if (ready && this.replacement === attempt) {
        attempt.commit();
      }
      if (this.replacement === attempt) {
        this.replacement = undefined;
      }
    });
  }

  private async approveClose(request: CloseAttempt): Promise<boolean | CloseApproval> {
    if (request.signal?.aborted) {
      return false;
    }
    const isCurrent = this.form.capture();
    const value = this.form.value();
    const isApprovalCurrent = () => isCurrent() && this.form.value() === value && this.form.acceptsClose(request.result);
    if (!isApprovalCurrent()) {
      return false;
    }
    if (!this.form.isDirty()) {
      return { isCurrent: isApprovalCurrent };
    }
    const model = this.form.model();
    if (!model) {
      return false;
    }
    const prompt = this.dirtyDialog.open(model.dirtyDialog, this.injector);
    this.prompt = prompt;
    const abort = () => prompt.forceClose();
    request.signal?.addEventListener('abort', abort, { once: true });
    let choice: DirtyChoice | undefined;
    try {
      choice = await firstValueFrom(prompt.closed);
    } finally {
      request.signal?.removeEventListener('abort', abort);
    }
    if (this.prompt === prompt) {
      this.prompt = undefined;
    }
    if (request.signal?.aborted || !isCurrent() || this.form.isBusy()) {
      return false;
    }
    if (choice === 'discard') {
      return { isCurrent: isApprovalCurrent };
    }
    if (choice === 'save') {
      const approval: CloseApproval = {};
      return (await this.form.saveForClose(approval)) ? approval : false;
    }
    return false;
  }

  private dismissPrompt(): void {
    this.prompt?.dismiss();
    this.prompt = undefined;
  }
}
