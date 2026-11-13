import { Component, inject, input, output } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import { TextsService } from '../core/texts.service';
import { UiIconDirective } from '../icon/ui-icon.directive';
import type { AgentMemoryItem } from './agent-models';

@Component({
  selector: 'ui-agent-memory-pill',
  imports: [BtnComponent, UiIconDirective],
  template: `
    @let m = memory();
    <div
      class="inline-flex max-w-full items-center gap-1.5 rounded-full border border-line bg-surface-2 py-0.5 pr-1 pl-2.5 text-xs text-muted"
      [title]="m.content"
    >
      <i class="flex h-3.5 w-3.5 shrink-0 items-center justify-center text-mark" [icon]="'pin'"></i>
      @if (m.status === 'pending') {
        @if (m.dismissed) {
          <span class="truncate opacity-65">{{ texts.of('AgentMemoryDismissed') }}: {{ m.title }}</span>
        } @else {
          <span class="truncate text-ink">{{ texts.of('AgentMemoryPending', m.title) }}</span>
          <ui-btn
            size="small"
            variant="solid"
            color="green"
            [label]="texts.of('AgentMemoryConfirm')"
            [loading]="!!m.confirming"
            (click)="confirm.emit(m.pendingToken!)"
          />
          <ui-btn
            size="small"
            variant="ghost"
            [label]="texts.of('AgentMemoryDismiss')"
            [disabled]="!!m.confirming"
            (click)="dismiss.emit(m.pendingToken!)"
          />
        }
      } @else if (m.undone) {
        <span class="truncate line-through opacity-65">{{ texts.of('AgentMemoryUndone') }}: {{ m.title }}</span>
      } @else {
        <span class="truncate">{{ texts.of('AgentMemorySaved', m.title) }}</span>
        <ui-btn size="small" variant="ghost" [label]="texts.of('AgentMemoryUndo')" [loading]="!!m.undoing" (click)="undo.emit(m.id)" />
      }
    </div>
  `,
  host: { class: 'block max-w-full min-w-0' },
})
export class AgentMemoryPillComponent {
  protected readonly texts = inject(TextsService);

  readonly memory = input.required<AgentMemoryItem>();
  readonly confirm = output<string>();
  readonly dismiss = output<string>();
  readonly undo = output<number>();
}
