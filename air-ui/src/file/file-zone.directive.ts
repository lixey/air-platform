import { Directive, ElementRef, inject, input, output, signal } from '@angular/core';
import { LocaleService } from '../app/locale.service';
import { pastedFilesOf, type UploadAccept } from './file-upload-call';

/** The type a drag started on the page carries: a drag from outside it never does. */
const PAGE_DRAG = 'application/x-ui-page-drag';

/** Whether the drag started on the page: its types, readable while it goes, name the mark. */
function isPageDrag(event: DragEvent): boolean {
  return [...(event.dataTransfer?.types ?? [])].includes(PAGE_DRAG);
}

/**
 * Makes the host the zone files enter by: files dragged over it may be dropped there rather than opened by the browser,
 * and files pasted while the host or anything in it holds focus are taken, each of an accepted type named by the
 * moment of the paste on the user's clock (`pastedFilesOf`). A paste without files, text among them, is left to the browser. The host says
 * nothing about how it looks: it reads `isDragging` to show that files are over it. Without limits the zone takes
 * nothing.
 *
 * A drag that starts on the page, a file's picture or its download link among them, is marked as the page's own and
 * never dropped on the zone: the browser would hand it over as a file, and the zone would upload the file again.
 */
@Directive({
  selector: '[uiFileZone]',
  exportAs: 'uiFileZone',
  host: {
    '(document:dragstart)': 'onPageDragStart($event)',
    '(dragover)': 'onDragOver($event)',
    '(dragleave)': 'onDragLeave($event)',
    '(drop)': 'onDrop($event)',
    '(paste)': 'onPaste($event)',
  },
})
export class FileZoneDirective {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly timeZone = inject(LocaleService).zone;

  /** The limits of the upload the files go to, which name a pasted file; null while the zone takes nothing. */
  readonly uiFileZone = input<UploadAccept | null>(null);

  /** The files dropped or pasted, in the order the browser gave them. */
  readonly uiFileZoneTaken = output<File[]>();

  private readonly _isDragging = signal(false);
  /** Whether files are dragged over the zone now. */
  readonly isDragging = this._isDragging.asReadonly();

  /** Marks a drag that starts on the page, so no zone takes it. */
  protected onPageDragStart(event: DragEvent): void {
    event.dataTransfer?.setData(PAGE_DRAG, '');
  }

  /** Files from outside the page may be dropped here: the drag is accepted and the zone shows it. */
  protected onDragOver(event: DragEvent): void {
    if (!this.uiFileZone() || isPageDrag(event)) {
      return;
    }
    event.preventDefault();
    this._isDragging.set(true);
  }

  /** The pointer left the zone, not merely moved onto something inside it. */
  protected onDragLeave(event: DragEvent): void {
    const into = event.relatedTarget;
    if (!(into instanceof Node) || !this.host.contains(into)) {
      this._isDragging.set(false);
    }
  }

  protected onDrop(event: DragEvent): void {
    if (!this.uiFileZone() || isPageDrag(event)) {
      return;
    }
    event.preventDefault();
    this._isDragging.set(false);
    const files = [...(event.dataTransfer?.files ?? [])];
    if (files.length) {
      this.uiFileZoneTaken.emit(files);
    }
  }

  protected onPaste(event: ClipboardEvent): void {
    const limits = this.uiFileZone();
    const files = [...(event.clipboardData?.files ?? [])];
    if (!limits || files.length === 0) {
      return;
    }
    event.preventDefault();
    this.uiFileZoneTaken.emit(pastedFilesOf(files, limits, new Date(), this.timeZone()));
  }
}
