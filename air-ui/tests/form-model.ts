import type { FormElementDto, FormModel, MutationTextsDto, OverlayDirtyModel } from '../src/core/generated/contracts';

const DEFAULT_DIRTY_DIALOG: OverlayDirtyModel = {
  kind: 'OverlayDirty',
  title: 'Unsaved changes',
  description: 'You have unsaved changes.',
  saveLabel: 'Save',
  discardLabel: 'Discard',
  continueLabel: 'Keep editing',
};

const DEFAULT_MUTATION_TEXTS: MutationTextsDto = {
  conflictTitle: 'The record changed',
  reloadLabel: 'Reload',
  unknownTitle: 'Outcome unknown',
  unknownDescription: 'Verify before retrying.',
  verifyLabel: 'Verify',
  committedWarningTitle: 'Saved with warnings',
  selectionCorrectionLabel: 'Selection adjusted',
};

export function formElement(path: string, extra: Partial<FormElementDto> = {}): FormElementDto {
  return {
    path,
    label: path,
    type: 'text',
    model: { kind: 'FormInput' },
    span: 12,
    isRequired: false,
    constraints: {},
    isDisabled: false,
    isHidden: false,
    isServerOwned: false,
    isInputOnly: false,
    valueKind: 'scalar',
    ...extra,
  };
}

export function formModel(elements: FormElementDto[] = [], extra: Partial<FormModel> = {}): FormModel {
  return {
    kind: 'Form',
    service: 'sample-form',
    elements,
    isReadOnly: false,
    dirtyDialog: DEFAULT_DIRTY_DIALOG,
    mutationTexts: DEFAULT_MUTATION_TEXTS,
    ...extra,
  };
}
