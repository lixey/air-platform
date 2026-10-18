import {
  afterNextRender,
  Component,
  computed,
  DestroyRef,
  type ElementRef,
  inject,
  Injector,
  input,
  linkedSignal,
  signal,
  viewChildren,
} from '@angular/core';
import { LocaleService } from '../app/locale.service';
import { BtnComponent, SLATE_GHOST_ICON_BTN_CLASSES, SLATE_SOFT_BTN_CLASSES } from '../btn/btn.component';
import { TextsService } from '../core/texts.service';
import type { GridRowKey } from '../grid/grid-row';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { OverlayRef } from '../overlay/overlay-ref';
import { FOCUS_RING } from '../theme/css-classes';
import type { FileViewerOverlayModel } from '../ui-components';
import { FileInfoPanelComponent } from './file-info-panel.component';
import { formatBytes } from './file-size';
import { badgeOf, downloadLinkOf, type FileView, isInlineImage, isInlineVideo, pictureOf } from './file-view';
import { FILE_VIEWER_SOURCE } from './file-viewer';
import { FileViewerLinks } from './file-viewer-links';

/** A thumbnail of the strip: a small square button, the shown file's framed in the checked role. */
const THUMB = `flex items-center justify-center size-14 rounded-md border-2 bg-surface-2 overflow-hidden cursor-pointer ${FOCUS_RING}`;

/** The keys that walk the files, with where each goes from the shown one among `count`. */
const MOVES: Readonly<Record<string, (index: number, count: number) => number>> = {
  ArrowLeft: index => index - 1,
  ArrowRight: index => index + 1,
  Home: () => 0,
  End: (_, count) => count - 1,
};

