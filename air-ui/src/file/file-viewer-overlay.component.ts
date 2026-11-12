import {
  afterNextRender,
  Component,
  computed,
  DestroyRef,
  type ElementRef,
  inject,
  Injector,
  input,
  signal,
  viewChildren,
} from '@angular/core';
import { LocaleService } from '../app/locale.service';
import { BtnComponent, SLATE_GHOST_ICON_BTN_CLASSES, SLATE_SOFT_BTN_CLASSES } from '../btn/btn.component';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { OverlayRef } from '../overlay/overlay-ref';
import type { FileViewerOverlayModel } from '../ui-components';
import { FileInfoPanelComponent } from './file-info-panel.component';
import { formatBytes } from './file-size';
import { badgeOf, downloadLinkOf, type FileView, isInlineImage, isInlineVideo, pictureOf } from './file-view';
import { FILE_VIEWER_SOURCE } from './file-viewer';
import { FileViewerLinks } from './file-viewer-links';
import { FileViewerSequence, isInInfo, isInStrip, isViewerMoveKey, THUMB_CLASSES } from './file-viewer-sequence';

/**
 * The file viewer (`FileViewerOverlay`), a dialog over the whole window opened at one file over a sequence of files its
 * opener gives it (`FILE_VIEWER_SOURCE`): a files element's files, the page's pictures of a file grid, a record's files
 * from its cover.
 */
