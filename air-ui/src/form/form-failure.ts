import type { AlertService } from '../app/alert/alert.service';
import type { FormElementDto, MessageDto, MutationOutcome } from '../core/generated/contracts';
import { classifyMutationFailure, refusalDetailOf } from '../core/mutation-failure';
import { problemOf } from '../core/problem';
import type { TextsService } from '../core/texts.service';
import { frozenCopy } from './form-value';

export interface FormFailureRouteOptions {
  readonly error: unknown;
  readonly own?: readonly MessageDto[];
  readonly alert: AlertService;
  readonly texts: Pick<TextsService, 'of'>;
  readonly conflictTitle?: string | null;
  readonly showsMessage: (path: string) => boolean;
  readonly elementOf: (path: string) => FormElementDto | undefined;
}

export interface FormFailureRouteResult {
  readonly outcome: MutationOutcome;
  readonly fieldMessages: readonly MessageDto[];
}

/** Reports a read failure of a form screen in the alert under the server's title or the model's `unknownTitle`. */
export function reportFormReadFailure(error: unknown, alert: AlertService, unknownTitle?: string | null): void {
  const problem = problemOf(error);
  const title = problem?.title ?? unknownTitle;
  if (title) {
    alert.error(title, problem?.detail);
  }
}

/**
 * Routes a save or action failure between the form's visible fields and the alert: messages whose path can be shown by
 * a form element return as `fieldMessages`, `own` messages belonging to an open `Ask` dialog are omitted, and all
 * remaining messages are shown in `alert` (prefixed by their element label when known).
 */
export function routeFormFailure(options: FormFailureRouteOptions): FormFailureRouteResult {
  const { error, own = [], alert, texts, conflictTitle, showsMessage, elementOf } = options;
  const failure = classifyMutationFailure(error);
  const messages = own.length ? failure.messages.filter(m => !own.includes(m)) : failure.messages;
  const shown = messages.filter(m => !!m.path && showsMessage(m.path));
  const fieldMessages = frozenCopy(shown);
  const general = messages
    .filter(m => !shown.includes(m))
    .map(message => {
      const label = message.path ? elementOf(message.path)?.label : undefined;
      const text = message.title ?? message.detail;
      return label && text ? { ...message, title: `${label}: ${text}` } : message;
    });

  if (failure.outcome === 'unknown') {
    if (general.length) {
      alert.open(general);
    }
    return { outcome: failure.outcome, fieldMessages };
  }

  if (failure.outcome === 'conflict' || general.length || !failure.messages.length) {
    const fallback = failure.outcome === 'conflict' ? conflictTitle : texts.of('RequestFailed');
    const title = failure.title ?? fallback;
    if (title) {
      alert.error(title, refusalDetailOf(failure, general, '\n'));
    }
  }

  return { outcome: failure.outcome, fieldMessages };
}
