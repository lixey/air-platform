import { httpResource } from '@angular/common/http';
import {
  afterNextRender,
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  type ElementRef,
  inject,
  Injector,
  input,
  type Signal,
  viewChild,
} from '@angular/core';
import { shownByCaller } from '../../app/alert/error-alert.interceptor';
import { LocaleService } from '../../app/locale.service';
import { BtnComponent } from '../../btn/btn.component';
import { type AnswerOf, serviceUrl } from '../../core/api';
import type { FileGridServiceEndpoints, FormFilesModel, GridRequest } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import { FileReorderDirective, type FileReorderItem } from '../../file/file-reorder.directive';
import { FileTileComponent } from '../../file/file-tile.component';
import { FileUploadQueue, type FileUploadTarget } from '../../file/file-upload-queue';
import type { FileView } from '../../file/file-view';
import { fileInfoSourceOf, type FileViewerSource, openFileViewer } from '../../file/file-viewer';
import { FileZoneDirective } from '../../file/file-zone.directive';
import { OverlayService } from '../../overlay/overlay.service';
import { SCREEN_LIFETIME } from '../../screen/screen-lifetime';
import { FormService } from '../form.service';
import {
  type FileItem,
  fileItemsOf,
  fileRowsOf,
  FILES_LIST_CLASSES,
  filesAcceptsOf,
  filesCountText,
  filesHeldOf,
  filesLimitsText,
  filesTargetOf,
  filesZoneClasses,
  focusAfterViewer,
  holdLeaveWhileUploading,
  inPlacedOrder,
  isFilesFull,
  uploadsShownOf,
} from './form-files';
import { FormFilesActions } from './form-files-actions';

/**
 * The files of the form's record, drawn in the form's body after the elements of its tab: one page of the file grid's
 * data under the form's record, each file a tile (`FileTile`) in the look the model names, tiles or a list, which opens
 * the file viewer (`FileViewerOverlay`) at it over the element's files, downloads it and, where the model allows,
 * deletes it after a confirmation; "Delete all" deletes every file of the page in one request after a confirmation
 * naming the count and asking the user to type it, and "Download all", offered to a reader too, saves the file grid's
 * `download-all` under the name the answer gives, `Project_42.zip`, with the browser's moment,
 * `Project_42_260928153312.zip`. Where the model offers `canSetCover`, each file carries a pin: pressed on the record's
 * cover, it makes another file the cover through the file grid's `set-cover`, and clears the cover when pressed again.
 *
 * Where the model allows the upload, the element is itself the zone files enter by: it takes focus, takes files dropped
 * on it or pasted while it holds focus, opens the browser's picker on "Choose files" and on Enter or Space, and offers
 * "Take photo" on a coarse pointer when the upload accepts a raster image, and "Take video" when it accepts a video.
 * The limits and the count of the record's files against `maxFiles` show under it before any pick; a record holding
 * `maxFiles` files, or `maxTotalBytes`, is full: no zone, pick, paste or camera, and the count names it full. The count
 * takes the files uploaded since the page was asked until the page shows them. Each file goes through the upload queue
 * it provides (`FileUploadQueue`) and shows at once as a tile with its status, first among the element's files, with
 * Cancel while it is on its way; the page is asked again once the queue has no post left. While a file is on its way,
 * leaving the screen, closing the form and unloading the page ask the user first, and a confirmed leave cancels the
 * files. Where the upload is offered, each file of the page carries a handle that moves it among the files
 * (`FileReorderDirective`: dragged by a mouse, a pen or a finger, or carried by Space and the arrows), not while files
 * post; a drop posts the file grid's `move` with the file's version and the file it now stands before, none when it
 * stands last, and the page is asked again; a move that fails puts the file back and is reported. The viewer opened
 * here offers Info, the name and the description saved through `set-info` where the upload is offered, and the
 * file's metadata where the model carries `canReadMetadata`.
 * Upload and delete need Edit on the file grid's feature and an editable form; a record not saved yet has no
 * files, and the upload it allows shows disabled with the hint to save first. The page is asked again as well when the
 * record is saved or reloaded and when a file is deleted, so the signed links it holds are fresh ones.
 */
