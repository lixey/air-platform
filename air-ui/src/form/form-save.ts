import type { CloseApproval } from '../core/close-decision';
import type { FormDataDto, FormModel } from '../core/generated/contracts';
import type { ConfirmationService } from '../overlay/confirmation.service';
import type { ScreenOperation } from '../screen/screen-mutations';

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
