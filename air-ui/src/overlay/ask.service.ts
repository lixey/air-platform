import { inject, InjectionToken, Injector, Service, type StaticProvider } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { FormAskModel, MessageDto } from '../core/generated/contracts';
import { singlePanelLayout } from '../panels/single-panel-layout';
import { OverlayService } from './overlay.service';

/** Posts the values; answers the messages that stay on the dialog's fields, none to close it. */
export type AskSubmit = (value: Readonly<Record<string, unknown>>) => Promise<readonly MessageDto[]>;

/** The submission of the opener, which the dialog of an action's input hands its values to. */
export const ASK_SUBMIT = new InjectionToken<AskSubmit>('ASK_SUBMIT');

/** The values the dialog opens with, by element path; none opens it empty. */
export const ASK_VALUE = new InjectionToken<Readonly<Record<string, unknown>>>('ASK_VALUE');

/** The messages of a refusal that name a field of the dialog. */
export function ownMessages(model: FormAskModel, messages: readonly MessageDto[]): readonly MessageDto[] {
  const paths = new Set(model.elements.map(element => element.path));
  return messages.filter(message => !!message.path && paths.has(message.path));
}

/** Asks an action's input in a dialog that stays open while its opener's submission answers messages. */
@Service()
export class AskService {
  private readonly overlay = inject(OverlayService);
  private readonly injector = inject(Injector);

  /**
   * Opens the dialog, over `value` when its opener gives the values it opens with, and resolves when it is closed: true
   * after a submission that left no message, false when given up.
   */
  async ask(model: FormAskModel, submit: AskSubmit, injector?: Injector, value?: Readonly<Record<string, unknown>>): Promise<boolean> {
    const providers: StaticProvider[] = [{ provide: ASK_SUBMIT, useValue: submit }];
    if (value) {
      providers.push({ provide: ASK_VALUE, useValue: value });
    }
    const ref = this.overlay.open<boolean>(
      { key: 'overlay.ask', presentation: 'dialog', layout: singlePanelLayout('overlay.ask', model) },
      null,
      Injector.create({ providers, parent: injector ?? this.injector }),
    );
    return (await firstValueFrom(ref.closed)) === true;
  }
}
