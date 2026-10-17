import { inject, Service } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { OverlayConfirmModel } from '../core/generated/contracts';
import { singlePanelLayout } from '../panels/single-panel-layout';
import { OverlayService } from './overlay.service';

/** Asks a question in a dialog and answers with a boolean. */
@Service()
export class ConfirmationService {
  private readonly overlay = inject(OverlayService);

  /**
   * Opens the confirmation and answers whether the user confirmed. A `signal` that aborts closes it unanswered, as a
   * close attempt that asked it and was given up does; an aborted one opens nothing.
   */
  async confirm(messageOrOptions: string | Omit<OverlayConfirmModel, 'kind'>, signal?: AbortSignal): Promise<boolean> {
    if (signal?.aborted) {
      return false;
    }
    const options = typeof messageOrOptions === 'string' ? { message: messageOrOptions } : messageOrOptions;
    const model: OverlayConfirmModel = { ...options, kind: 'OverlayConfirm' };

    const ref = this.overlay.open<boolean>({
      key: 'overlay.confirm',
      presentation: 'dialog',
      layout: singlePanelLayout('overlay.confirm', model),
    });
    const abort = () => ref.forceClose();
    signal?.addEventListener('abort', abort, { once: true });
    try {
      return (await firstValueFrom(ref.closed)) === true;
    } finally {
      signal?.removeEventListener('abort', abort);
    }
  }
}
