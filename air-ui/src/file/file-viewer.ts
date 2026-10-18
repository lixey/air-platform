import { InjectionToken, Injector, type Signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AlertService } from '../app/alert/alert.service';
import { shownByCaller } from '../app/alert/error-alert.interceptor';
import { Api } from '../core/api';
import type {
  FileGridServiceEndpoints,
  FileInfoDto,
  FileInfoRequest,
  FileMetadataDto,
  GridRequest,
  OverlayModelDto,
} from '../core/generated/contracts';
import type { IsCurrent } from '../core/lifetime';
import { TextsService } from '../core/texts.service';
import type { GridRowKey } from '../grid/grid-row';
import type { OverlayRef } from '../overlay/overlay-ref';
import type { OverlayService } from '../overlay/overlay.service';
import type { FileViewerOverlayModel } from '../ui-components';
import { isCommitted, reportFileRefusal } from './file-actions';
import type { FileView } from './file-view';

/** The key of the file viewer's overlay. */
export const FILE_VIEWER_OVERLAY = 'file.viewer';

/** What Info saves of a file: the name without its extension and the description, with the version it was read at. */
export interface FileInfoChange {
  readonly name: string;
  readonly description: string | null;
  readonly ver: number;
}

/**
 * What the viewer's Info asks of its opener: whether the name and the description are fields and whether the metadata
 * shows, and the requests of the file grid that answer them.
 */
export interface FileInfoSource {
  /** Whether the opener offers the files' upload: Info's name and description are fields saved through `saveInfo`. */
  readonly canEditInfo: Signal<boolean>;
  /** Whether the opener carries `canReadMetadata`: Info holds the section of the file's metadata. */
  readonly canReadMetadata: Signal<boolean>;
  /** The file as the file grid's `get-info` answers it; rejects when it could not be read, which Info shows. */
  info(file: FileView): Promise<FileInfoDto>;
  /**
   * Saves the name's base and the description through the file grid's `set-info`; resolves whether the save committed.
   * A refusal, a stale version among them, is reported here, and the opener asks its page again either way.
   */
  saveInfo(file: FileView, change: FileInfoChange): Promise<boolean>;
  /** The file's metadata, as the file grid's `file-metadata` answers it; null for a file without any. */
  metadata(file: FileView): Promise<FileMetadataDto | null>;
}

/**
 * What opened the file viewer gives it: the files it walks, whether it offers their delete, and the requests it answers
 * for them. The sequence is live: the viewer follows it as the opener asks its page again.
 */
export interface FileViewerSource extends FileInfoSource {
  /** The files the viewer walks, in their order. */
  readonly files: Signal<readonly FileView[]>;
  /** Whether the opener offers the files' delete. */
  readonly canDelete: Signal<boolean>;
  /** Whether the opener is asking its page again. */
  readonly isRefreshing: Signal<boolean>;
  /** Deletes the file after a confirmation; resolves whether it was deleted. */
  delete(file: FileView): Promise<boolean>;
  /** Asks the page again, for fresh links. */
  refresh(): void;
}

/** Where a record's files stand: the file grid and the record it lists them under. */
export interface FileGridPlace {
  readonly service: string;
  readonly parentService?: string | null;
  readonly parentId?: number | null;
}

/** What an opener says of its files for `fileInfoSourceOf`. */
export interface FileInfoOptions {
  /** The file grid under the record; null where the opener stands in none, and Info cannot be read. */
  readonly place: () => FileGridPlace | null;
  readonly canEditInfo: Signal<boolean>;
  readonly canReadMetadata: Signal<boolean>;
  /** The title a stale save is reported under: the opener's `mutationTexts.conflictTitle`. */
  readonly conflictTitle: () => string | null | undefined;
  /** Takes the opener's currency when a save is asked, so an answer it no longer stands for is dropped. */
  readonly capture?: () => IsCurrent;
  /** Asks the opener's page again, after a save. */
  readonly saved: () => void;
}

/**
 * The Info requests of a file grid under a record: `get-info` and `file-metadata` with the file as `id`, `set-info`
 * with the file and its version as `selectedIds` and `selectedVersions`. A read's failure is the caller's to show; a
 * save's is reported as a refused save is, a stale one under the opener's conflict title.
 */