@Component({
  selector: 'ui-file-viewer-overlay',
  imports: [BtnComponent, FileInfoPanelComponent, UiIconDirective],
  template: `
    <header class="flex items-center gap-2 px-3 py-2 border-b border-line">
      <div class="flex flex-col flex-1 min-w-0">
        <h2 class="m-0 truncate text-base font-semibold text-ink" [title]="current()?.name ?? ''" data-file-name>
          {{ current()?.name }}
        </h2>
        <span class="text-xs text-muted" data-file-size>{{ size() }}</span>
      </div>
      <div class="flex shrink-0 items-center gap-(--ui-panel-gap)" data-file-bar>
        <p class="m-0 shrink-0 text-sm text-muted tabular-nums" role="status" aria-live="polite" aria-atomic="true" data-file-counter>
          {{ counter() }}
        </p>
        @if (current(); as file) {
          @if (isImage()) {
            <div class="flex shrink-0 items-center gap-(--ui-panel-gap)">
              <ui-btn
                icon="fullscreen-exit"
                variant="ghost"
                color="slate"
                [title]="texts.of('FitToScreen')"
                [selected]="!isActualSize()"
                (click)="isActualSize.set(false)"
                data-file-fit
              />
              <ui-btn
                icon="fullscreen"
                variant="ghost"
                color="slate"
                [title]="texts.of('ActualSize')"
                [selected]="isActualSize()"
                (click)="isActualSize.set(true)"
                data-file-actual
              />
            </div>
          }
          @if (file.url; as url) {
            <a
              [class]="linkIconButton"
              [href]="downloadLinkOf(url)"
              [attr.download]="file.name"
              [attr.aria-label]="texts.of('Download')"
              [title]="texts.of('Download')"
              data-file-download
            >
              <i icon="download"></i>
            </a>
          }
          @if (canDelete()) {
            <ui-btn
              icon="trash"
              variant="ghost"
              color="slate"
              [title]="texts.of('DeleteFile', file.name)"
              [loading]="isDeleting()"
              (click)="remove()"
              data-file-delete
            />
          }
        }
        @if (current()) {
          <ui-btn
            icon="info"
            variant="ghost"
            color="slate"
            [title]="texts.of('FileInfo')"
            [selected]="isInfoOpen()"
            (click)="isInfoOpen.set(!isInfoOpen())"
            data-file-info
          />
        }
        <ui-btn icon="close" variant="ghost" color="slate" [title]="texts.of('Close')" (click)="close()" data-file-close />
      </div>
    </header>

    <div class="flex flex-col flex-1 min-h-0 min-w-0 @4xl/panel:flex-row">
      <div class="relative flex flex-1 min-h-0 min-w-0 bg-surface-2">
        <div
          class="flex flex-1 min-h-0 min-w-0 overflow-auto outline-none"
          tabindex="0"
          role="group"
          [attr.aria-label]="current()?.name ?? texts.of('FileViewer')"
          data-file-stage
        >
          @if (current(); as file) {
            @if (showsImage()) {
              <img
                [src]="file.url"
                [alt]="file.name"
                decoding="async"
                [class]="isActualSize() ? 'm-auto max-w-none' : 'm-auto max-w-full max-h-full object-contain'"
                (error)="links.imageFailed(file)"
                data-file-image
              />
            } @else if (showsVideo()) {
              <video
                class="m-auto max-w-full max-h-full"
                controls
                playsinline
                preload="none"
                [autoplay]="resumesAt() !== null"
                [attr.poster]="file.thumbUrl"
                [src]="file.url"
                [attr.aria-label]="file.name"
                (loadedmetadata)="links.videoLoaded(file, $event)"
                (playing)="links.videoPlaying(file)"
                (error)="links.videoFailed(file, $event)"
                data-file-video
              ></video>
            } @else {
              <div class="m-auto flex flex-col items-center gap-3 p-6 rounded-lg border border-line bg-surface" data-file-card>
                <span
                  class="flex items-center justify-center size-24 rounded-md bg-surface-2 text-xl font-semibold text-muted"
                  aria-hidden="true"
                >
                  {{ badge() }}
                </span>
                @if (isImage() || isVideo()) {
                  <p class="m-0 text-sm text-muted" data-file-failed>
                    {{ source.isRefreshing() ? texts.of('Loading') : texts.of('FileLoadFailed') }}
                  </p>
                }
                @if (file.url; as url) {
                  <div class="flex flex-wrap justify-center gap-2">
                    <a [class]="linkButton" [href]="url" target="_blank" rel="noopener noreferrer" data-file-open-tab>
                      <i icon="external"></i>
                      <span>{{ texts.of('Open') }}</span>
                      <span class="sr-only">({{ texts.of('OpensInNewTab') }})</span>
                    </a>
                    <a [class]="linkButton" [href]="downloadLinkOf(url)" [attr.download]="file.name" data-file-card-download>
                      <i icon="download"></i>
                      <span>{{ texts.of('Download') }}</span>
                    </a>
                  </div>
                }
              </div>
            }
          }
        </div>
        @if (files().length > 1) {
          <ui-btn
            class="absolute left-2 top-1/2 -translate-y-1/2"
            icon="chevron-left"
            color="slate"
            [title]="texts.of('PreviousFile')"
            [disabled]="index() <= 0"
            (click)="move('ArrowLeft')"
            data-file-previous
          />
          <ui-btn
            class="absolute right-2 top-1/2 -translate-y-1/2"
            icon="chevron-right"
            color="slate"
            [title]="texts.of('NextFile')"
            [disabled]="index() >= files().length - 1"
            (click)="move('ArrowRight')"
            data-file-next
          />
        }
      </div>
      @if (isInfoOpen() && current(); as file) {
        <section
          class="flex-none max-h-[45%] overflow-y-auto border-t border-line bg-surface @4xl/panel:max-h-none @4xl/panel:w-80 @4xl/panel:border-t-0 @4xl/panel:border-l"
          [attr.aria-label]="texts.of('FileInfo')"
          data-file-info-panel
        >
          <ui-file-info-panel [file]="file" />
        </section>
      }
    </div>

    @if (files().length > 1) {
      <ul role="list" class="m-0 flex-none flex gap-2 p-2 list-none overflow-x-auto border-t border-line" data-file-strip>
        @for (file of files(); track file.key; let i = $index) {
          <li class="flex-none">
            <button
              #thumb
              type="button"
              [class]="i === index() ? thumbCurrent : thumbOther"
              [attr.aria-label]="texts.of('OpenFile', file.name)"
              [attr.aria-current]="i === index() ? 'true' : null"
              [tabIndex]="i === index() ? 0 : -1"
              (click)="show(i)"
              data-file-thumb
            >
              @if (thumbOf(file); as picture) {
                <img
                  [src]="picture"
                  alt=""
                  loading="lazy"
                  decoding="async"
                  class="size-full object-cover"
                  (error)="links.thumbFailed(picture)"
                />
              } @else {
                <span class="text-xs font-semibold text-muted" aria-hidden="true">{{ badgeOf(file.name) }}</span>
              }
            </button>
          </li>
        }
      </ul>
    }
  `,
  host: { class: 'flex flex-col h-full min-h-0 min-w-0', '(keydown)': 'onKeydown($event)' },
})
export class FileViewerOverlayComponent {
  protected readonly texts = inject(TextsService);
  protected readonly source = inject(FILE_VIEWER_SOURCE);
  private readonly locale = inject(LocaleService).current;
  private readonly ref = inject(OverlayRef, { optional: true });
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly thumbs = viewChildren<ElementRef<HTMLElement>>('thumb');

