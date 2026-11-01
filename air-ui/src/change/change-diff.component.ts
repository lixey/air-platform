import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { BadgeComponent } from '../badge/badge.component';
import type { ChangeDiffModel, UiColor } from '../core/generated/contracts';
import { TextsService } from '../core/texts.service';

@Component({
  selector: 'ui-change-diff',
  standalone: true,
  imports: [BadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'block w-full p-3 bg-page overflow-y-auto',
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
          @if (m.operation) {
            <ui-badge [label]="m.operation" [color]="asColor(m.operationColor)" />
          }
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
    }
  `,
})
export class ChangeDiffComponent {
  protected readonly texts = inject(TextsService);

  readonly model = input<ChangeDiffModel | null>();

  protected asColor(color: string | null | undefined): UiColor {
    return (color as UiColor) || 'slate';
  }
}
