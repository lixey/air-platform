import { Component, computed, inject } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import { GridService } from './grid.service';

@Component({
  selector: 'ui-grid-delete-outcome',
  imports: [BtnComponent],
  template: `
    @if (grid.deletion.result(); as result) {
      <section role="status" aria-live="polite" class="max-h-64 overflow-auto border-b border-line p-3 text-sm">
        @if (texts(); as texts) {
          @if (result.outcome === 'unknown') {
            <h3 class="font-semibold">{{ texts.unknownTitle }}</h3>
            <p>{{ texts.unknownDescription }}</p>
          } @else if (result.outcome === 'conflict') {
            <h3 class="font-semibold">{{ texts.conflictTitle }}</h3>
          }
        }
        @if (result.title) {
          <h3 class="font-semibold">{{ result.title }}</h3>
        }
        @if (result.detail) {
          <p>{{ result.detail }}</p>
        }
        <ul class="space-y-2">
          @for (group of grid.deletion.groups(); track $index) {
            <li>
              @if (group.recordId !== null) {
                <h4 class="font-semibold">{{ group.label }} #{{ group.recordId }}</h4>
              }
              <ul>
                @for (message of group.messages; track $index) {
                  <li class="whitespace-pre-wrap break-words">
                    @if (message.code) {
                      <code>{{ message.code }}</code>
                    }
                    @if (message.path) {
                      <code>{{ message.path }}</code>
                    }
                    @if (message.title) {
                      <span>{{ message.title }}</span>
                    }
                    @if (message.detail) {
                      <span>{{ message.detail }}</span>
                    }
                  </li>
                }
              </ul>
            </li>
          }
        </ul>
        @if (texts(); as texts) {
          <div class="flex flex-wrap gap-2 mt-2">
            @if (canCorrect()) {
              <ui-btn [label]="texts.selectionCorrectionLabel" [disabled]="grid.deletion.isBusy()" (click)="grid.deselectFailedRows()" />
            }
            @if (grid.deletion.needsVerification()) {
              <ui-btn
                [label]="result.outcome === 'unknown' ? texts.verifyLabel : texts.reloadLabel"
                [disabled]="grid.deletion.isBusy() || grid.isLoading()"
                [loading]="grid.isLoading()"
                (click)="grid.verifyDeletion()"
              />
            }
          </div>
        }
      </section>
    }
  `,
  host: { class: 'shrink-0 min-h-0' },
})
export class GridDeleteOutcomeComponent {
  protected readonly grid = inject(GridService);
  protected readonly texts = computed(() => this.grid.model()?.mutationTexts);
  protected readonly canCorrect = computed(() => {
    const errors = new Set(this.grid.deletion.errorIds().map(String));
    return this.grid.selection.ids().some(id => errors.has(String(id)));
  });
}
