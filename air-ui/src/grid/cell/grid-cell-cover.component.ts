import { Component, computed, DestroyRef, inject, Injector, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import type { FileGridServiceEndpoints, GridCellCoverModel, GridCoverDto, GridRequest } from '../../core/generated/contracts';
import { isRecord } from '../../core/json';
import { TextsService } from '../../core/texts.service';
import { badgeOf, filesOfPage, type FileView, pictureOf } from '../../file/file-view';
import { fileInfoSourceOf, type FileViewerSource, openFileViewer } from '../../file/file-viewer';
import { OverlayService } from '../../overlay/overlay.service';
import { FOCUS_RING } from '../../theme/css-classes';
import { GridCellBase } from './grid-cell-base';

/** The suffix of the path a cover column takes after the member holding the record's key: `idCover` for `id`. */
const COVER_SUFFIX = 'Cover';

/** The cover's picture: a small rounded square the height of a row's control. */
const PICTURE = 'flex items-center justify-center size-7 shrink-0 rounded-sm bg-surface-2 overflow-hidden';

/**
 * A record's cover, the file its file grid marks (`AsCover<TFileGrid>()`): its thumbnail, drawn as the file cell draws
 * one — the cover's `thumbUrl`, else its `url` for a raster image a link serves inline, else the badge of its extension.
 * Activated, it asks the file grid's `get-data` under the row's record, the key the column stands on (`id` under
 * `idCover`), and opens the file viewer at the cover over the record's files; the viewer asks that page again for fresh
 * links, and its Info reads the file grid under the record, as text: a cover column carries neither the upload nor
 * the metadata's right. A row without a cover draws an empty cell; a disabled column draws the picture and opens nothing.
 */
@Component({
  selector: 'ui-grid-cell-cover',
  template: `
    @if (cover(); as cover) {
      @if (canOpen()) {
        <button
          type="button"
          [class]="buttonClasses"
          [attr.aria-label]="texts.of('OpenFile', cover.name)"
          [attr.aria-busy]="isOpening() || null"
          [title]="cover.name"
          (click)="open($event)"
          data-cover-open
        >
          @if (picture(); as picture) {
            <img
              [src]="picture"
              alt=""
              loading="lazy"
              decoding="async"
              class="size-full object-cover"
              (error)="failedPicture.set(picture)"
            />
          } @else {
            <span class="text-xs font-semibold text-muted" aria-hidden="true">{{ badge() }}</span>
          }
        </button>
      } @else {
        <span [class]="pictureClasses" [title]="cover.name">
          @if (picture(); as picture) {
            <img
              [src]="picture"
              alt=""
              loading="lazy"
              decoding="async"
              class="size-full object-cover"
              (error)="failedPicture.set(picture)"
            />
          } @else {
            <span class="text-xs font-semibold text-muted" aria-hidden="true">{{ badge() }}</span>
          }
        </span>
      }
    }
  `,
  host: { class: 'flex items-center h-full min-w-0 px-2' },
})
export class GridCellCoverComponent extends GridCellBase<GridCellCoverModel> {
  protected readonly texts = inject(TextsService);
  private readonly api = inject(Api);
  private readonly overlay = inject(OverlayService);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  /** Whether the record's files are being asked, before the viewer opens. */
  protected readonly isOpening = signal(false);

  /** The picture that failed to load: the badge stands in for it. */
  protected readonly failedPicture = signal<string | null>(null);

  /** The row's cover, as the service filled the column's path; null for a row without one. */
  protected readonly cover = computed(() => coverOf(this.value()));

  protected readonly picture = computed(() => {
    const cover = this.cover();
    const picture = cover ? pictureOf(fileViewOfCover(cover)) : null;
    return picture !== this.failedPicture() ? picture : null;
  });

  protected readonly badge = computed(() => badgeOf(this.cover()?.name ?? ''));

  /** The key of the row's record: the member the column stands on, its path less `Cover`. */
  private readonly parentId = computed(() => {
    const path = this.column().path ?? '';
    const member = path.length > COVER_SUFFIX.length && path.endsWith(COVER_SUFFIX) ? path.slice(0, -COVER_SUFFIX.length) : 'id';
    const key = this.row()?.[member];
    return typeof key === 'number' ? key : null;
  });

  protected readonly canOpen = computed(
    () => !this.isDisabled() && !!this.model() && (this.model()!.service ? this.parentId() !== null : !!this.cover()),
  );

  protected readonly buttonClasses = `${PICTURE} cursor-pointer ${FOCUS_RING}`;
  protected readonly pictureClasses = PICTURE;

  /** Asks the record's files and opens the viewer at the cover over them, while the cell and the grid's page stand. */
  protected async open(event: Event): Promise<void> {
    event.stopPropagation();
    const model = this.model();
    const cover = this.cover();
    if (!model || !cover || this.isOpening()) {
      return;
    }
    if (!model.service) {
      const files = signal<readonly FileView[]>([fileViewOfCover(cover)]);
      const source: FileViewerSource = {
        files,
        canDelete: signal(false),
        isRefreshing: signal(false),
        delete: () => Promise.resolve(false),
        refresh: () => undefined,
        ...fileInfoSourceOf(this.injector, {
          place: () => null,
          canEditInfo: signal(false),
          canReadMetadata: signal(false),
          conflictTitle: () => undefined,
          saved: () => undefined,
        }),
      };
      openFileViewer(this.overlay, this.injector, source, cover.id);
      return;
    }
    const parentId = this.parentId();
    if (parentId === null) {
      return;
    }
    const isCurrent = this.grid?.capture() ?? (() => true);
    const request: GridRequest = { parentService: model.parentService, parentId, isDataOnly: true, page: 1 };
    const files = signal<readonly FileView[]>([]);
    const isRefreshing = signal(false);
    const ask = async (): Promise<readonly FileView[]> => {
      const answer = await firstValueFrom(this.api.postTo<FileGridServiceEndpoints, 'get-data'>(model.service, 'get-data', request));
      return filesOfPage(answer.data);
    };

    this.isOpening.set(true);
    try {
      files.set(await ask());
    } catch {
      // The global alert reports the failure; nothing opens
      return;
    } finally {
      if (!this.destroyRef.destroyed) {
        this.isOpening.set(false);
      }
    }
    if (this.destroyRef.destroyed || !isCurrent()) {
      return;
    }

    const refresh = (): void => {
      isRefreshing.set(true);
      ask()
        .then(fresh => files.set(fresh))
        .catch(() => undefined)
        .finally(() => isRefreshing.set(false));
    };
    const source: FileViewerSource = {
      files,
      canDelete: signal(false),
      isRefreshing,
      delete: () => Promise.resolve(false),
      refresh,
      ...fileInfoSourceOf(this.injector, {
        place: () => ({ service: model.service, parentService: model.parentService, parentId }),
        canEditInfo: signal(false),
        canReadMetadata: signal(false),
        conflictTitle: () => this.grid?.model()?.mutationTexts.conflictTitle,
        saved: refresh,
      }),
    };
    openFileViewer(this.overlay, this.injector, source, cover.id);
  }
}

/** A cover as the service answers it, or null for anything else: a row without a cover answers none. */
function coverOf(value: unknown): GridCoverDto | null {
  if (typeof value === 'string') {
    const url = value.trim();
    if (!url) {
      return null;
    }
    const name = url.split('/').pop()?.split('?')[0] || url;
    return {
      id: 0,
      name,
      url,
      contentType: 'image/*',
      thumbUrl: url,
    };
  }
  if (!isRecord(value) || typeof value['id'] !== 'number' || typeof value['name'] !== 'string' || typeof value['url'] !== 'string') {
    return null;
  }
  return {
    id: value['id'],
    name: value['name'],
    url: value['url'],
    contentType: typeof value['contentType'] === 'string' ? value['contentType'] : '',
    thumbUrl: typeof value['thumbUrl'] === 'string' && value['thumbUrl'] !== '' ? value['thumbUrl'] : null,
  };
}

function fileViewOfCover(cover: GridCoverDto): FileView {
  return { key: cover.id, name: cover.name, size: null, contentType: cover.contentType, url: cover.url, thumbUrl: cover.thumbUrl ?? null };
}
