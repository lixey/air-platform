import { httpResource } from '@angular/common/http';
import { afterNextRender, computed, DestroyRef, DOCUMENT, inject, Injector, type Signal } from '@angular/core';
import { shownByCaller } from '../../app/alert/error-alert.interceptor';
import { LocaleService } from '../../app/locale.service';
import { type AnswerOf, serviceUrl } from '../../core/api';
import type { FileGridServiceEndpoints, FormFilesModel, GridRequest } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import type { FileReorderItem } from '../../file/file-reorder.directive';
import { FileUploadQueue, type FileUploadTarget } from '../../file/file-upload-queue';
import type { FileView } from '../../file/file-view';
import { fileInfoSourceOf, type FileViewerSource, openFileViewer } from '../../file/file-viewer';
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

/** Inputs passed by `FormFilesComponent` to its reactive state collaborator. */
export interface FormFilesStateHost {
  readonly model: () => FormFilesModel | null | undefined;
  readonly reorderOrder: () => readonly FileReorderItem[] | null | undefined;
  readonly isDraggingZone: () => boolean;
  readonly zoneElement: () => HTMLElement | undefined;
}

/**
 * Owns the reactive page resource, upload queue integration, file reorder state, accessibility ids, and file viewer
 * source for `FormFilesComponent`. Constructed in the component's injection context.
 */
export class FormFilesState {
  private static next = 0;

  readonly texts = inject(TextsService);
  readonly queue = inject(FileUploadQueue);
  private readonly form = inject(FormService);
  private readonly overlay = inject(OverlayService);
  private readonly injector = inject(Injector);
  private readonly locale = inject(LocaleService).current;
  private readonly window = inject(DOCUMENT).defaultView;
  private readonly destroyRef = inject(DestroyRef);
  private readonly lifetime = inject(SCREEN_LIFETIME, { optional: true });

  private readonly isCoarsePointer = typeof this.window?.matchMedia === 'function' && this.window.matchMedia('(pointer: coarse)').matches;

  readonly isNew = computed(() => this.host.model()?.parentId == null);
  readonly canUpload = computed(() => this.host.model()?.canUpload === true);
  readonly canDelete = computed(() => this.host.model()?.canDelete === true && !this.isNew());
  readonly canSetCover = computed(() => this.host.model()?.canSetCover === true && !this.isNew());
  readonly look = computed(() => this.host.model()?.look ?? 'tiles');
  readonly canReorder = computed(() => this.canUpload() && !this.isNew());
  readonly accepts = computed(() => filesAcceptsOf(this.host.model()?.limits));

  readonly target = computed<FileUploadTarget | null>(() =>
    filesTargetOf(this.host.model(), this.form.model()?.mutationTexts.committedWarningTitle),
  );

  readonly limitsText = computed(() => {
    const model = this.host.model();
    return model ? filesLimitsText(this.texts, model.limits, this.locale()) : '';
  });

  readonly page = httpResource<AnswerOf<FileGridServiceEndpoints, 'get-data'>>(() => {
    const model = this.host.model();
    this.lifetime?.epoch();
    this.form.ver();
    if (!model || model.parentId == null || (this.lifetime && !this.lifetime.isCurrent())) {
      return undefined;
    }
    const body: GridRequest = { parentService: model.parentService, parentId: model.parentId, isDataOnly: true, page: 1 };
    return { url: serviceUrl<FileGridServiceEndpoints>(model.service, 'get-data'), method: 'POST', body, ...shownByCaller() };
  });

  private readonly rows = computed(() => fileRowsOf(this.page.hasValue() ? this.page.value()?.data : null));
  readonly canDeleteAll = computed(() => this.canDelete() && this.rows().length > 0);
  readonly canDownloadAll = computed(() => !this.isNew() && this.rows().length > 0);

  readonly actions: FormFilesActions = new FormFilesActions({
    model: () => this.host.model(),
    isNew: () => this.isNew(),
    rows: () => this.rows(),
    canMove: () => this.canMove(),
    reload: () => this.page.reload(),
  });

  readonly canMove: Signal<boolean> = computed(
    () => this.canReorder() && !this.queue.isPosting() && !this.actions.isMovingFile() && !this.actions.isDeletingAll(),
  );