/**
 * The file viewer (`FileViewerOverlay`), a dialog over the whole window opened at one file over a sequence of files its
 * opener gives it (`FILE_VIEWER_SOURCE`): a files element's files, the page's pictures of a file grid, a record's files
 * from its cover.
 *
 * An image shows from its `url`, fitted to the stage or at its natural size by the pair of toggles; an MP4 video plays
 * in the browser's own player, `<video controls playsinline preload="none">` with its thumbnail as the poster, which
 * requests nothing before it is played; any other file, a PDF included, shows as its card, the badge of its extension
 * with Open, which follows the `url` in a new browsing context with neither opener nor referrer, and Download; no
 * document is drawn inside the viewer. Download follows the `url` with the query `download`, which the server answers
 * as an attachment. The header names the file and its size, counts its place ("3 / 5") in a polite live region, and
 * offers Download, Delete where the opener offers it, Info, and Close, spaced one unit apart as a nav's buttons are
 * and all in the neutral colour, Delete too; the strip under the stage shows the sequence's
 * thumbnails, the shown one current. Info toggles a panel (`FileInfoPanel`) beside the stage on a wide panel and under
 * it on a narrow one, holding what the opener's file grid says of the shown file: the name and the description, as
 * fields where the opener offers the upload, and the metadata where it carries the right to read it.
 *
 * Left and Right show the previous and the next file, Home and End the first and the last, save inside Info, whose
 * fields keep their keys; Escape closes the viewer,
 * and the overlay gives the focus back to the element that opened it. Delete asks the opener, which confirms and
 * deletes, then shows the next file, or the previous one after the last, and closes when none is left. An image or a
 * video that fails to load makes the opener ask its page again, once for each file, for fresh links, and a video plays
 * on from the moment it stopped at; one that fails again shows its card with the reason and Download.
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
                icon="minimize"
                variant="ghost"
                color="slate"
                [title]="texts.of('FitToScreen')"
                [selected]="!isActualSize()"
                (click)="isActualSize.set(false)"
                data-file-fit
              />
              <ui-btn
                icon="maximize"
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

  /** The files deleted here: gone from the sequence at once, before the opener's page says so. */
  private readonly removed = signal<ReadonlySet<GridRowKey>>(new Set());

  /** The files the viewer walks: the opener's that have a key, less those deleted here. */
  protected readonly files = computed(() => {
    const removed = this.removed();
    return this.source.files().filter(file => file.key !== null && !removed.has(file.key));
  });

  /** The key of the shown file: the model's at the opening, then the one the user walks to. */
  private readonly shownKey = linkedSignal<GridRowKey | null>(() => this.model()?.key ?? null);

  /** Where the shown file stood, which holds the place when the file leaves the sequence. */
  private readonly position = signal(0);

  /** The shown file's place: where its key stands, else the last place held, within the sequence. */
  protected readonly index = computed(() => {
    const files = this.files();
    const found = files.findIndex(file => file.key === this.shownKey());
    return found >= 0 ? found : Math.min(this.position(), files.length - 1);
  });

  protected readonly current = computed<FileView | null>(() => this.files()[this.index()] ?? null);

  protected readonly counter = computed(() => {
    const count = this.files().length;
    return count ? this.texts.of('FileCounter', this.index() + 1, count) : '';
  });

  protected readonly size = computed(() => {
    const size = this.current()?.size;
    return size === null || size === undefined ? '' : formatBytes(size, this.locale());
  });

  protected readonly badge = computed(() => badgeOf(this.current()?.name ?? ''));

  /** Whether the shown file is a raster image a link serves inline, which the viewer draws. */
  protected readonly isImage = computed(() => isInlineImage(this.current()?.contentType));

  /** Whether the shown file is a video a link serves inline, which the viewer plays. */
  protected readonly isVideo = computed(() => isInlineVideo(this.current()?.contentType));

  /** The links that failed to load here, and where a video plays on from by its fresh one. */
  protected readonly links = new FileViewerLinks(() => this.source.refresh());

  protected readonly showsImage = computed(() => {
    const file = this.current();
    return this.isImage() && !!file?.url && !this.links.hasFailed(file.url);
  });

  protected readonly showsVideo = computed(() => {
    const file = this.current();
    return this.isVideo() && !!file?.url && !this.links.hasFailed(file.url);
  });

  /** Where the shown video plays on from its fresh link; null where it starts afresh, when the user plays it. */
  protected readonly resumesAt = computed(() => this.links.resumesAt(this.current()?.key));

  /** Whether the image shows at its natural size rather than fitted to the stage. */
  protected readonly isActualSize = signal(false);
  protected readonly isDeleting = signal(false);
  /** Whether Info shows beside the file: it stays open as the user walks the files. */
  protected readonly isInfoOpen = signal(false);
  protected readonly canDelete = computed(() => this.source.canDelete() && this.current()?.key != null);

  protected readonly badgeOf = badgeOf;
  protected readonly downloadLinkOf = downloadLinkOf;
  /** A link drawn as a medium soft slate button, with an icon and its label, by the classes `ui-btn` draws. */
  protected readonly linkButton = SLATE_SOFT_BTN_CLASSES;
  /** A link drawn as a medium ghost slate button with its icon alone, by the classes `ui-btn` draws. */
  protected readonly linkIconButton = SLATE_GHOST_ICON_BTN_CLASSES;
  protected readonly thumbCurrent = `${THUMB} border-checked`;
  protected readonly thumbOther = `${THUMB} border-transparent`;

  constructor() {
    this.ref?.name({ label: this.texts.of('FileViewer') });
  }

  /** The picture of a thumbnail of the strip; none where it failed, drawn as the badge. */
  protected thumbOf(file: FileView): string | null {
    const picture = pictureOf(file);
    return picture && !this.links.hasFailed(picture) ? picture : null;
  }

  /** Left and Right show the previous and the next file, Home and End the first and the last. */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.altKey || event.ctrlKey || event.metaKey || !(event.key in MOVES) || isInInfo(event.target)) {
      return;
    }
    event.preventDefault();
    this.move(event.key);
  }

  protected move(key: string): void {
    const count = this.files().length;
    const to = MOVES[key]?.(this.index(), count);
    if (to !== undefined && count > 0) {
      this.show(Math.max(0, Math.min(to, count - 1)));
    }
  }

  /**
   * Shows the file at `index` of the sequence, and brings its thumbnail into the strip's view. A focus in the strip moves
   * to that thumbnail, the strip's one Tab stop; a focus anywhere else in the viewer stays where it is.
   */
  protected show(index: number): void {
    const file = this.files()[index];
    if (!file || index === this.index()) {
      return;
    }
    const isStripFocused = isInStrip(document.activeElement);
    this.shownKey.set(file.key);
    this.position.set(index);
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

  /**
   * Asks the opener to delete the shown file, which it confirms first; once deleted, shows the file after it, or the
   * one before it after the last, and closes when none is left.
   */
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
    const key = file.key;
    this.removed.update(removed => new Set(removed).add(key));
    const rest = this.files();
    if (!rest.length) {
      this.close();
      return;
    }
    const next = Math.min(at, rest.length - 1);
    this.shownKey.set(rest[next].key);
    this.position.set(next);
  }

  protected close(): void {
    void this.ref?.dismiss();
  }
}

/** Whether a key came from inside Info, whose fields keep their keys. */
function isInInfo(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest('[data-file-info-panel]') !== null;
}

/** Whether the focus is on the strip of thumbnails. */
function isInStrip(active: Element | null): boolean {
  return active !== null && active.closest('[data-file-strip]') !== null;
}
