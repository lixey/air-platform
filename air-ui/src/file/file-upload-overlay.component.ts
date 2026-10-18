import { Component, computed, DestroyRef, type ElementRef, inject, input, viewChild } from '@angular/core';
import { LocaleService } from '../app/locale.service';
import { BtnComponent } from '../btn/btn.component';
import type { OverlayModelDto } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { OverlayRef } from '../overlay/overlay-ref';
import { singlePanelLayout } from '../panels/single-panel-layout';
import type { FileUploadOverlayModel } from '../ui-components';
import { formatBytes } from './file-size';
import { FileTileComponent } from './file-tile.component';
import { acceptedNamesOf, pickerAcceptOf } from './file-upload-call';
import { FileUploadQueue } from './file-upload-queue';
import { fileViewOfUpload } from './file-view';
import { FileZoneDirective } from './file-zone.directive';

/** The key of the upload dialog's layout, and the stem of its overlay's key. */
const FILE_UPLOAD_OVERLAY = 'file.upload';

/** The drop zone: a dashed field, whose edge takes the hover ring while files are dragged over it. */
const DROP_ZONE = 'flex flex-col items-center gap-2 p-4 rounded-md border-2 border-dashed bg-field text-center';

/**
 * The upload dialog of a file grid, opened by the grid's `Upload`. It takes several files at once, chosen in the
 * browser's picker, dropped on its zone or pasted while it holds focus, and shows the upload's limits before any pick.
 * Its files go through the upload queue it provides (`FileUploadQueue`), which refuses a file beyond the limits, or
 * beyond the room the record has left by the files the grid showed at the opening, with its reason, and posts the others
 * one at a time, each drawn as a line of the list look with its status, its progress, and Cancel, Try again or Remove
 * where they apply. The dialog does not close while a file is waiting or posting; whoever opened it reloads what it
 * shows once it closes.
 */
@Component({
  selector: 'ui-file-upload-overlay',
  imports: [BtnComponent, FileTileComponent, FileZoneDirective],
  providers: [FileUploadQueue],
  template: `
    <header class="flex items-center gap-(--ui-panel-gap) px-3 py-2 border-b border-line">
      <h2 [id]="titleId" class="m-0 text-base font-semibold">{{ texts.of('UploadFiles') }}</h2>
    </header>

    <div class="flex flex-col gap-3 p-3" [uiFileZone]="model() ?? null" (uiFileZoneTaken)="take($event)" #zone="uiFileZone">
      <div [class]="zone.isDragging() ? dropZoneDragging : dropZone" data-drop-zone>
        <p class="m-0 text-sm text-ink">{{ texts.of('FilesZoneHint') }}</p>
        <ui-btn [label]="texts.of('ChooseFiles')" icon="import" color="orange" [attr.aria-describedby]="hintId" (click)="choose()" />
        <p [id]="hintId" class="m-0 text-xs text-muted" data-files-limits>{{ limits() }}</p>
      </div>
      <input
        #picker
        type="file"
        multiple
        class="hidden"
        tabindex="-1"
        aria-hidden="true"
        [accept]="accept()"
        (change)="takePicked(picker)"
      />

      @if (rows().length) {
        <ul role="list" class="m-0 p-0 list-none flex flex-col divide-y divide-line max-h-72 overflow-y-auto">
          @for (row of rows(); track row.upload.id) {
            <li data-upload-item [attr.data-status]="row.upload.status">
              <ui-file-tile
                look="list"
                [file]="row.file"
                [upload]="row.upload"
                cancelable
                (retryRequested)="queue.retry(row.upload.id)"
                (removeRequested)="queue.remove(row.upload.id)"
                (cancelRequested)="queue.cancel(row.upload.id)"
              />
            </li>
          }
        </ul>
      }
    </div>

    <footer class="flex justify-end gap-(--ui-panel-gap) px-3 py-2 border-t border-line">
      <ui-btn [label]="texts.of('Close')" color="slate" [loading]="queue.isPosting()" (click)="close()" />
    </footer>
  `,
  host: { class: 'flex flex-col w-md max-w-full' },
})
export class FileUploadOverlayComponent {
  private static next = 0;

  protected readonly texts = inject(TextsService);
  protected readonly queue = inject(FileUploadQueue);
  private readonly locale = inject(LocaleService).current;
  private readonly ref = inject(OverlayRef, { optional: true });
  private readonly picker = viewChild<ElementRef<HTMLInputElement>>('picker');

  readonly model = input<FileUploadOverlayModel | null>();

  /** What the picker offers: the content types the grid accepts and their extensions. */
  protected readonly accept = computed(() => {
    const model = this.model();
    return model ? pickerAcceptOf(model) : '';
  });

  /** What the grid takes, under the zone: the accepted files by extension and the largest size. */
  protected readonly limits = computed(() => {
    const model = this.model();
    return model ? this.texts.of('FileLimits', acceptedNamesOf(model), formatBytes(model.maxBytes, this.locale())) : '';
  });

  protected readonly rows = computed(() => this.queue.uploads().map(upload => ({ upload, file: fileViewOfUpload(upload.file) })));

  protected readonly dropZone = `${DROP_ZONE} border-field-edge`;
  protected readonly dropZoneDragging = `${DROP_ZONE} border-field-hover`;
  private readonly uid = `ui-file-upload-${++FileUploadOverlayComponent.next}`;
  protected readonly titleId = `${this.uid}-title`;
  protected readonly hintId = `${this.uid}-hint`;

  constructor() {
    this.ref?.name({ labelledBy: this.titleId });
    const unregister = this.ref?.registerCloseParticipant(() => !this.queue.isPosting());
    const unregisterShown = this.queue.registerShownFiles(() => this.model()?.shownFiles ?? []);
    inject(DestroyRef).onDestroy(() => {
      unregister?.();
      unregisterShown();
    });
  }

  /** Opens the browser's picker, which takes several files at once. */
  protected choose(): void {
    this.picker()?.nativeElement.click();
  }

  protected takePicked(input: HTMLInputElement): void {
    const files = [...(input.files ?? [])];
    // Emptied, so picking the same files again is a change again
    input.value = '';
    this.take(files);
  }

  protected take(files: readonly File[]): void {
    const model = this.model();
    if (model) {
      this.queue.add(files, model);
    }
  }

  protected close(): void {
    void this.ref?.dismiss();
  }
}

/**
 * The upload dialog over `target`, a file grid and the parent its files go under: a modal card that a click beside it
 * leaves open, so the statuses of the files stay in view until the user closes it.
 */
export function uploadOverlayOf(target: Omit<FileUploadOverlayModel, 'kind'>): OverlayModelDto {
  return {
    key: `${FILE_UPLOAD_OVERLAY}.${target.service}`,
    presentation: 'dialog',
    closesOnOutside: false,
    layout: singlePanelLayout(FILE_UPLOAD_OVERLAY, { ...target, kind: 'FileUploadOverlay' }),
  };
}