@Component({
  selector: 'ui-form-files',
  imports: [BtnComponent, FileReorderDirective, FileTileComponent, FileZoneDirective],
  providers: [FileUploadQueue],
  template: `
    <section
      class="flex flex-col gap-2 min-w-0"
      [attr.aria-labelledby]="model()?.label ? labelId : null"
      [attr.aria-busy]="isBusy() || null"
    >
      <div class="flex flex-wrap items-center gap-(--ui-panel-gap) min-h-9">
        @if (model()?.label; as label) {
          <h3 [id]="labelId" class="sr-only">{{ label }}</h3>
        }
        @if (canUpload() && !isFull()) {
          <ui-btn
            [label]="texts.of('ChooseFiles')"
            icon="import"
            color="indigo"
            [disabled]="isNew()"
            [attr.aria-describedby]="isNew() ? hintId : limitsDescription()"
            (click)="choose()"
          />
          @if (canTakePhoto()) {
            <ui-btn
              [label]="texts.of('TakePhoto')"
              icon="photo"
              color="indigo"
              [disabled]="isNew()"
              [attr.aria-describedby]="isNew() ? hintId : limitsDescription()"
              (click)="takePhoto()"
            />
          }
          @if (canTakeVideo()) {
            <ui-btn
              [label]="texts.of('TakeVideo')"
              icon="play"
              color="indigo"
              [disabled]="isNew()"
              [attr.aria-describedby]="isNew() ? hintId : limitsDescription()"
              (click)="takeVideo()"
            />
          }
        }
        @if (canDownloadAll() || canDeleteAll()) {
          <div class="flex items-center gap-(--ui-panel-gap) ml-auto">
            @if (canDownloadAll()) {
              <ui-btn
                [label]="texts.of('DownloadAllFiles')"
                icon="download"
                variant="ghost"
                [loading]="actions.isDownloadingAll()"
                (click)="actions.downloadAll()"
              />
            }
            @if (canDeleteAll()) {
              <ui-btn
                [label]="texts.of('DeleteAllFiles')"
                icon="trash"
                variant="ghost"
                color="red"
                [loading]="actions.isDeletingAll()"
                (click)="actions.removeAll()"
              />
            }
          </div>
        }
      </div>

      @if (isNew()) {
        <p [id]="hintId" class="m-0 text-sm text-muted" data-files-hint>{{ texts.of('FilesSaveFirst') }}</p>
      } @else if (page.error() && !items().length) {
        <div class="flex items-center gap-2">
          <p class="m-0 text-sm text-muted" role="alert">{{ texts.of('RequestFailed') }}</p>
          <ui-btn [label]="texts.of('Retry')" variant="ghost" (click)="reload()" />
        </div>
      } @else if (!page.isLoading() || items().length) {
        <div
          #zoneElement
          [class]="zoneClasses()"
          [uiFileZone]="intake()"
          [attr.tabindex]="intake() ? 0 : null"
          [attr.role]="intake() ? 'group' : null"
          [attr.aria-labelledby]="intake() ? (model()?.label ? labelId : dropId) : null"
          [attr.aria-describedby]="intake() ? zoneDescription() : null"
          (uiFileZoneTaken)="take($event)"
          (keydown.enter)="pickFromZone($event)"
          (keydown.space)="pickFromZone($event)"
          data-files-zone
        >
          @if (items().length) {
            <ul
              role="list"
              [class]="listClasses()"
              [uiFileReorder]="reorderItems()"
              [uiFileReorderDisabled]="!canMove()"
              (uiFileReorderMoved)="actions.move($event)"
            >
              @for (item of items(); track item.id) {
                <li
                  class="min-w-0"
                  data-file
                  [attr.data-status]="item.upload?.status ?? null"
                  [attr.data-reorder-item]="item.row ? item.file.key : null"
                >
                  <ui-file-tile
                    [file]="item.file"
                    [look]="look()"
                    [upload]="item.upload"
                    [deletable]="canDelete()"
                    [deleting]="actions.isDeletingAll() || (item.file.key !== null && actions.deleting() === item.file.key)"
                    [coverable]="canSetCover() && !!item.row"
                    [isCover]="item.row?.['isCover'] === true"
                    [settingCover]="item.file.key !== null && actions.settingCover() === item.file.key"
                    cancelable
                    [movable]="canMove() && !!item.row"
                    [moving]="item.file.key !== null && reorder()?.movingKey() === item.file.key"
                    [moveHint]="moveHintId"
                    (opened)="open(item.file)"
                    (coverRequested)="actions.setCover(item)"
                    (deleteRequested)="actions.remove(item)"
                    (retryRequested)="retry(item)"
                    (removeRequested)="forget(item)"
                    (cancelRequested)="cancel(item)"
                  />
                </li>
              }
            </ul>
          } @else {
            <p class="m-0 text-sm text-muted" data-files-empty>{{ texts.of('NoFiles') }}</p>
          }
          @if (intake()) {
            <p [id]="dropId" class="m-0 text-sm text-muted text-center" data-files-drop>{{ texts.of('FilesZoneHint') }}</p>
          }
        </div>
        @if (canReorder()) {
          <p [id]="moveHintId" class="sr-only">{{ texts.of('MoveFileHint') }}</p>
          <p class="sr-only" role="status" aria-live="polite" aria-atomic="true" data-files-reorder-status>
            {{ reorder()?.announcement() }}
          </p>
        }
        @if (canUpload()) {
          <div class="flex flex-wrap gap-x-3 text-xs text-muted">
            @if (!isFull()) {
              <p [id]="limitsId" class="m-0" data-files-limits>{{ limitsText() }}</p>
            }
            @if (countText(); as count) {
              <p [id]="countId" class="m-0" data-files-count>{{ count }}</p>
            }
          </div>
        }
      }
      @if (intake()) {
        <input
          #picker
          type="file"
          multiple
          class="hidden"
          tabindex="-1"
          aria-hidden="true"
          [accept]="accepts().picker"
          (change)="takePicked(picker)"
        />
        @if (canTakePhoto()) {
          <input
            #camera
            type="file"
            capture="environment"
            class="hidden"
            tabindex="-1"
            aria-hidden="true"
            [accept]="accepts().camera"
            (change)="takePicked(camera)"
          />
        }
        @if (canTakeVideo()) {
          <input
            #videoCamera
            type="file"
            capture="environment"
            class="hidden"
            tabindex="-1"
            aria-hidden="true"
            [accept]="accepts().video"
            (change)="takePicked(videoCamera)"
            data-files-video
          />
        }
      }
    </section>
  `,
  host: { class: 'block min-w-0' },
})
export class FormFilesComponent {
  private static next = 0;

