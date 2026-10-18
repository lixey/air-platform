import { NgTemplateOutlet } from '@angular/common';
import { booleanAttribute, Component, computed, inject, input, output, signal } from '@angular/core';
import { LocaleService } from '../app/locale.service';
import { BtnComponent } from '../btn/btn.component';
import type { FilesLook } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { FOCUS_RING, SEGMENT_GHOST, SEGMENTED_GHOST } from '../theme/css-classes';
import { formatBytes } from './file-size';
import { FILE_UPLOAD_CLASSES, FILE_UPLOAD_TEXTS, type FileUpload, UNDER_WAY } from './file-upload-queue';
import { badgeOf, downloadLinkOf, type FileView, pictureOf, stemOf } from './file-view';

/** The frame of a file in each look: a card of the tiles, a line of the list. */
const FRAME_CLASSES = {
  tiles: 'flex flex-col h-full min-w-0 rounded-lg border border-line bg-surface overflow-hidden',
  list: 'flex items-center gap-2 min-w-0 min-h-11 px-1',
} satisfies Record<FilesLook, string>;

/** The frame of a file being moved: its edge in the checked role, lifted above the files making room. */
const MOVING_FRAME_CLASSES = {
  tiles: 'flex flex-col h-full min-w-0 rounded-lg border border-checked bg-surface overflow-hidden shadow-lg',
  list: 'flex items-center gap-2 min-w-0 min-h-11 px-1 rounded-lg border border-checked bg-surface shadow-lg',
} satisfies Record<FilesLook, string>;

/** What a file opens by: the picture, the name and the size, stacked in a tile, in one line in the list. */
const OPEN_CLASSES = {
  tiles: `flex flex-col items-stretch min-w-0 text-left cursor-pointer rounded-t-lg hover:bg-hover ${FOCUS_RING}`,
  list: `flex flex-1 items-center gap-2 min-w-0 min-h-10 px-1 text-left cursor-pointer rounded-md hover:bg-hover ${FOCUS_RING}`,
} satisfies Record<FilesLook, string>;

/** The picture's box: the tile's upper part, the list's small square. */
const PICTURE_CLASSES = {
  tiles: 'flex items-center justify-center aspect-4/3 w-full bg-surface-2 overflow-hidden',
  list: 'flex flex-none items-center justify-center size-9 rounded-md bg-surface-2 overflow-hidden',
} satisfies Record<FilesLook, string>;

/** The name, under the picture in a tile, beside it with the size in the list. */
const CAPTION_CLASSES = {
  tiles: 'flex flex-col min-w-0 px-2 py-1',
  list: 'flex flex-1 items-baseline gap-2 min-w-0',
} satisfies Record<FilesLook, string>;

/** The picture and the caption of a file on its way, laid out as the open button lays them out for a file of the page. */
const CAPTION_ROW_CLASSES = {
  tiles: 'flex flex-col min-w-0',
  list: 'flex flex-1 items-center gap-2 min-w-0 px-1',
} satisfies Record<FilesLook, string>;

/**
 * A tile's name: its lines wrap, a long word breaking, up to the two lines a menu's name takes, cut with an ellipsis
 * after them; both lines are taken for a one-line name too, so the tiles of a row stand equal. The name with its
 * extension is read by assistive technology alone.
 */
const TILE_NAME = 'min-w-0 h-[2lh] text-sm leading-tight text-ink line-clamp-2 break-words';

/** A list line's name: one line, the size beside it. */
const LINE_NAME = 'min-w-0 truncate text-sm text-ink';

/** Where the actions of a file on its way stand: along the tile's foot, at the list line's end. */
const ACTIONS_CLASSES = {
  tiles: 'flex items-center gap-1 mt-auto p-1',
  list: 'flex flex-none items-center gap-1',
} satisfies Record<FilesLook, string>;

/**
 * A file's actions, one row of joined grey ghost buttons (`SEGMENTED_GHOST`): in a tile it stands on the tile's left,
 * bottom and right edges with no gap, a hairline above it parting it from the name, its buttons sharing the tile's
 * width, so four stand inside the narrowest tile; at the list line's end its buttons are square.
 */
const BLOCK_CLASSES = {
  tiles: `${SEGMENTED_GHOST} mt-auto border-t border-line`,
  list: `${SEGMENTED_GHOST} flex-none`,
} satisfies Record<FilesLook, string>;

