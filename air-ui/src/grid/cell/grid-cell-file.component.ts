import { Component, computed, inject, Injector, signal } from '@angular/core';
import type { GridCellFileModel, GridModel, GridNavUploadModel, NavModel } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import { type FileView, fileViewOf, pictureOf } from '../../file/file-view';
import { fileInfoSourceOf, type FileViewerSource, openFileViewer } from '../../file/file-viewer';
import { OverlayService } from '../../overlay/overlay.service';
import { FOCUS_RING } from '../../theme/css-classes';
import { type GridRow, keyOf } from '../grid-row';
import { GridCellBase } from './grid-cell-base';

/**
 * A file's name as a link to the row's `url`, opened in a new browsing context with neither opener nor referrer: an
 * inline image shows there, any other file downloads by its disposition. A row with a `thumbUrl`, or whose `contentType`
 * is an inline image, shows a small thumbnail before the name, loaded lazily from the `thumbUrl`, else from the `url`;
 * the thumbnail is a button that opens the file viewer at its row over the page's rows that show one, asking the page
 * again for fresh links when an image fails to load. The viewer's Info asks the grid's service under the grid's record;
 * its name and description are fields where the grid's nav offers the upload, which also says, by its
 * `canReadMetadata`, whether the metadata shows. A row without a `url`, and a disabled column, draw the name as
 * plain text. The link says, to a screen reader, that it opens a new tab. The link takes the grid's link ink, so a hue
 * the row paints turns it too.
 */
@Component({
  selector: 'ui-grid-cell-file',
  template: `
    @if (url(); as url) {
      @if (picture(); as picture) {
        <button type="button" [class]="thumbClasses" [attr.aria-label]="texts.of('OpenFile', text())" (click)="view($event)" data-file-view>
          <img [src]="picture" alt="" loading="lazy" decoding="async" class="size-5 rounded-sm object-cover" />
        </button>
      }
      <a
        class="block min-w-0 truncate text-link hover:text-link-hover hover:underline hover:underline-offset-4"
        target="_blank"
        rel="noopener noreferrer"
        [href]="url"
        [title]="text()"
        (click)="$event.stopPropagation()"
      >
        {{ text() }}
        <span class="sr-only">({{ texts.of('OpensInNewTab') }})</span>
      </a>
    } @else {
      <span class="block truncate" [title]="text()">{{ text() }}</span>
    }
  `,
  host: { class: 'flex items-center gap-2 h-full min-w-0 px-2' },
})
export class GridCellFileComponent extends GridCellBase<GridCellFileModel> {
  protected readonly texts = inject(TextsService);
  private readonly overlay = inject(OverlayService);
  private readonly injector = inject(Injector);

  protected readonly thumbClasses = `flex shrink-0 rounded-sm cursor-pointer ${FOCUS_RING}`;

  protected readonly text = computed(() => {
    const value = this.value();
    return value === null || value === undefined ? '' : String(value);
  });

  /** The row's signed link; null where there is none to follow or the column acts on nothing. */
  protected readonly url = computed(() => {
    const url = this.row()?.['url'];
    return typeof url === 'string' && url !== '' && !this.isDisabled() ? url : null;
  });

  /** The thumbnail beside the name: the row's `thumbUrl`, else its `url` for an inline image; null for any other file. */
  protected readonly picture = computed(() => {
    const row = this.row();
    return row && this.url() ? pictureOf(fileViewOf(row, null)) : null;
  });

  /**
   * Opens the viewer at this row's file over the page's rows that show
   * a thumbnail; the page is the grid's, asked again for fresh links.
   */
  protected view(event: Event): void {
    event.stopPropagation();
    const row = this.row();
    const key = keyOf(row);
    if (!row || key === null) {
      return;
    }
    const grid = this.grid;
    const upload = computed(() => uploadOf(grid?.model() ?? null));
    const source: FileViewerSource = {
      files: computed(() => picturedFilesOf(grid ? grid.rows() : [row])),
      canDelete: signal(false),
      isRefreshing: grid?.isLoading ?? signal(false),
      delete: () => Promise.resolve(false),
      refresh: () => grid?.reload(),
      ...fileInfoSourceOf(this.injector, {
        place: () => {
          const service = grid?.service();
          return grid && service ? { service, parentService: grid.request().parentService, parentId: grid.request().parentId } : null;
        },
        canEditInfo: computed(() => upload() !== null),
        canReadMetadata: computed(() => upload()?.canReadMetadata === true),
        conflictTitle: () => grid?.model()?.mutationTexts.conflictTitle,
        capture: () => grid?.capture() ?? (() => true),
        saved: () => grid?.reload(),
      }),
    };
    openFileViewer(this.overlay, this.injector, source, key);
  }
}

/** The files of the rows that show a thumbnail: a key, a link and a picture. */
function picturedFilesOf(rows: readonly GridRow[]): readonly FileView[] {
  return rows.flatMap(row => {
    const key = keyOf(row);
    const file = key === null ? null : fileViewOf(row, key);
    return file?.url && pictureOf(file) ? [file] : [];
  });
}

/** The upload a grid's nav offers; null where it offers none, to a reader among others. */
function uploadOf(model: GridModel | null): GridNavUploadModel | null {
  const navs: readonly NavModel[] = model ? [model.nav, model.actionNav] : [];
  for (const nav of navs) {
    for (const element of [...nav.start, ...nav.elements, ...nav.end, ...nav.more]) {
      if (element.kind === 'GridNavUpload') {
        return element;
      }
    }
  }
  return null;
}
