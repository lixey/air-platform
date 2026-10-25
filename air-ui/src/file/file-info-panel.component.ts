import { Component, computed, DestroyRef, inject, input, linkedSignal, resource, signal, untracked } from '@angular/core';
import { LocaleService } from '../app/locale.service';
import { BtnComponent } from '../btn/btn.component';
import { TextBoxComponent } from '../controls/text-box.component';
import type { FileInfoDto, FileMetadataDto } from '../core/generated/contracts';
import { TextsService, type UiTextKey } from '../core/texts.service';
import { knownZone } from '../core/time-zone';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { FOCUS_RING } from '../theme/css-classes';
import { formatBytes } from './file-size';
import { type FileView, splitName } from './file-view';
import { FILE_VIEWER_SOURCE } from './file-viewer';

/** The longest description `set-info` takes (`data/record-files`). */
const DESCRIPTION_MAX_LENGTH = 2000;

/** One line of a list of facts: what it names and what it says. */
interface Fact {
  readonly label: string;
  readonly value: string;
}

/** A text that ends in `Z` or an offset: an instant, not a wall clock. */
const OFFSET = /(Z|[+-]\d{2}:?\d{2})$/i;

/**
 * A moment as the culture writes its date and time: an instant, a text with `Z` or an offset, on the clock of `zone`,
 * and a camera's local time, a text without one, as written; the text itself where it names none.
 */
function formatMoment(value: string | null | undefined, locale: string, zone: string): string {
  if (!value) {
    return '';
  }
  const isInstant = OFFSET.test(value);
  const moment = new Date(isInstant ? value : `${value}Z`);
  if (Number.isNaN(moment.getTime())) {
    return value;
  }
  // A camera's local time was read as UTC above, so UTC writes it as it reads
  const clock = isInstant ? knownZone(zone) : 'UTC';
  return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short', timeZone: clock }).format(moment);
}

function formatNumber(value: number, locale: string, options: Intl.NumberFormatOptions = {}): string {
  return new Intl.NumberFormat(locale, options).format(value);
}

/**
 * The Info of the file viewer's file (`FileViewerOverlay`): what the opener's file grid answers by `get-info` — the
 * name, the description, the type, the size, the size in pixels, the moment it was added and who uploaded it. Where
 * the opener offers the upload (`canEditInfo`), the name without its extension and the description are fields, saved
 * with the version read through the opener's `saveInfo`, which reports a refusal, a stale save among them, and asks its
 * page again; the Info is read again after a save. Where the opener carries `canReadMetadata`, a section opened on
 * demand asks the file's metadata (`file-metadata`) and lists the camera, the lens, the exposure and the place, as its
 * coordinates; a file without metadata says so. The description shows here alone, never on a tile.
 */