/**
 * A button of the actions' row. In a tile the first and last take the tile's inner radius at its lower corners, so a
 * hover and the focus ring, drawn inside the button, follow the tile's corners and are never cut by them; in the list
 * each is a ghost button's rounded square.
 */
const ACTION_CLASSES = {
  tiles: `${SEGMENT_GHOST} flex-1 min-w-7 first:rounded-bl-[calc(var(--radius-lg)-1px)] last:rounded-br-[calc(var(--radius-lg)-1px)]`,
  list: `${SEGMENT_GHOST} flex-none w-9 rounded-lg`,
} satisfies Record<FilesLook, string>;

/** The handle that moves a file refuses the browser's gestures, so a finger drags it and the page does not scroll. */
const HANDLE_CLASSES = 'cursor-grab! active:cursor-grabbing! touch-none';

/** A pressed pin, quietly: its icon in the checked role, the ghost's ground unchanged. */
const PRESSED_CLASSES = 'text-checked!';

/** An action whose request is posting: dimmed, refusing the pointer. */
const BUSY_CLASSES = 'opacity-50 cursor-not-allowed!';

/**
 * One file of a files element, drawn in its look: a tile — the picture above, the name without its extension under it,
 * on two lines, the actions along its foot, the name with its extension and the size as its title — or
 * a line of the list with the picture, the name with its extension, the size and the actions in a row. The picture is
 * the file's thumbnail, a video's first frame among them, else its own content for a raster image a link serves inline,
 * else the badge of its extension, a video without a thumbnail among them; a picture that fails to load falls back to
 * the badge.
 *
 * A file of the page is a button named "Open {name}" that emits `opened`. Its actions stand joined in one row of grey
 * ghost buttons, across the tile's foot from edge to edge: when `movable`, a handle named "Move {name}" and described by
 * `moveHint`, which carries `data-reorder-handle` with the file's key for the reorder around the tiles
 * (`FileReorderDirective`), `moving` drawing the file lifted while it is carried; when
 * `coverable`, a pin, pressed on the record's cover (`isCover`), which emits `coverRequested`;
 * Download, a link to its content with the query `download`, which the server answers as an attachment under its name;
 * and, when `deletable`, Delete, which emits `deleteRequested`, as the Delete key does on the button. What an open, a
 * delete or a pin means is the element's. A tile shows no description. A file on its way (`upload`) shows its status in
 * a polite live region, its progress while it posts and its reason when it did not go, with Try again for a failed
 * post, Remove for a refused or failed one and, when `cancelable`, Cancel for a waiting, preparing or posting one, which
 * emits `cancelRequested`; it opens nothing, having no content yet.
 */
