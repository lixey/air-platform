import { afterRenderEffect, Component, DestroyRef, ElementRef, inject, Injector, signal, viewChild } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import type { AgentSyncSourceDto, UiIcon } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { ICON_SET } from '../icon/icon-set';
import { UiIconDirective } from '../icon/ui-icon.directive';
import type { SelectOption } from '../select/select-option';
import { SelectService } from '../select/select.service';
import { fieldClasses } from '../theme/css-classes';
import { fitPromptToText } from './agent-ui-helpers';
import { AgentService } from './agent.service';

@Component({
  selector: 'ui-agent-composer',
  imports: [BtnComponent, UiIconDirective],
  providers: [SelectService],
  template: `
    @if (agent.pendingSyncSource() || agent.pendingAttachments().length > 0) {
      <div class="mb-2 flex flex-wrap items-center gap-1.5">
        @if (agent.pendingSyncSource(); as syncSrc) {
          <div
            class="flex max-w-full items-center gap-1.5 rounded-md border border-mark/30 bg-surface-2 px-2.5 py-1 text-xs font-medium text-ink"
          >
            <i class="flex h-3.5 w-3.5 shrink-0 items-center justify-center text-mark" [icon]="syncSourceIcon(syncSrc)"></i>
            <span class="truncate">{{ syncSrc.label }}</span>
            @if (agent.pendingSyncFile(); as syncFile) {
              <span class="max-w-36 truncate text-muted" [title]="syncFile.fileName">· {{ syncFile.fileName }}</span>
            }
            <button
              type="button"
              class="ml-0.5 flex h-4 w-4 shrink-0 cursor-pointer items-center justify-center rounded-full text-muted transition-colors hover:bg-red-500 hover:text-white"
              [title]="texts.of('Delete')"
              [attr.aria-label]="texts.of('Delete')"
              (click)="agent.clearPendingSync()"
            >
              <i class="flex items-center justify-center" [icon]="'close'"></i>
            </button>
          </div>
        }

        @for (att of agent.pendingAttachments(); track att.id) {
          @if (isImageAttachment(att.contentType)) {
            <div class="relative overflow-hidden rounded-md border border-line bg-surface">
              <img [src]="att.previewUrl" [alt]="att.fileName" class="h-14 w-14 object-cover" />
              <button
                type="button"
                class="absolute top-0.5 right-0.5 flex h-4 w-4 cursor-pointer items-center justify-center rounded-full bg-surface/90 text-ink hover:bg-red-500 hover:text-white"
                [title]="texts.of('Delete')"
                [attr.aria-label]="texts.of('Delete')"
                (click)="agent.removePendingAttachment(att.id)"
              >
                <i class="flex items-center justify-center" [icon]="'close'"></i>
              </button>
            </div>
          } @else {
            <div class="flex items-center gap-1.5 rounded-md border border-line bg-surface-2 px-2.5 py-1 text-xs font-medium text-ink">
              <i class="flex h-3.5 w-3.5 shrink-0 items-center justify-center text-muted" [icon]="'import'"></i>
              <span class="max-w-40 truncate" [title]="att.fileName">{{ att.fileName }}</span>
              <button
                type="button"
                class="flex h-4 w-4 shrink-0 cursor-pointer items-center justify-center rounded-full text-muted hover:bg-red-500 hover:text-white"
                [title]="texts.of('Delete')"
                [attr.aria-label]="texts.of('Delete')"
                (click)="agent.removePendingAttachment(att.id)"
              >
                <i class="flex items-center justify-center" [icon]="'close'"></i>
              </button>
            </div>
          }
        }
      </div>
    }

    <input #fileInput type="file" accept="image/*" multiple class="hidden" (change)="onFilesPicked($event)" />
    <input
      #syncFileInput
      type="file"
      [accept]="pendingFileSyncSource()?.fileAccept || '.xlsx,.xls,.csv,.json'"
      class="hidden"
      (change)="onSyncFilePicked($event)"
    />

    <div class="flex items-start gap-1.5">
      <ui-btn
        size="medium"
        variant="soft"
        color="blue"
        icon="plus"
        [title]="agent.syncSources().length > 0 ? texts.of('AgentActionsTitle') : texts.of('AgentAttachTitle')"
        [loading]="agent.isUploading()"
        [disabled]="agent.isStreaming()"
        (click)="onLeftActionClick($event)"
      />

      <textarea
        #promptInput
        rows="1"
        [class]="fieldClasses(false) + ' flex-1 min-h-9 max-h-40 py-1.25 leading-6 resize-none overflow-hidden'"
        [placeholder]="agent.pendingSyncSource() ? texts.of('AgentSyncInputPlaceholder') : texts.of('AgentInputPlaceholder')"
        [value]="draft()"
        (input)="draft.set(promptInput.value)"
        (keydown)="onKeydown($event)"
        (paste)="onPaste($event)"
      ></textarea>

      @if (agent.isStreaming()) {
        <ui-btn size="medium" variant="soft" color="red" icon="close" [title]="texts.of('AgentStopTitle')" (click)="agent.stop()" />
      } @else {
        <ui-btn
          size="medium"
          variant="soft"
          color="blue"
          icon="arrow-right"
          [title]="texts.of('AgentSendTitle')"
          [disabled]="!canSend()"
          (click)="submit()"
        />
      }
    </div>
  `,
  host: { class: 'block shrink-0 border-t border-line bg-surface p-2' },
})
export class AgentComposerComponent {
  protected readonly agent = inject(AgentService);
  protected readonly texts = inject(TextsService);
  private readonly select = inject(SelectService);
  private readonly injector = inject(Injector);