  protected readonly texts = inject(TextsService);
  private readonly queue = inject(FileUploadQueue);
  private readonly form = inject(FormService);
  private readonly overlay = inject(OverlayService);
  private readonly injector = inject(Injector);
  private readonly locale = inject(LocaleService).current;
  private readonly document = inject(DOCUMENT);
  private readonly window = this.document.defaultView;
  private readonly destroyRef = inject(DestroyRef);
  private readonly lifetime = inject(SCREEN_LIFETIME, { optional: true });
  private readonly picker = viewChild<ElementRef<HTMLInputElement>>('picker');
  private readonly camera = viewChild<ElementRef<HTMLInputElement>>('camera');
  private readonly videoCamera = viewChild<ElementRef<HTMLInputElement>>('videoCamera');
  private readonly zoneElement = viewChild<ElementRef<HTMLElement>>('zoneElement');
  private readonly zone = viewChild(FileZoneDirective);
  protected readonly reorder = viewChild(FileReorderDirective);

  readonly model = input<FormFilesModel | null>();

  /** Whether the record is not saved yet: it has no files, and none can be added before it is. */
  protected readonly isNew = computed(() => this.model()?.parentId == null);
  protected readonly canUpload = computed(() => this.model()?.canUpload === true);
  protected readonly canDelete = computed(() => this.model()?.canDelete === true && !this.isNew());
  protected readonly canDeleteAll = computed(() => this.canDelete() && this.rows().length > 0);
  /** "Download all" is offered wherever the element holds a file, to a reader too. */
  protected readonly canDownloadAll = computed(() => !this.isNew() && this.rows().length > 0);
  protected readonly canSetCover = computed(() => this.model()?.canSetCover === true && !this.isNew());
  protected readonly look = computed(() => this.model()?.look ?? 'tiles');

  /** Whether the files carry their handles: where the upload is offered, on a saved record. */
  protected readonly canReorder = computed(() => this.canUpload() && !this.isNew());

  /** Whether a handle moves its file now: not while files post, a move posts or every file is being deleted. */
  protected readonly canMove: Signal<boolean> = computed(
    () => this.canReorder() && !this.queue.isPosting() && !this.actions.isMovingFile() && !this.actions.isDeletingAll(),
  );

  /** Whether the pointer is coarse, a finger's: a phone or a tablet, whose camera "Take photo" asks. */
  private readonly isCoarsePointer = typeof this.window?.matchMedia === 'function' && this.window.matchMedia('(pointer: coarse)').matches;

  /** "Take photo" is offered on a coarse pointer when the upload accepts a raster image and the record has room. */
  protected readonly canTakePhoto = computed(
    () => this.isCoarsePointer && this.canUpload() && !this.isFull() && this.accepts().camera !== '',
  );

