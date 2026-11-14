import type { OverlayDto } from '../core/generated/contracts';
import { singlePanelLayout } from '../panels/single-panel-layout';
import type { FileUploadOverlayModel } from '../ui-components';

/** The key of the upload dialog's layout, and the stem of its overlay's key. */
export const FILE_UPLOAD_OVERLAY = 'file.upload';

/**
 * The upload dialog over `target`, a file grid and the parent its files go under: a modal card that a click beside it
 * leaves open, so the statuses of the files stay in view until the user closes it.
 */
export function uploadOverlayOf(target: Omit<FileUploadOverlayModel, 'kind'>): OverlayDto {
  return {
    key: `${FILE_UPLOAD_OVERLAY}.${target.service}`,
    presentation: 'dialog',
    closesOnOutside: false,
    layout: singlePanelLayout(FILE_UPLOAD_OVERLAY, { ...target, kind: 'FileUploadOverlay' }),
  };
}
