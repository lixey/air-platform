import { Component, type ElementRef, input, viewChild } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { FormFilesModel } from '../../core/generated/contracts';
import { FileReorderDirective } from '../../file/file-reorder.directive';
import { FileTileComponent } from '../../file/file-tile.component';
import { FileUploadQueue } from '../../file/file-upload-queue';
import type { FileView } from '../../file/file-view';
import { FileZoneDirective } from '../../file/file-zone.directive';
import type { FileItem } from './form-files';
import { FormFilesState } from './form-files-state';

/**
 * The files of the form's record, drawn in the form's body after the elements of its tab: one page of the file grid's
 * data under the form's record, each file a tile (`FileTile`) in the look the model names, tiles or a list, which opens
 * the file viewer (`FileViewerOverlay`) at it over the element's files, downloads it and, where the model allows,
 * deletes it after a confirmation.
 */
@Component({
  selector: 'ui-form-files',
  imports: [BtnComponent, FileReorderDirective, FileTileComponent, FileZoneDirective],
  providers: [FileUploadQueue],
  template: `
    <section
      class="flex flex-col gap-2 min-w-0"
      [attr.aria-labelledby]="model()?.label ? state.labelId : null"
      [attr.aria-busy]="state.isBusy() || null"
    >
      <div class="flex flex-wrap items-center gap-(--ui-panel-gap) min-h-9">
        @if (model()?.label; as label) {
          <h3 [id]="state.labelId" class="sr-only">{{ label }}</h3>
        }
        @if (state.canUpload() && !state.isFull()) {
          <ui-btn
            [label]="texts.of('ChooseFiles')"
            icon="import"
            color="indigo"
            [disabled]="state.isNew()"
            [attr.aria-describedby]="state.isNew() ? state.hintId : state.limitsDescription()"
            (click)="choose()"
          />
          @if (state.canTakePhoto()) {
            <ui-btn
              [label]="texts.of('TakePhoto')"
              icon="photo"
              color="indigo"
              [disabled]="state.isNew()"
              [attr.aria-describedby]="state.isNew() ? state.hintId : state.limitsDescription()"
              (click)="takePhoto()"
            />
          }
          @if (state.canTakeVideo()) {
            <ui-btn
              [label]="texts.of('TakeVideo')"
              icon="play"
              color="indigo"
              [disabled]="state.isNew()"
              [attr.aria-describedby]="state.isNew() ? state.hintId : state.limitsDescription()"
              (click)="takeVideo()"
            />
          }
        }
        @if (state.canDownloadAll() || state.canDeleteAll()) {
          <div class="flex items-center gap-(--ui-panel-gap) ml-auto">
            @if (state.canDownloadAll()) {
              <ui-btn
                [label]="texts.of('DownloadAllFiles')"
                icon="download"
                variant="ghost"
                [loading]="actions.isDownloadingAll()"
                (click)="actions.downloadAll()"
              />
            }
            @if (state.canDeleteAll()) {
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

      @if (state.isNew()) {
        <p [id]="state.hintId" class="m-0 text-sm text-muted" data-files-hint>{{ texts.of('FilesSaveFirst') }}</p>
      } @else if (state.page.error() && !state.items().length) {
        <div class="flex items-center gap-2">
          <p class="m-0 text-sm text-muted" role="alert">{{ texts.of('RequestFailed') }}</p>
          <ui-btn [label]="texts.of('Retry')" variant="ghost" (click)="reload()" />
        </div>
      } @else if (!state.page.isLoading() || state.items().length) {
        <div
          #zoneElement
          [class]="state.zoneClasses()"
          [uiFileZone]="state.intake()"
          [attr.tabindex]="state.intake() ? 0 : null"
          [attr.role]="state.intake() ? 'group' : null"
          [attr.aria-labelledby]="state.intake() ? (model()?.label ? state.labelId : state.dropId) : null"
          [attr.aria-describedby]="state.intake() ? state.zoneDescription() : null"
          (uiFileZoneTaken)="take($event)"
          (keydown.enter)="pickFromZone($event)"
          (keydown.space)="pickFromZone($event)"
          data-files-zone
        >
          @if (state.items().length) {
            <ul
              role="list"
              [class]="state.listClasses()"
              [uiFileReorder]="state.reorderItems()"
              [uiFileReorderDisabled]="!state.canMove()"
              (uiFileReorderMoved)="actions.move($event)"
            >
              @for (item of state.items(); track item.id) {
                <li
                  class="min-w-0"
                  data-file
                  [attr.data-status]="item.upload?.status ?? null"
                  [attr.data-reorder-item]="item.row ? item.file.key : null"
                >
                  <ui-file-tile
                    [file]="item.file"
                    [look]="state.look()"
                    [upload]="item.upload"
                    [deletable]="state.canDelete()"
                    [deleting]="actions.isDeletingAll() || (item.file.key !== null && actions.deleting() === item.file.key)"
                    [coverable]="state.canSetCover() && !!item.row"
                    [isCover]="item.row?.cells?.['isCover'] === true"
                    [settingCover]="item.file.key !== null && actions.settingCover() === item.file.key"
                    cancelable
                    [movable]="state.canMove() && !!item.row"
                    [moving]="item.file.key !== null && reorder()?.movingKey() === item.file.key"
                    [moveHint]="state.moveHintId"
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
          @if (state.intake()) {
            <p [id]="state.dropId" class="m-0 text-sm text-muted text-center" data-files-drop>{{ texts.of('FilesZoneHint') }}</p>
          }
        </div>
        @if (state.canReorder()) {
          <p [id]="state.moveHintId" class="sr-only">{{ texts.of('MoveFileHint') }}</p>
          <p class="sr-only" role="status" aria-live="polite" aria-atomic="true" data-files-reorder-status>
            {{ reorder()?.announcement() }}
          </p>
        }
        @if (state.canUpload()) {
          <div class="flex flex-wrap gap-x-3 text-xs text-muted">
            @if (!state.isFull()) {
              <p [id]="state.limitsId" class="m-0" data-files-limits>{{ state.limitsText() }}</p>
            }
            @if (state.countText(); as count) {
              <p [id]="state.countId" class="m-0" data-files-count>{{ count }}</p>
            }
          </div>
        }
      }
      @if (state.intake()) {
        <input
          #picker
          type="file"
          multiple
          class="hidden"
          tabindex="-1"
          aria-hidden="true"
          [accept]="state.accepts().picker"
          (change)="takePicked(picker)"
        />
        @if (state.canTakePhoto()) {
          <input
            #camera
            type="file"
            capture="environment"
            class="hidden"
            tabindex="-1"
            aria-hidden="true"
            [accept]="state.accepts().camera"
            (change)="takePicked(camera)"
          />
        }
        @if (state.canTakeVideo()) {
          <input
            #videoCamera
            type="file"
            capture="environment"
            class="hidden"
            tabindex="-1"
            aria-hidden="true"
            [accept]="state.accepts().video"
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
  private readonly picker = viewChild<ElementRef<HTMLInputElement>>('picker');
  private readonly camera = viewChild<ElementRef<HTMLInputElement>>('camera');
  private readonly videoCamera = viewChild<ElementRef<HTMLInputElement>>('videoCamera');
  private readonly zoneElement = viewChild<ElementRef<HTMLElement>>('zoneElement');
  private readonly zone = viewChild(FileZoneDirective);
  protected readonly reorder = viewChild(FileReorderDirective);

  readonly model = input<FormFilesModel | null>();

  protected readonly state = new FormFilesState({
    model: () => this.model(),
    reorderOrder: () => this.reorder()?.order(),
    isDraggingZone: () => this.zone()?.isDragging() === true,
    zoneElement: () => this.zoneElement()?.nativeElement,
  });
  protected readonly texts = this.state.texts;
  protected readonly actions = this.state.actions;

  protected reload(): void {
    this.state.page.reload();
  }

  protected choose(): void {
    this.picker()?.nativeElement.click();
  }

  protected takePhoto(): void {
    this.camera()?.nativeElement.click();
  }

  protected takeVideo(): void {
    this.videoCamera()?.nativeElement.click();
  }

  protected pickFromZone(event: Event): void {
    if (event.target !== this.zoneElement()?.nativeElement || !this.state.intake()) {
      return;
    }
    event.preventDefault();
    this.choose();
  }

  protected takePicked(input: HTMLInputElement): void {
    const files = [...(input.files ?? [])];
    input.value = '';
    this.take(files);
  }

  protected take(files: readonly File[]): void {
    this.state.take(files);
  }

  protected open(file: FileView): void {
    this.state.open(file);
  }

  protected retry(item: FileItem): void {
    if (item.upload) {
      this.state.queue.retry(item.upload.id);
    }
  }

  protected forget(item: FileItem): void {
    if (item.upload) {
      this.state.queue.remove(item.upload.id);
    }
  }

  protected cancel(item: FileItem): void {
    if (item.upload) {
      this.state.queue.cancel(item.upload.id);
    }
  }
}
