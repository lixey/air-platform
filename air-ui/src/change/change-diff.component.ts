import { ChangeDetectionStrategy, Component, inject, Injector, input } from '@angular/core';
import { BadgeComponent } from '../badge/badge.component';
import { BtnComponent } from '../btn/btn.component';
import type { ChangeDiffModel, ChangeDiffRelatedItemDto, OpenTargetDto, UiColor } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';
import { OverlayService } from '../overlay/overlay.service';
import { canOpenTarget, openRecord } from '../report/report-row-open';
import { ScreenMutations } from '../screen/screen-mutations';

@Component({
  selector: 'ui-change-diff',
  standalone: true,
  imports: [BadgeComponent, BtnComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'flex flex-col gap-3 w-full p-3 bg-page overflow-y-auto',
  },
  template: `
    @if (model(); as m) {
      <div class="w-full overflow-hidden rounded-lg border border-line bg-surface-2 shadow-xs">
        <div class="flex items-center justify-between gap-2 border-b border-line bg-surface-3 px-3 py-2.5">
          <div class="min-w-0 flex-1">
            <div class="truncate text-sm font-semibold text-ink">{{ m.title }}</div>
            @if (m.subtitle) {
              <div class="truncate text-xs text-muted">{{ m.subtitle }}</div>
            }
          </div>
          <div class="flex shrink-0 items-center gap-1.5">
            @if (m.sourceBadge) {
              <ui-badge [label]="m.sourceBadge" [color]="asColor(m.sourceBadgeColor)" />
            }
            @if (m.operation) {
              <ui-badge [label]="m.operation" [color]="asColor(m.operationColor)" />
            }
          </div>
        </div>

        @if (m.fields.length > 0) {
          <div class="divide-y divide-line/60 px-3 py-1.5 text-xs">
            @for (field of m.fields; track field.key) {
              <div class="flex items-start justify-between gap-3 py-1.5">
                <span class="shrink-0 text-muted">{{ field.label }}</span>
                <div class="flex min-w-0 flex-wrap items-center justify-end gap-1.5 text-right">
                  @if (field.isChanged || m.operationKey === 'Modified') {
                    <span class="break-all text-muted line-through">{{ field.before ?? '—' }}</span>
                    <span class="text-muted">→</span>
                    <span class="break-all font-semibold text-ink">{{ field.after ?? '—' }}</span>
                  } @else if (m.operationKey === 'Deleted') {
                    <span class="break-all font-medium text-red-600 dark:text-red-400">{{ field.before ?? '—' }}</span>
                  } @else {
                    <span class="break-all font-medium text-ink">{{ field.after ?? field.before ?? '—' }}</span>
                  }
                </div>
              </div>
            }
          </div>
        } @else {
          <div class="px-3 py-4 text-center text-xs text-muted">
            {{ m.emptyLabel || texts.of('NoRows') }}
          </div>
        }
      </div>

      @if (m.relatedChanges.length > 1) {
        <div class="w-full overflow-hidden rounded-lg border border-line bg-surface-2 shadow-xs">
          <div class="border-b border-line bg-surface-3 px-3 py-2 text-xs font-semibold text-ink">
            {{ m.relatedTitle || texts.of('ChangeRelatedTitle', m.relatedChanges.length) }}
          </div>
          <div class="divide-y divide-line/60 px-3 py-1 text-xs">
            @for (item of m.relatedChanges; track item.id) {
              <div class="flex items-center justify-between gap-2 py-1.5" [class.font-semibold]="item.isCurrent">
                <div class="min-w-0 flex-1 truncate text-ink" [title]="item.title">
                  {{ item.title }}
                </div>
                <div class="flex shrink-0 items-center gap-1.5">
                  <ui-badge [label]="item.operation" [color]="asColor(item.operationColor)" />
                  @if (canOpen(item.open)) {
                    <ui-btn
                      size="small"
                      variant="ghost"
                      color="blue"
                      icon="external"
                      [label]="texts.of('Open')"
                      (click)="openRelatedRecord(item, $event)"
                    />
                  }
                </div>
              </div>
            }
          </div>
        </div>
      }
    }
  `,
})
export class ChangeDiffComponent {
  protected readonly texts = inject(TextsService);
  private readonly overlay = inject(OverlayService);
  private readonly injector = inject(Injector);
  private readonly mutations = inject(ScreenMutations, { optional: true });

  readonly model = input<ChangeDiffModel | null>();

  protected asColor(color: string | null | undefined): UiColor {
    return (color as UiColor) || 'slate';
  }

  protected canOpen(target: OpenTargetDto | null | undefined): boolean {
    return canOpenTarget(target);
  }

  protected openRelatedRecord(item: ChangeDiffRelatedItemDto, event: Event): void {
    if (!canOpenTarget(item.open)) {
      return;
    }

    const anchor = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
    const defaultOverlay = {
      key: item.open.service,
      presentation: 'drawer' as const,
      width: '720px',
      closesOnEscape: true,
    };

    openRecord({ ...item.open, overlay: item.open.overlay ?? defaultOverlay }, this.overlay, anchor, this.injector, this.mutations);
  }
}
