import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AlertService } from '../app/alert/alert.service';
import { shownByCaller } from '../app/alert/error-alert.interceptor';
import { BadgeComponent } from '../badge/badge.component';
import { BtnComponent } from '../btn/btn.component';
import { serviceUrl } from '../core/api';
import type { ChangeDiffModel, GridActionDto, UiColor } from '../core/generated/contracts';
import { classifyMutationFailure, refusalDetailOf, refusalOf } from '../core/mutation-failure';
import { TextsService } from '../core/texts.service';
import { GridService } from '../grid/grid.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { ConfirmationService } from '../overlay/confirmation.service';
import { OverlayRef } from '../overlay/overlay-ref';

@Component({
  selector: 'ui-change-diff',
  standalone: true,
  imports: [BadgeComponent, BtnComponent, UiIconDirective],
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

        @if (m.canRestore || restored()) {
          <div class="flex items-center justify-end gap-2 border-t border-line bg-surface-3 px-3 py-2">
            @if (restored()) {
              <div class="flex items-center gap-1.5 text-xs font-semibold text-green-600 dark:text-green-400">
                <i class="flex h-4 w-4 items-center justify-center" [icon]="'checkmark'"></i>
                <span>{{ m.restoredLabel || texts.of('Restore') }}</span>
              </div>
            } @else {
              <ui-btn
                size="small"
                variant="soft"
                color="blue"
                icon="reload"
                [label]="m.restoreLabel ?? texts.of('Restore')"
                [loading]="restoring()"
                (click)="restore(m)"
              />
            }
          </div>
        }
      </div>
    }
  `,
})
export class ChangeDiffComponent {
  private readonly http = inject(HttpClient);
  private readonly confirmation = inject(ConfirmationService);
  private readonly alert = inject(AlertService);
  private readonly ref = inject(OverlayRef, { optional: true });
  protected readonly texts = inject(TextsService);

  readonly model = input<ChangeDiffModel | null>();

  protected readonly restoring = signal(false);
  protected readonly restored = signal(false);

  protected asColor(color: string | null | undefined): UiColor {
    return (color as UiColor) || 'slate';
  }

  protected async restore(m: ChangeDiffModel): Promise<void> {
    if (!m.service || this.restoring()) {
      return;
    }

    if (m.restoreConfirm) {
      const confirmed = await this.confirmation.confirm({
        message: m.restoreConfirm,
        color: 'red',
      });
      if (!confirmed) {
        return;
      }
    }

    this.restoring.set(true);
    try {
      const url = `${serviceUrl(m.service as never, 'get-data' as never).replace(/\/get-data$/, '')}/restore`;
      const answer = await firstValueFrom(
        this.http.post<GridActionDto | null>(
          url,
          {
            id: m.id,
            selectedIds: [m.id],
            parentService: m.parentService,
            parentId: m.parentId,
          },
          shownByCaller(),
        ),
      );

      if (answer && answer.outcome && answer.outcome !== 'committed') {
        const failure = classifyMutationFailure(answer);
        this.alert.error(refusalOf(failure, this.texts).title, refusalDetailOf(failure, failure.messages, '\n'));
        return;
      }

      this.restored.set(true);
      GridService.reloadServices();
      await this.ref?.close(true);
    } catch (error: unknown) {
      const failure = classifyMutationFailure(error);
      this.alert.error(refusalOf(failure, this.texts).title, refusalDetailOf(failure, failure.messages, '\n'));
    } finally {
      this.restoring.set(false);
    }
  }
}