  /** "Take video" is offered on a coarse pointer when the upload accepts a video and the record has room. */
  protected readonly canTakeVideo = computed(
    () => this.isCoarsePointer && this.canUpload() && !this.isFull() && this.accepts().video !== '',
  );

  /** Where the files go: the file grid under this record, within its limits; null while nothing may be uploaded. */
  protected readonly target = computed<FileUploadTarget | null>(() =>
    filesTargetOf(this.model(), this.form.model()?.mutationTexts.committedWarningTitle),
  );

  /** What the browser's picker, the camera's photo and its video take (`filesAcceptsOf`). */
  protected readonly accepts = computed(() => filesAcceptsOf(this.model()?.limits));

  /** The upload's limits, shown before any pick: `PNG, JPG, PDF - up to 10 MB`. */
  protected readonly limitsText = computed(() => {
    const model = this.model();
    return model ? filesLimitsText(this.texts, model.limits, this.locale()) : '';
  });

  /**
   * The page of the record's files, asked while the screen's lifetime holds, and again on its every epoch, on another
   * record or version of it, and on another file grid; a new record asks nothing.
   */
  protected readonly page = httpResource<AnswerOf<FileGridServiceEndpoints, 'get-data'>>(() => {
    const model = this.model();
    this.lifetime?.epoch();
    this.form.ver();
    if (!model || model.parentId == null || (this.lifetime && !this.lifetime.isCurrent())) {
      return undefined;
    }
    const body: GridRequest = { parentService: model.parentService, parentId: model.parentId, isDataOnly: true, page: 1 };
    return { url: serviceUrl<FileGridServiceEndpoints>(model.service, 'get-data'), method: 'POST', body, ...shownByCaller() };
  });

  /** The rows of the page that carry a key, with the files they are. */
  private readonly rows = computed(() => fileRowsOf(this.page.hasValue() ? this.page.value()?.data : null));

  /** The cover, the order, the delete and the archive of the files. */
  protected readonly actions = new FormFilesActions({
    model: () => this.model(),
    isNew: () => this.isNew(),
    rows: () => this.rows(),
    canMove: () => this.canMove(),
    reload: () => this.page.reload(),
  });

  /** The files of the page in the order shown: as a move placed them while its page is asked, else the page's. */
  private readonly shownRows = computed(() => inPlacedOrder(this.rows(), this.actions.placed()));

  /** The files the handles move, in the order shown. */
  protected readonly reorderItems = computed<readonly FileReorderItem[]>(() =>
    this.shownRows().flatMap(({ file }) => (file.key === null ? [] : [{ key: file.key, name: file.name }])),
  );

  /** Whether an uploaded file still shows and takes the record's room: while the queue posts or the page is asked again. */
  private readonly isShowingUploaded = computed(() => this.queue.isPosting() || this.page.isLoading());

  /** The files of the upload queue going under this record, until the page shows them (`uploadsShownOf`). */
  private readonly uploads = computed(() => uploadsShownOf(this.queue.uploads(), this.target(), this.rows(), this.isShowingUploaded()));

  /**
   * The files on their way first, in the order given, as the server places them, then the files of the page, a file
   * being moved where it stands now.
   */
  protected readonly items = computed<readonly FileItem[]>(() => fileItemsOf(this.uploads(), this.shownRows(), this.reorder()?.order()));

  /** The files and bytes the record holds, by what the element knows (`filesHeldOf`). */
  private readonly held = computed(() => filesHeldOf(this.rows(), this.queue.uploads(), this.model() ?? null, this.isShowingUploaded()));

  /** Whether the record holds `maxFiles` files or `maxTotalBytes`: no file enters it then. */
  protected readonly isFull = computed(() => {
    const model = this.model();
    return !!model && isFilesFull(model.limits, this.held());
  });

  /** Where files entering the element go: the target while the record has room, else none. */
  protected readonly intake = computed(() => (this.isFull() ? null : this.target()));

  /** The count of the record's files against `maxFiles` (`7 of 50`), naming the record full when it is. */
  protected readonly countText = computed(() => {
    const model = this.model();
    return !model || this.isNew() ? '' : filesCountText(this.texts, model.limits, this.held(), this.isFull());
  });

  protected readonly isBusy = computed(() => this.page.isLoading() || this.actions.isBusy());