@Component({
  selector: 'ui-file-tile',
  imports: [BtnComponent, NgTemplateOutlet, UiIconDirective],
  template: `
    <ng-template #caption>
      <span [class]="captionClasses()">
        @if (look() === 'tiles') {
          <span [id]="nameId" [class]="tileName" data-file-name>
            <span aria-hidden="true">{{ stem() }}</span>
            <span class="sr-only">{{ file().name }}</span>
          </span>
        } @else {
          <span [id]="nameId" [class]="lineName" [title]="file().name" data-file-name>{{ file().name }}</span>
          @if (size(); as size) {
            <span class="shrink-0 text-xs text-muted">{{ size }}</span>
          }
        }
      </span>
    </ng-template>
    <div [class]="frameClasses()" [attr.title]="tileTitle()" [attr.data-status]="upload()?.status ?? null">
      @if (upload(); as upload) {
        <div [class]="captionRow()">
          <div [class]="pictureClasses()" aria-hidden="true">
            <span class="text-xs font-semibold text-muted">{{ badge() }}</span>
          </div>
          <ng-container [ngTemplateOutlet]="caption" />
        </div>
        <div class="flex flex-col gap-1 min-w-0 px-2" [class.flex-1]="look() === 'list'">
          @if (upload.status === 'uploading') {
            <div
              class="h-1 rounded-full bg-line overflow-hidden"
              role="progressbar"
              aria-valuemin="0"
              aria-valuemax="100"
              [attr.aria-valuenow]="upload.progress"
              [attr.aria-labelledby]="nameId"
            >
              <div class="h-full bg-checked" [style.width.%]="upload.progress"></div>
            </div>
          }
          <p role="status" class="m-0 text-xs" [class]="statusClasses()" data-upload-status>
            {{ statusText() }}
            @if (upload.detail; as detail) {
              <span class="block">{{ detail }}</span>
            }
          </p>
        </div>
        @if (isUnderWay() && cancelable()) {
          <div [class]="actionsClasses()">
            <ui-btn
              icon="close"
              variant="ghost"
              [aria-label]="texts.of('CancelUpload', file().name)"
              [title]="texts.of('Cancel')"
              (click)="cancelRequested.emit()"
              data-upload-cancel
            />
          </div>
        } @else if (upload.status === 'failed' || upload.status === 'refused') {
          <div [class]="actionsClasses()">
            @if (upload.status === 'failed') {
              <ui-btn
                icon="reload"
                variant="ghost"
                [title]="texts.of('Retry')"
                [attr.aria-describedby]="nameId"
                (click)="retryRequested.emit()"
              />
            }
            <ui-btn
              icon="close"
              variant="ghost"
              [title]="texts.of('FileRemove')"
              [attr.aria-describedby]="nameId"
              (click)="removeRequested.emit()"
            />
          </div>
        }
      } @else {
        <button
          type="button"
          [class]="openClasses()"
          [attr.aria-label]="texts.of('OpenFile', file().name)"
          (click)="opened.emit()"
          (keydown.delete)="askDelete($event)"
          data-file-open
        >
          <span [class]="pictureClasses()">
            @if (picture(); as picture) {
              <img
                [src]="picture"
                alt=""
                loading="lazy"
                decoding="async"
                draggable="false"
                class="size-full object-cover"
                (error)="failedPicture.set(picture)"
              />
            } @else {
              <span class="text-xs font-semibold text-muted" aria-hidden="true">{{ badge() }}</span>
            }
          </span>
          <ng-container [ngTemplateOutlet]="caption" />
        </button>
        @if (hasActions()) {
          <div [class]="blockClasses()" data-file-actions>
            @if (isMovable()) {
              <button
                type="button"
                [class]="handleClasses()"
                [attr.data-reorder-handle]="file().key"
                [attr.aria-label]="texts.of('MoveFile', file().name)"
                [attr.aria-describedby]="moveHint()"
                [title]="texts.of('MoveFile', file().name)"
              >
                <i icon="grip-vertical" aria-hidden="true"></i>
              </button>
            }
            @if (coverable()) {
              <button
                type="button"
                [class]="pinClasses()"
                [attr.aria-label]="texts.of('Cover')"
                [title]="isCover() ? texts.of('Cover') : texts.of('SetCover')"
                [attr.aria-pressed]="isCover()"
                [attr.aria-busy]="settingCover() || null"
                [attr.aria-disabled]="settingCover() || null"
                [attr.aria-describedby]="nameId"
                (click)="setCover()"
                data-file-cover
              >
                <i [icon]="settingCover() ? 'spinner' : 'pin'" aria-hidden="true"></i>
              </button>
            }
            @if (file().url; as url) {
              <a
                [class]="actionClasses()"
                [href]="downloadLinkOf(url)"
                [attr.download]="file().name"
                draggable="false"
                [attr.aria-label]="texts.of('Download')"
                [attr.aria-describedby]="nameId"
                [title]="texts.of('Download')"
                data-file-download
              >
                <i icon="download" aria-hidden="true"></i>
              </a>
            }
            @if (deletable()) {
              <button
                type="button"
                [class]="deleteClasses()"
                [attr.aria-label]="texts.of('DeleteFile', file().name)"
                [title]="texts.of('DeleteFile', file().name)"
                [attr.aria-busy]="deleting() || null"
                [attr.aria-disabled]="deleting() || null"
                (click)="askDelete($event)"
                data-file-delete
              >
                <i [icon]="deleting() ? 'spinner' : 'trash'" aria-hidden="true"></i>
              </button>
            }
          </div>
        }
      }
    </div>
  `,
  host: { class: 'block min-w-0' },
})
export class FileTileComponent {
  private static next = 0;

  protected readonly texts = inject(TextsService);
  private readonly locale = inject(LocaleService).current;

  readonly file = input.required<FileView>();
  readonly look = input<FilesLook>('tiles');
  /** The file's place in the upload queue while it is on its way; null for a file of the page. */
  readonly upload = input<FileUpload | null>(null);
  /** Whether the file offers its delete. */
  readonly deletable = input(false);
  /** Whether the file's delete is posting. */
  readonly deleting = input(false);
  /** Whether the file offers its pin, which makes it the record's cover or, pressed, clears the cover. */
  readonly coverable = input(false);
  /** Whether the file is its record's cover: the pin is drawn pressed. */
  readonly isCover = input(false);
  /** Whether the pin's request is posting. */
  readonly settingCover = input(false);
  /** Whether a file on its way offers Cancel while it waits, is prepared or posts. */
  readonly cancelable = input(false, { transform: booleanAttribute });
  /** Whether the file offers its handle, which moves it among the files. */
  readonly movable = input(false);
  /** Whether the file is being moved: drawn lifted. */
  readonly moving = input(false);
  /** The id of the text that tells how the handle moves the file; none leaves it undescribed. */
  readonly moveHint = input<string | null>(null);

