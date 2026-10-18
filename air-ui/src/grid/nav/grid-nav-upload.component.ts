import { Component, DestroyRef, inject, Injector, model } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { GridNavUploadModel } from '../../core/generated/contracts';
import { uploadOverlayOf } from '../../file/file-upload-overlay.component';
import type { ShownFile } from '../../file/file-upload-queue';
import { OverlayService } from '../../overlay/overlay.service';
import { keyOf, type GridRow } from '../grid-row';
import { GridService } from '../grid.service';
import { isOwnerCurrent } from './grid-nav-mutation';

/**
 * The file grid's `Upload`: a button that opens the upload dialog (`FileUploadOverlay`) over the grid's service and its
 * parent, `parentService` and `parentId`, within the model's `limits` (`FileUploadLimitsDto`: the limits, the image
 * rules and the server's bounds), with the rows the page shows, whose count and sizes take the record's room (the page
 * holds all of a record's files, `maxFiles` being at most its size). The dialog is opened with this element's injector,
 * so the screen owns it; when it closes the grid reloads its page, while it still shows what it showed at the opening.
 * The grid it stands in knows the service, so the model carries the button's look and the limits alone; outside a grid
 * there is nothing to upload to, and it draws nothing.
 */
@Component({
  selector: 'ui-grid-nav-upload',
  imports: [BtnComponent],
  template: `
    @if (grid) {
      <ui-btn [model]="model()" aria-haspopup="dialog" (click)="open()" />
    }
  `,
})
export class GridNavUploadComponent {
  protected readonly grid = inject(GridService, { optional: true });
  private readonly overlay = inject(OverlayService);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  readonly model = model<GridNavUploadModel | null>();

  protected open(): void {
    const upload = this.model();
    const grid = this.grid;
    const service = grid?.service();
    if (!upload || !grid || !service) {
      return;
    }

    const isCurrent = isOwnerCurrent(grid, this.destroyRef);
    const { parentService, parentId } = grid.request();
    const dialog = uploadOverlayOf({
      ...upload.limits,
      service,
      parentService,
      parentId,
      shownFiles: shownFilesOf(grid.rows()),
      committedWarningTitle: grid.model()?.mutationTexts.committedWarningTitle,
    });
    this.overlay.open(dialog, null, this.injector).closed.subscribe(() => {
      if (isCurrent()) {
        grid.reload();
      }
    });
  }
}

/** The rows of the page as the files that take the record's room: each row with a key, and its `size` in bytes. */
function shownFilesOf(rows: readonly GridRow[]): ShownFile[] {
  return rows.flatMap(row => {
    const id = keyOf(row);
    const size = row['size'];
    return id === null ? [] : [{ id, size: typeof size === 'number' ? size : 0 }];
  });
}