  /**
   * What the file viewer walks: the files of the page in the order shown, their delete where the element offers it, the
   * page asked again; Info's fields where the upload is offered, and the metadata where the model carries its right.
   */
  private readonly viewerSource: FileViewerSource = {
    files: computed(() => this.shownRows().map(({ file }) => file)),
    canDelete: this.canDelete,
    isRefreshing: computed(() => this.page.isLoading()),
    delete: file => this.actions.removeFile(file),
    refresh: () => this.page.reload(),
    ...fileInfoSourceOf(this.injector, {
      place: () => {
        const model = this.model();
        return model && model.parentId != null ? model : null;
      },
      canEditInfo: this.canReorder,
      canReadMetadata: computed(() => this.model()?.canReadMetadata === true && !this.isNew()),
      conflictTitle: () => this.form.model()?.mutationTexts.conflictTitle,
      capture: () => this.actions.capture(),
      saved: () => this.page.reload(),
    }),
  };

  protected readonly listClasses = computed(() => FILES_LIST_CLASSES[this.look()]);

  protected readonly zoneClasses = computed(() => filesZoneClasses(!!this.intake(), this.zone()?.isDragging() === true));

  protected readonly labelId = `ui-form-files-${++FormFilesComponent.next}`;
  protected readonly hintId = `${this.labelId}-hint`;
  protected readonly dropId = `${this.labelId}-drop`;
  protected readonly limitsId = `${this.labelId}-limits`;
  protected readonly countId = `${this.labelId}-count`;
  protected readonly moveHintId = `${this.labelId}-move`;

  /** What describes the pickers: the limits, and the count where there is one. */
  protected readonly limitsDescription = computed(() => (this.countText() ? `${this.limitsId} ${this.countId}` : this.limitsId));

  /** What describes the zone: the drop hint, when the heading names the zone, the limits and the count. */
  protected readonly zoneDescription = computed(() =>
    this.model()?.label ? `${this.dropId} ${this.limitsDescription()}` : this.limitsDescription(),
  );

  constructor() {
    // The queue has no post left: the page shows what it committed
    this.queue.onDrained(() => this.page.reload());
    // The files of the page take the record's room
    this.queue.registerShownFiles(() =>
      this.rows().flatMap(({ file }) => (file.key === null ? [] : [{ id: file.key, size: file.size ?? 0 }])),
    );
    // A file on its way holds the form in an overlay, the routed screen and the page
    holdLeaveWhileUploading(this.queue);
  }

  protected reload(): void {
    this.page.reload();
  }

  /** Opens the browser's picker for several files. */
  protected choose(): void {
    this.picker()?.nativeElement.click();
  }

  /** Opens the browser's picker asking the camera. */
  protected takePhoto(): void {
    this.camera()?.nativeElement.click();
  }

  /** Opens the browser's picker asking the camera to record. */
  protected takeVideo(): void {
    this.videoCamera()?.nativeElement.click();
  }

  /** Enter or Space on the zone itself, not on a file inside it, opens the picker. */
  protected pickFromZone(event: Event): void {
    if (event.target !== this.zoneElement()?.nativeElement || !this.intake()) {
      return;
    }
    event.preventDefault();
    this.choose();
  }

  protected takePicked(input: HTMLInputElement): void {
    const files = [...(input.files ?? [])];
    // Emptied, so picking the same files again is a change again
    input.value = '';
    this.take(files);
  }

  /**
   * Gives the files to the queue, for this record, while it has room. The uploaded files of an earlier batch are
   * forgotten once the page answered after them; while it is asked again they still take the record's room.
   */
  protected take(files: readonly File[]): void {
    const target = this.intake();
    if (!target || files.length === 0) {
      return;
    }
    if (!this.queue.isPosting() && !this.page.isLoading()) {
      this.queue.clearUploaded();
    }
    this.queue.add(files, target);
  }

  /**
   * Opens a file of the page, the one place a tile opens one: the file viewer at it over the element's files. The
   * overlay gives the focus back to the tile that opened it; when that tile is gone, deleted from the viewer, the focus
   * goes to the first file left, else to the zone.
   */
  protected open(file: FileView): void {
    if (file.key === null) {
      return;
    }
    const ref = openFileViewer(this.overlay, this.injector, this.viewerSource, file.key);
    ref.closed.subscribe(() => {
      if (this.destroyRef.destroyed) {
        return;
      }
      afterNextRender(() => focusAfterViewer(this.zoneElement()?.nativeElement), { injector: this.injector });
    });
  }

  protected retry(item: FileItem): void {
    if (item.upload) {
      this.queue.retry(item.upload.id);
    }
  }

  protected forget(item: FileItem): void {
    if (item.upload) {
      this.queue.remove(item.upload.id);
    }
  }

  /** Cancels a file on its way: it leaves the queue, and its post, when one is under way, is aborted. */
  protected cancel(item: FileItem): void {
    if (item.upload) {
      this.queue.cancel(item.upload.id);
    }
  }
}