  readonly model = input<FileViewerOverlayModel | null>();

  private readonly sequence = new FileViewerSequence(
    () => this.source.files(),
    () => this.model()?.key ?? null,
  );

  protected readonly files = this.sequence.files;
  protected readonly index = this.sequence.index;
  protected readonly current = this.sequence.current;

  protected readonly counter = computed(() => {
    const count = this.files().length;
    return count ? this.texts.of('FileCounter', this.index() + 1, count) : '';
  });

  protected readonly size = computed(() => {
    const size = this.current()?.size;
    return size === null || size === undefined ? '' : formatBytes(size, this.locale());
  });

  protected readonly badge = computed(() => badgeOf(this.current()?.name ?? ''));
  protected readonly isImage = computed(() => isInlineImage(this.current()?.contentType));
  protected readonly isVideo = computed(() => isInlineVideo(this.current()?.contentType));
  protected readonly links = new FileViewerLinks(() => this.source.refresh());

  protected readonly showsImage = computed(() => {
    const file = this.current();
    return this.isImage() && !!file?.url && !this.links.hasFailed(file.url);
  });

  protected readonly showsVideo = computed(() => {
    const file = this.current();
    return this.isVideo() && !!file?.url && !this.links.hasFailed(file.url);
  });

  protected readonly resumesAt = computed(() => this.links.resumesAt(this.current()?.key));
  protected readonly isActualSize = signal(false);
  protected readonly isDeleting = signal(false);
  protected readonly isInfoOpen = signal(false);
  protected readonly canDelete = computed(() => this.source.canDelete() && this.current()?.key != null);

  protected readonly badgeOf = badgeOf;
  protected readonly downloadLinkOf = downloadLinkOf;
  protected readonly linkButton = SLATE_SOFT_BTN_CLASSES;
  protected readonly linkIconButton = SLATE_GHOST_ICON_BTN_CLASSES;
  protected readonly thumbCurrent = `${THUMB_CLASSES} border-checked`;
  protected readonly thumbOther = `${THUMB_CLASSES} border-transparent`;

  constructor() {
    this.ref?.name({ label: this.texts.of('FileViewer') });
  }

  protected thumbOf(file: FileView): string | null {
    const picture = pictureOf(file);
    return picture && !this.links.hasFailed(picture) ? picture : null;
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.altKey || event.ctrlKey || event.metaKey || !isViewerMoveKey(event.key) || isInInfo(event.target)) {
      return;
    }
    event.preventDefault();
    this.move(event.key);
  }

  protected move(key: string): void {
    const target = this.sequence.targetIndexFor(key);
    if (target !== null) {
      this.show(target);
    }
  }

  protected show(index: number): void {
    const isStripFocused = isInStrip(document.activeElement);
    if (!this.sequence.select(index)) {
      return;
    }
    afterNextRender(
      () => {
        const thumb = this.thumbs()[index]?.nativeElement;
        if (isStripFocused) {
          thumb?.focus();
        }
        thumb?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
      },
      { injector: this.injector },
    );
  }

  protected async remove(): Promise<void> {
    const file = this.current();
    if (!file || file.key === null || this.isDeleting()) {
      return;
    }
    const at = this.index();
    this.isDeleting.set(true);
    let deleted: boolean;
    try {
      deleted = await this.source.delete(file);
    } finally {
      if (!this.destroyRef.destroyed) {
        this.isDeleting.set(false);
      }
    }
    if (!deleted || this.destroyRef.destroyed) {
      return;
    }
    if (!this.sequence.recordDeleted(file.key, at)) {
      this.close();
    }
  }

  protected close(): void {
    void this.ref?.dismiss();
  }
}