  readonly opened = output();
  readonly deleteRequested = output();
  readonly retryRequested = output();
  readonly removeRequested = output();
  readonly cancelRequested = output();
  readonly coverRequested = output();

  /** The picture that failed to load: the badge stands in for it. */
  protected readonly failedPicture = signal<string | null>(null);

  protected readonly picture = computed(() => {
    const picture = pictureOf(this.file());
    return picture !== this.failedPicture() ? picture : null;
  });

  protected readonly badge = computed(() => badgeOf(this.file().name));

  /** A tile's visible name, without its extension; assistive technology reads the whole name. */
  protected readonly stem = computed(() => stemOf(this.file().name));

  protected readonly size = computed(() => {
    const size = this.file().size;
    return size === null ? '' : formatBytes(size, this.locale());
  });

  /** A tile's title, shown on hover: the name with its extension and the size, a line each; a list line has none. */
  protected readonly tileTitle = computed(() => {
    if (this.look() !== 'tiles') {
      return null;
    }
    const size = this.size();
    return size ? [this.file().name, size].join('\n') : this.file().name;
  });

  protected readonly statusText = computed(() => {
    const upload = this.upload();
    return upload ? this.texts.of(FILE_UPLOAD_TEXTS[upload.status]) : '';
  });

  /** Whether the file waits, is being prepared or posts. */
  protected readonly isUnderWay = computed(() => {
    const upload = this.upload();
    return !!upload && UNDER_WAY.includes(upload.status);
  });

  protected readonly statusClasses = computed(() => {
    const upload = this.upload();
    return upload ? FILE_UPLOAD_CLASSES[upload.status] : '';
  });

  protected readonly frameClasses = computed(() => (this.moving() ? MOVING_FRAME_CLASSES : FRAME_CLASSES)[this.look()]);
  protected readonly openClasses = computed(() => OPEN_CLASSES[this.look()]);
  protected readonly pictureClasses = computed(() => PICTURE_CLASSES[this.look()]);
  protected readonly captionClasses = computed(() => CAPTION_CLASSES[this.look()]);
  protected readonly actionsClasses = computed(() => ACTIONS_CLASSES[this.look()]);
  protected readonly blockClasses = computed(() => BLOCK_CLASSES[this.look()]);
  protected readonly actionClasses = computed(() => ACTION_CLASSES[this.look()]);
  protected readonly handleClasses = computed(() => `${this.actionClasses()} ${HANDLE_CLASSES}`);
  protected readonly pinClasses = computed(() =>
    [this.actionClasses(), this.isCover() ? PRESSED_CLASSES : '', this.settingCover() ? BUSY_CLASSES : ''].filter(Boolean).join(' '),
  );
  protected readonly deleteClasses = computed(() => (this.deleting() ? `${this.actionClasses()} ${BUSY_CLASSES}` : this.actionClasses()));

  /** Whether the file offers its handle: a file of the page that the element lets move. */
  protected readonly isMovable = computed(() => this.movable() && this.file().key !== null);

  /** Whether the file offers any action, so the actions' row is drawn. */
  protected readonly hasActions = computed(() => this.isMovable() || this.coverable() || !!this.file().url || this.deletable());
  protected readonly captionRow = computed(() => CAPTION_ROW_CLASSES[this.look()]);

  protected readonly tileName = TILE_NAME;
  protected readonly lineName = LINE_NAME;
  protected readonly downloadLinkOf = downloadLinkOf;
  protected readonly nameId = `ui-file-tile-${++FileTileComponent.next}-name`;

  /** Delete, or the Delete key on the file, asks to delete it, where it offers its delete and none is posting. */
  protected askDelete(event: Event): void {
    if (this.deletable() && !this.deleting()) {
      event.preventDefault();
      this.deleteRequested.emit();
    }
  }

  /** The pin asks to make the file the record's cover, or to clear it, while no such request is posting. */
  protected setCover(): void {
    if (!this.settingCover()) {
      this.coverRequested.emit();
    }
  }
}
