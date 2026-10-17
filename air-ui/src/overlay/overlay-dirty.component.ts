import { Component, inject, model } from '@angular/core';
import { BtnComponent } from '../btn/btn.component';
import type { OverlayDirtyModel } from '../core/generated/contracts';
import { OverlayRef } from './overlay-ref';

export type DirtyChoice = 'save' | 'discard' | 'continue';

@Component({
  selector: 'ui-overlay-dirty',
  imports: [BtnComponent],
  template: `
    <header class="px-3 py-2 border-b border-line">
      <h2 [id]="id + '-title'" class="m-0 text-base font-semibold">{{ model()?.title }}</h2>
    </header>
    <p [id]="id + '-description'" class="m-0 px-3 py-4 max-w-prose whitespace-pre-line">{{ model()?.description }}</p>
    <footer class="flex justify-end gap-(--ui-panel-gap) px-3 py-2 border-t border-line">
      <ui-btn autofocus [label]="model()?.continueLabel" color="slate" (click)="ref.close('continue')" />
      <ui-btn [label]="model()?.discardLabel" color="slate" (click)="ref.close('discard')" />
      <ui-btn [label]="model()?.saveLabel" variant="solid" (click)="ref.close('save')" />
    </footer>
  `,
  host: { class: 'flex flex-col min-w-80' },
})
export class OverlayDirtyComponent {
  private static next = 0;
  protected readonly id = `ui-dirty-${++OverlayDirtyComponent.next}`;
  protected readonly ref = inject(OverlayRef<DirtyChoice>);
  readonly model = model<OverlayDirtyModel | null>();

  constructor() {
    // The card is the alert dialog, not a dialog holding one
    this.ref.name({ role: 'alertdialog', labelledBy: `${this.id}-title`, describedBy: `${this.id}-description` });
  }
}