@Component({
  selector: 'ui-file-info-panel',
  imports: [BtnComponent, TextBoxComponent, UiIconDirective],
  template: `
    <h3 [id]="id + '-title'" class="m-0 text-base font-semibold text-ink">{{ texts.of('FileInfo') }}</h3>
    @if (shown(); as info) {
      @if (source.canEditInfo()) {
        <div class="flex flex-col gap-3" data-file-info-form>
          <div class="flex flex-col gap-1">
            <label [for]="id + '-name'" class="text-sm font-medium text-muted">{{ texts.of('FileName') }}</label>
            <div class="flex items-center gap-1 min-w-0">
              <ui-text-box
                class="flex-1 min-w-0"
                [id]="id + '-name'"
                [allowsNull]="false"
                [required]="true"
                [(value)]="name"
                (keydown.enter)="save($event)"
              />
              @if (extension(); as extension) {
                <span class="shrink-0 text-sm text-muted" data-file-info-extension>{{ extension }}</span>
              }
            </div>
          </div>
          <div class="flex flex-col gap-1">
            <label [for]="id + '-description'" class="text-sm font-medium text-muted">{{ texts.of('FileDescription') }}</label>
            <ui-text-box [id]="id + '-description'" kind="textarea" [maxLength]="descriptionMaxLength" [(value)]="description" />
          </div>
          <div class="flex justify-end">
            <ui-btn
              [label]="texts.of('FileInfoSave')"
              variant="solid"
              [disabled]="!canSave()"
              [loading]="isSaving()"
              (click)="save($event)"
              data-file-info-save
            />
          </div>
        </div>
      }
      <dl class="m-0 flex flex-col gap-2 min-w-0" data-file-info-facts>
        @for (fact of facts(); track fact.label) {
          <div class="flex flex-col min-w-0">
            <dt class="text-sm font-medium text-muted">{{ fact.label }}</dt>
            <dd class="m-0 text-sm text-ink break-words whitespace-pre-line">{{ fact.value }}</dd>
          </div>
        }
      </dl>
      @if (source.canReadMetadata()) {
        <div class="flex flex-col gap-2 border-t border-line pt-2">
          <button
            type="button"
            [class]="disclosureClasses"
            [attr.aria-expanded]="isMetadataOpen()"
            [attr.aria-controls]="id + '-metadata'"
            (click)="isMetadataOpen.set(!isMetadataOpen())"
            data-file-metadata-toggle
          >
            <i [icon]="isMetadataOpen() ? 'chevron-bottom' : 'chevron-right'"></i>
            <span>{{ texts.of('FileMetadata') }}</span>
          </button>
          <div [id]="id + '-metadata'" [hidden]="!isMetadataOpen()" data-file-metadata>
            @if (isMetadataOpen()) {
              @if (metadata.error()) {
                <div class="flex items-center gap-2">
                  <p class="m-0 text-sm text-muted" role="alert">{{ texts.of('RequestFailed') }}</p>
                  <ui-btn [label]="texts.of('Retry')" variant="ghost" (click)="metadata.reload()" />
                </div>
              } @else if (metadataFacts().length) {
                <dl class="m-0 flex flex-col gap-2 min-w-0">
                  @for (fact of metadataFacts(); track fact.label) {
                    <div class="flex flex-col min-w-0">
                      <dt class="text-sm font-medium text-muted">{{ fact.label }}</dt>
                      <dd class="m-0 text-sm text-ink break-words" [attr.data-metadata]="fact.label">{{ fact.value }}</dd>
                    </div>
                  }
                </dl>
              } @else if (!metadata.isLoading()) {
                <p class="m-0 text-sm text-muted" data-file-metadata-none>{{ texts.of('FileMetadataNone') }}</p>
              }
            }
          </div>
        </div>
      }
    } @else if (info.error()) {
      <div class="flex items-center gap-2">
        <p class="m-0 text-sm text-muted" role="alert">{{ texts.of('RequestFailed') }}</p>
        <ui-btn [label]="texts.of('Retry')" variant="ghost" (click)="info.reload()" />
      </div>
    }
  `,
  host: { class: 'flex flex-col gap-3 min-w-0 p-3', '[attr.aria-busy]': 'info.isLoading() || isSaving() || null' },
})
export class FileInfoPanelComponent {
  private static next = 0;

  protected readonly texts = inject(TextsService);
  protected readonly source = inject(FILE_VIEWER_SOURCE);
  private readonly locale = inject(LocaleService).current;
  private readonly zone = inject(LocaleService).zone;
  private readonly destroyRef = inject(DestroyRef);

  /** The file the viewer shows. */
  readonly file = input.required<FileView>();

  /** The file's Info, asked again for another file and after a save. */
  protected readonly info = resource({
    params: () => this.file().key ?? undefined,
    loader: () => this.source.info(untracked(this.file)),
  });

  /** The Info read; null while it is asked or when it failed. */
  protected readonly shown = computed<FileInfoDto | null>(() => (this.info.hasValue() ? this.info.value() : null));

  /** The name's base in its field: the Info's, until the user types. */
  protected readonly name = linkedSignal<string | null>(() => splitName(this.shown()?.name ?? '').base);
  /** The description in its field: the Info's, until the user types. */
  protected readonly description = linkedSignal<string | null>(() => this.shown()?.description ?? null);

  protected readonly extension = computed(() => splitName(this.shown()?.name ?? '').extension);
  protected readonly isSaving = signal(false);

  /** Whether the fields say something else than the Info read, with a name to save. */
  protected readonly canSave = computed(() => {
    const info = this.shown();
    const name = (this.name() ?? '').trim();
    if (!info || !name || this.isSaving()) {
      return false;
    }
    return name !== splitName(info.name).base || blankAsNone(this.description()) !== (info.description ?? null);
  });

  /** What the Info says of the file: its name and description as text where they are no fields, then its facts. */
  protected readonly facts = computed<readonly Fact[]>(() => {
    const info = this.shown();
    if (!info) {
      return [];
    }
    const locale = this.locale();
    const facts: Fact[] = [];
    if (!this.source.canEditInfo()) {
      facts.push({ label: this.texts.of('FileName'), value: info.name });
      if (info.description) {
        facts.push({ label: this.texts.of('FileDescription'), value: info.description });
      }
    }
    facts.push({ label: this.texts.of('FileType'), value: info.contentType });
    facts.push({ label: this.texts.of('FileSize'), value: formatBytes(info.size, locale) });
    if (info.width != null && info.height != null) {
      facts.push({
        label: this.texts.of('FileDimensions'),
        value: `${formatNumber(info.width, locale)} × ${formatNumber(info.height, locale)}`,
      });
    }
    facts.push({ label: this.texts.of('FileAdded'), value: formatMoment(info.createdAt, locale, this.zone()) });
    if (info.uploadedBy) {
      facts.push({ label: this.texts.of('FileUploadedBy'), value: info.uploadedBy });
    }
    return facts;
  });

