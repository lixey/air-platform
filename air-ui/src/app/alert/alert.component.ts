import { Component, inject } from '@angular/core';
import { BtnComponent } from '../../btn/btn.component';
import type { MessageType } from '../../core/generated/contracts';
import { TextsService } from '../../core/texts.service';
import type { UiColor } from '../../theme/ui-color';
import { ProgressComponent } from '../progress/progress.component';
import { type AlertMessage, AlertService } from './alert.service';

/** The hue a message type speaks in. Rendered through the hue rules the button reads. */
const HUE_BY_TYPE: Record<MessageType, UiColor> = {
  success: 'green',
  error: 'red',
  warning: 'amber',
  info: 'sky',
};

/**
 * The messages the alert service is showing, stacked in a corner. Each is a card in the
 * hue of its type, announced as a status — or as an alert when it is an error — and
 * dismissed by its own close button, never by a click on the card. A detail draws its
 * line feeds as lines and scrolls inside its card when it is longer than the card's frame.
 */
@Component({
  selector: 'ui-alert',
  imports: [BtnComponent, ProgressComponent],
  template: `
    <ui-progress />
    @if (alert.messages().length > 0) {
      <div
        class="fixed z-(--ui-application-overlay-z-index) flex w-[calc(100vw-2rem-var(--sal)-var(--sar))] max-w-sm flex-col gap-2"
        style="bottom: calc(1rem + var(--sab)); right: calc(1rem + var(--sar))"
      >
        @for (msg of alert.messages(); track msg.id) {
          <div [attr.role]="roleOf(msg)" [class]="cardClasses(msg)">
            <div class="flex min-w-0 grow flex-col">
              @if (msg.title) {
                <div class="text-sm font-semibold leading-snug">{{ msg.title }}</div>
              }
              @if (msg.detail) {
                <div class="mt-0.5 max-h-60 overflow-y-auto whitespace-pre-line break-words text-xs opacity-90">{{ msg.detail }}</div>
              }
            </div>
            <ui-btn
              size="small"
              variant="ghost"
              icon="close"
              [color]="hueOf(msg)"
              [title]="texts.of('CloseNotification')"
              (click)="alert.close(msg.id)"
            />
          </div>
        }
      </div>
    }
  `,
  host: {
    class: 'contents',
  },
})
export class AlertComponent {
  readonly alert = inject(AlertService);
  protected readonly texts = inject(TextsService);

  protected hueOf(msg: AlertMessage): UiColor {
    return HUE_BY_TYPE[msg.type] ?? 'sky';
  }

  protected roleOf(msg: AlertMessage): 'alert' | 'status' {
    return msg.type === 'error' ? 'alert' : 'status';
  }

  protected cardClasses(msg: AlertMessage): string {
    return `hue-${this.hueOf(msg)} bg-(--btn-ground) border-(--btn-edge) text-(--btn-ink) flex items-start gap-2 rounded-xl border p-3 shadow-xl`;
  }
}