  private readonly shownRows = computed(() => inPlacedOrder(this.rows(), this.actions.placed()));

  readonly reorderItems = computed<readonly FileReorderItem[]>(() =>
    this.shownRows().flatMap(({ file }) => (file.key === null ? [] : [{ key: file.key, name: file.name }])),
  );

  private readonly isShowingUploaded = computed(() => this.queue.isPosting() || this.page.isLoading());
  private readonly uploads = computed(() => uploadsShownOf(this.queue.uploads(), this.target(), this.rows(), this.isShowingUploaded()));

  readonly items = computed<readonly FileItem[]>(() =>
    fileItemsOf(this.uploads(), this.shownRows(), this.host.reorderOrder() ?? undefined),
  );

  private readonly held = computed(() =>
    filesHeldOf(this.rows(), this.queue.uploads(), this.host.model() ?? null, this.isShowingUploaded()),
  );

  readonly isFull = computed(() => {
    const model = this.host.model();
    return !!model && isFilesFull(model.limits, this.held());
  });

  readonly canTakePhoto = computed(() => this.isCoarsePointer && this.canUpload() && !this.isFull() && this.accepts().camera !== '');
  readonly canTakeVideo = computed(() => this.isCoarsePointer && this.canUpload() && !this.isFull() && this.accepts().video !== '');
  readonly intake = computed(() => (this.isFull() ? null : this.target()));

  readonly countText = computed(() => {
    const model = this.host.model();
    return !model || this.isNew() ? '' : filesCountText(this.texts, model.limits, this.held(), this.isFull());
  });

  readonly isBusy = computed(() => this.page.isLoading() || this.actions.isBusy());
  readonly listClasses = computed(() => FILES_LIST_CLASSES[this.look()]);
  readonly zoneClasses = computed(() => filesZoneClasses(!!this.intake(), this.host.isDraggingZone()));

  readonly labelId = `ui-form-files-${++FormFilesState.next}`;
  readonly hintId = `${this.labelId}-hint`;
  readonly dropId = `${this.labelId}-drop`;
  readonly limitsId = `${this.labelId}-limits`;
  readonly countId = `${this.labelId}-count`;
  readonly moveHintId = `${this.labelId}-move`;

  readonly limitsDescription = computed(() => (this.countText() ? `${this.limitsId} ${this.countId}` : this.limitsId));
  readonly zoneDescription = computed(() =>
    this.host.model()?.label ? `${this.dropId} ${this.limitsDescription()}` : this.limitsDescription(),
  );

  private readonly viewerSource: FileViewerSource;

  constructor(private readonly host: FormFilesStateHost) {
    this.viewerSource = {
      files: computed(() => this.shownRows().map(({ file }) => file)),
      canDelete: this.canDelete,
      isRefreshing: computed(() => this.page.isLoading()),
      delete: file => this.actions.removeFile(file),
      refresh: () => this.page.reload(),
      ...fileInfoSourceOf(this.injector, {
        place: () => {
          const model = this.host.model();
          return model && model.parentId != null ? model : null;
        },
        canEditInfo: this.canReorder,
        canReadMetadata: computed(() => this.host.model()?.canReadMetadata === true && !this.isNew()),
        conflictTitle: () => this.form.model()?.mutationTexts.conflictTitle,
        capture: () => this.actions.capture(),
        saved: () => this.page.reload(),
      }),
    };

    this.queue.onDrained(() => this.page.reload());
    this.queue.registerShownFiles(() =>
      this.rows().flatMap(({ file }) => (file.key === null ? [] : [{ id: file.key, size: file.size ?? 0 }])),
    );
    holdLeaveWhileUploading(this.queue);
  }

  take(files: readonly File[]): void {
    const target = this.intake();
    if (!target || files.length === 0) {
      return;
    }
    if (!this.queue.isPosting() && !this.page.isLoading()) {
      this.queue.clearUploaded();
    }
    this.queue.add(files, target);
  }

  open(file: FileView): void {
    if (file.key === null) {
      return;
    }
    const ref = openFileViewer(this.overlay, this.injector, this.viewerSource, file.key);
    ref.closed.subscribe(() => {
      if (this.destroyRef.destroyed) {
        return;
      }
      afterNextRender(() => focusAfterViewer(this.host.zoneElement()), { injector: this.injector });
    });
  }
}