  /** Whether the metadata section is open: its metadata is asked only then. */
  protected readonly isMetadataOpen = signal(false);

  /** The file's metadata, asked when the section opens and for another file while it is open. */
  protected readonly metadata = resource({
    params: () => (this.isMetadataOpen() && this.source.canReadMetadata() ? (this.file().key ?? undefined) : undefined),
    loader: () => this.source.metadata(untracked(this.file)),
  });

  /** The metadata's lines, each only where the file says it; none for a file without metadata. */
  protected readonly metadataFacts = computed<readonly Fact[]>(() =>
    this.metadata.hasValue() ? metadataFactsOf(this.metadata.value(), this.locale(), this.zone(), key => this.texts.of(key)) : [],
  );

  protected readonly id = `ui-file-info-${++FileInfoPanelComponent.next}`;
  protected readonly descriptionMaxLength = DESCRIPTION_MAX_LENGTH;
  protected readonly disclosureClasses = `inline-flex items-center gap-1.5 self-start min-h-9 px-1 rounded-md text-sm font-medium text-ink cursor-pointer hover:bg-hover ${FOCUS_RING}`;

  /**
   * Saves the name's base and the description with the version read, by the
   * button or Enter in the name; a committed save reads the Info again.
   */
  protected async save(event: Event): Promise<void> {
    event.preventDefault();
    const info = this.shown();
    if (!info || !this.canSave()) {
      return;
    }
    this.isSaving.set(true);
    let isSaved: boolean;
    try {
      isSaved = await this.source.saveInfo(this.file(), {
        name: (this.name() ?? '').trim(),
        description: blankAsNone(this.description()),
        ver: info.ver,
      });
    } finally {
      if (!this.destroyRef.destroyed) {
        this.isSaving.set(false);
      }
    }
    if (isSaved && !this.destroyRef.destroyed) {
      this.info.reload();
    }
  }
}

/** A text of spaces alone is none, as the server stores it. */
function blankAsNone(value: string | null): string | null {
  return value?.trim() ? value : null;
}

/** The lines of a file's metadata, in the order a photo is read: camera, lens, moment, exposure, place. */
function metadataFactsOf(
  metadata: FileMetadataDto | null,
  locale: string,
  zone: string,
  textOf: (key: UiTextKey) => string,
): readonly Fact[] {
  if (!metadata) {
    return [];
  }
  const make = metadata.make?.trim() ?? '';
  const model = metadata.model?.trim() ?? '';
  const camera = make && model.toLowerCase().startsWith(make.toLowerCase()) ? model : [make, model].filter(Boolean).join(' ');
  const place =
    metadata.latitude != null && metadata.longitude != null ? `${metadata.latitude.toFixed(6)}, ${metadata.longitude.toFixed(6)}` : '';
  const lines: readonly [UiTextKey, string][] = [
    ['MetadataCamera', camera],
    ['MetadataLens', metadata.lens ?? ''],
    ['MetadataTakenAt', formatMoment(metadata.takenAt, locale, zone)],
    ['MetadataExposure', metadata.exposureTime ? `${metadata.exposureTime} s` : ''],
    ['MetadataAperture', metadata.fNumber != null ? `f/${formatNumber(metadata.fNumber, locale, { maximumFractionDigits: 1 })}` : ''],
    ['MetadataIso', metadata.iso != null ? formatNumber(metadata.iso, locale) : ''],
    [
      'MetadataFocalLength',
      metadata.focalLength != null
        ? formatNumber(metadata.focalLength, locale, { style: 'unit', unit: 'millimeter', maximumFractionDigits: 1 })
        : '',
    ],
    ['MetadataOrientation', metadata.orientation != null ? String(metadata.orientation) : ''],
    ['MetadataSoftware', metadata.software ?? ''],
    ['MetadataPlace', place],
    [
      'MetadataAltitude',
      metadata.altitude != null ? formatNumber(metadata.altitude, locale, { style: 'unit', unit: 'meter', maximumFractionDigits: 1 }) : '',
    ],
  ];
  return lines.filter(([, value]) => value !== '').map(([key, value]) => ({ label: textOf(key), value }));
}