  protected readonly draft = signal('');
  protected readonly fieldClasses = fieldClasses;
  protected readonly pendingFileSyncSource = signal<AgentSyncSourceDto | null>(null);

  private readonly promptInput = viewChild<ElementRef<HTMLTextAreaElement>>('promptInput');
  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');
  private readonly syncFileInput = viewChild<ElementRef<HTMLInputElement>>('syncFileInput');
  private readonly areaWidth = signal(0);

  private readonly watchPromptWidth = afterRenderEffect(onCleanup => {
    const area = this.promptInput()?.nativeElement;
    if (!area || typeof ResizeObserver === 'undefined') {
      return;
    }
    const observer = new ResizeObserver(([entry]) => this.areaWidth.set(entry?.contentRect.width ?? 0));
    observer.observe(area);
    onCleanup(() => observer.disconnect());
  });

  private readonly fitPromptArea = afterRenderEffect({
    write: () => {
      this.draft();
      this.areaWidth();
      const area = this.promptInput()?.nativeElement;
      if (area) {
        fitPromptToText(area);
      }
    },
  });

  constructor() {
    const subscription = this.select.committed.subscribe(options => {
      const picked = options[0];
      if (!picked) {
        return;
      }
      if (picked.id === 0) {
        this.fileInput()?.nativeElement.click();
        return;
      }
      const src = picked.row as AgentSyncSourceDto | undefined;
      if (!src) {
        return;
      }
      if (src.acceptsFile) {
        this.pendingFileSyncSource.set(src);
        queueMicrotask(() => this.syncFileInput()?.nativeElement.click());
      } else {
        this.agent.setPendingSync(src, null);
        queueMicrotask(() => this.promptInput()?.nativeElement.focus());
      }
    });

    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  setDraftAndFocus(text: string): void {
    if (!text) {
      return;
    }
    this.draft.set(text);
    queueMicrotask(() => {
      const area = this.promptInput()?.nativeElement;
      if (area) {
        fitPromptToText(area);
        area.focus();
        area.setSelectionRange(area.value.length, area.value.length);
      }
    });
  }

  protected onLeftActionClick(event: Event): void {
    const sources = this.agent.syncSources();
    if (sources.length === 0) {
      this.fileInput()?.nativeElement.click();
      return;
    }

    const anchor = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
    const options: SelectOption[] = [
      {
        id: 0,
        label: this.texts.of('AgentAttachPhotoOption'),
        icon: 'photo',
      },
      ...sources.map((src, idx) => ({
        id: idx + 1,
        label: src.label,
        subtitle: src.subtitle ?? undefined,
        icon: this.syncSourceIcon(src),
        row: src,
      })),
    ];

    this.select.open({ model: { label: this.texts.of('AgentActionsTitle'), options }, picked: [] }, anchor, this.injector);
  }

  protected canSend(): boolean {
    return (
      (this.draft().trim().length > 0 || this.agent.pendingAttachments().length > 0 || !!this.agent.pendingSyncSource()) &&
      !this.agent.isUploading() &&
      !this.agent.isStreaming()
    );
  }

  protected async submit(): Promise<void> {
    if (!this.canSend()) {
      return;
    }
    const text = this.draft();
    this.draft.set('');
    const syncSrc = this.agent.pendingSyncSource();
    if (syncSrc) {
      await this.agent.runSync(syncSrc, text);
    } else {
      await this.agent.send(text);
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.submit();
    }
  }

  protected onFilesPicked(event: Event): void {
    const inputEl = event.target as HTMLInputElement;
    if (inputEl.files && inputEl.files.length > 0) {
      void this.agent.uploadFiles(inputEl.files);
      inputEl.value = '';
    }
  }

  protected async onSyncFilePicked(event: Event): Promise<void> {
    const inputEl = event.target as HTMLInputElement;
    const file = inputEl.files?.[0];
    const src = this.pendingFileSyncSource();
    inputEl.value = '';
    this.pendingFileSyncSource.set(null);

    if (!file || !src) {
      return;
    }

    const uploaded = await this.agent.uploadSingleFile(file);
    if (uploaded) {
      this.agent.setPendingSync(src, uploaded);
      queueMicrotask(() => this.promptInput()?.nativeElement.focus());
    }
  }

  protected syncSourceIcon(src: AgentSyncSourceDto): UiIcon {
    if (src.icon && src.icon in ICON_SET) {
      return src.icon as UiIcon;
    }
    return src.acceptsFile ? 'import' : 'reload';
  }

  protected isImageAttachment(contentType: string | null | undefined): boolean {
    return !contentType || contentType.startsWith('image/');
  }

  protected onPaste(event: ClipboardEvent): void {
    const items = event.clipboardData?.items;
    if (!items) {
      return;
    }

    const imageFiles: File[] = [];
    for (let i = 0; i < items.length; i++) {
      const item = items[i];
      if (item.type.startsWith('image/')) {
        const file = item.getAsFile();
        if (file) {
          imageFiles.push(file);
        }
      }
    }

    if (imageFiles.length > 0) {
      event.preventDefault();
      void this.agent.uploadFiles(imageFiles);
    }
  }
}
