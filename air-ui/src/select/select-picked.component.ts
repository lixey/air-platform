import { Component, computed, inject } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import { SelectService } from './select.service';

/**
 * The panel of the picked in multiple mode: a strip with the count and a quiet `Clear all`, then the picked newest
 * first, each a small blue chip showing its label, whose remove control stands at its end and that is named for the removal.
 */
@Component({
  selector: 'ui-select-picked',
  imports: [BtnComponent, UiIconDirective],
  template: `
    <div class="flex items-center gap-2 pl-2 border-b border-line bg-surface-2 text-sm">
      <span class="flex-1 text-center text-mark tabular-nums" [attr.aria-label]="texts.of('PickedCount', picked().length)">
        {{ picked().length }}
      </span>
      <ui-btn size="small" variant="ghost" color="slate" [label]="texts.of('ClearAll')" (click)="select.clearDraft()" />
    </div>
    <div class="flex flex-col gap-1 p-1 overflow-auto min-h-0">
      @for (option of picked(); track option.id) {
        <ui-btn
          size="small"
          color="blue"
          [label]="option.label"
          [title]="texts.of('RemoveItem', option.label)"
          [aria-label]="texts.of('RemoveItem', option.label)"
          class="justify-between"
          (click)="select.removeFromDraft(option)"
        >
          <i icon="close" class="shrink-0" aria-hidden="true"></i>
        </ui-btn>
      }
    </div>
  `,
  host: { class: 'flex flex-col min-h-0 w-64 shrink-0 border-l border-line' },
})
export class SelectPickedComponent {
  protected readonly texts = inject(TextsService);
  protected readonly select = inject(SelectService);
  protected readonly picked = computed(() => [...this.select.draft()].reverse());
}