export function fileInfoSourceOf(injector: Injector, options: FileInfoOptions): FileInfoSource {
  const services: InfoServices = { api: injector.get(Api), alert: injector.get(AlertService), texts: injector.get(TextsService) };
  const { api } = services;
  return {
    canEditInfo: options.canEditInfo,
    canReadMetadata: options.canReadMetadata,
    info: async file => {
      const { service, body } = infoRequestOf(options, file, 'get-info');
      return firstValueFrom(api.postTo<FileGridServiceEndpoints, 'get-info'>(service, 'get-info', body, shownByCaller()));
    },
    metadata: async file => {
      const { service, body } = infoRequestOf(options, file, 'file-metadata');
      return firstValueFrom(api.postTo<FileGridServiceEndpoints, 'file-metadata'>(service, 'file-metadata', body, shownByCaller()));
    },
    saveInfo: (file, change) => saveFileInfo(services, options, file, change),
  };
}

/** What the Info requests are made with: taken once, when the source is created. */
interface InfoServices {
  readonly api: Api;
  readonly alert: AlertService;
  readonly texts: Pick<TextsService, 'of'>;
}

/** The file grid and the body naming the file as `id` under its record; throws where the opener names none. */
function infoRequestOf(options: FileInfoOptions, file: FileView, method: string): { service: string; body: GridRequest } {
  const place = options.place();
  if (!place || file.key === null) {
    throw new Error(`The file viewer's opener names no file grid to ask '${method}' of.`);
  }
  return { service: place.service, body: { parentService: place.parentService, parentId: place.parentId, id: file.key } };
}

/** Saves the name's base and the description through `set-info`; resolves whether the save committed. */
async function saveFileInfo(services: InfoServices, options: FileInfoOptions, file: FileView, change: FileInfoChange): Promise<boolean> {
  const place = options.place();
  if (!place || file.key === null || !options.canEditInfo()) {
    return false;
  }
  const { api, alert, texts } = services;
  const isCurrent = options.capture?.() ?? (() => true);
  const body: FileInfoRequest = {
    parentService: place.parentService,
    parentId: place.parentId,
    selectedIds: [file.key],
    selectedVersions: [change.ver],
    name: change.name,
    description: change.description,
  };
  try {
    const answer = await firstValueFrom(api.postTo<FileGridServiceEndpoints, 'set-info'>(place.service, 'set-info', body, shownByCaller()));
    if (!isCurrent()) {
      return false;
    }
    const hasCommitted = isCommitted(answer);
    if (hasCommitted) {
      alert.warnings(answer.warnings);
    } else {
      reportFileRefusal(alert, texts, answer, options.conflictTitle());
    }
    options.saved();
    return hasCommitted;
  } catch (error: unknown) {
    // The failure is reported here: the global alert was told to stay silent
    if (isCurrent()) {
      reportFileRefusal(alert, texts, error, options.conflictTitle());
      options.saved();
    }
    return false;
  }
}

/** The source the viewer's panel reads, provided by the injector it is opened with. */
export const FILE_VIEWER_SOURCE = new InjectionToken<FileViewerSource>('FILE_VIEWER_SOURCE');

/**
 * The file viewer at the file `key`: a modal dialog over the whole window, its one panel filling the card, which Escape
 * closes.
 */
export function fileViewerOf(key: GridRowKey): OverlayModelDto {
  const model: FileViewerOverlayModel = { kind: 'FileViewerOverlay', key };
  return {
    key: FILE_VIEWER_OVERLAY,
    presentation: 'dialog',
    width: '1fr',
    height: '1fr',
    maxWidth: '3840px',
    layout: { key: FILE_VIEWER_OVERLAY, root: { key: 'root', direction: 'column', children: [{ key: 'body', model }] } },
  };
}

/**
 * Opens the file viewer at the file `key` over the files of `source`, with the opener's injector, so the screen the
 * opener stands in owns it. The overlay hands the focus back to the element that held it when the viewer closes.
 */
export function openFileViewer(overlay: OverlayService, injector: Injector, source: FileViewerSource, key: GridRowKey): OverlayRef {
  const viewerInjector = Injector.create({ providers: [{ provide: FILE_VIEWER_SOURCE, useValue: source }], parent: injector });
  return overlay.open(fileViewerOf(key), null, viewerInjector);
}
