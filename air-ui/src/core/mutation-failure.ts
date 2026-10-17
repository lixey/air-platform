import { HttpErrorResponse } from '@angular/common/http';
import type { MessageDto, MutationOutcome } from './generated/contracts';
import { isRecord } from './json';
import { problemOf } from './problem';
import type { TextsService } from './texts.service';

/** The outcomes of a mutation that did not commit. */
type FailureOutcome = Exclude<MutationOutcome, 'committed'>;

export interface MutationFailure {
  readonly outcome: FailureOutcome;
  readonly title?: string;
  readonly detail?: string;
  readonly messages: readonly MessageDto[];
}

const OUTCOMES = { refused: true, rolledBack: true, conflict: true, unknown: true } as const satisfies Record<FailureOutcome, true>;

/**
 * Classifies a failed mutation response by its declared outcome or by transport status.
 */
export function classifyMutationFailure(error: unknown): MutationFailure {
  const status = statusOf(error);
  const body = error instanceof HttpErrorResponse ? error.error : error;
  const problem = isRecord(body) ? body : undefined;

  const named = problem?.['outcome'];
  const declared = isFailureOutcome(named) ? named : undefined;

  let outcome: FailureOutcome;
  if (declared) {
    outcome = declared;
  } else if (status === 409) {
    outcome = 'conflict';
  } else if (status !== undefined && status >= 400 && status < 500) {
    outcome = 'refused';
  } else {
    outcome = 'unknown';
  }

  const rawMessages = problem && Array.isArray(problem['messages']) ? problem['messages'] : [];
  const messages: MessageDto[] = rawMessages.filter((m): m is MessageDto => isRecord(m) && typeof m['type'] === 'string');

  // The body alone, never the status text: a save dialog has its own fallback titles.
  const stated = problemOf(problem) ?? {};

  return {
    outcome,
    ...(stated.title !== undefined ? { title: stated.title } : {}),
    ...(stated.detail !== undefined ? { detail: stated.detail } : {}),
    messages,
  };
}

/**
 * What a refusal says below its title: the titles of `messages`, a message's detail where it has none, joined by
 * `separator`; else the failure's own detail. A form passes its general messages, since its fields show the others; a
 * nav, which has no fields, passes them all, a line each.
 */
export function refusalDetailOf(
  failure: MutationFailure,
  messages: readonly MessageDto[] = failure.messages,
  separator = ' ',
): string | undefined {
  const said = messages
    .map(message => message.title ?? message.detail)
    .filter(Boolean)
    .join(separator);
  return said || failure.detail;
}

/**
 * A failure as one report: the server's title, else `conflictTitle` for a conflict, else the platform's failed request;
 * below it what {@link refusalDetailOf} says of all its messages.
 */
export function refusalOf(
  failure: MutationFailure,
  texts: Pick<TextsService, 'of'>,
  conflictTitle?: string | null,
): { readonly title: string; readonly detail: string | undefined } {
  const fallback = failure.outcome === 'conflict' ? conflictTitle : undefined;
  return { title: failure.title ?? fallback ?? texts.of('RequestFailed'), detail: refusalDetailOf(failure) };
}

/** The transport status of a failed response: an `HttpErrorResponse`'s, or a plain object's numeric `status`. */
function statusOf(error: unknown): number | undefined {
  if (error instanceof HttpErrorResponse) {
    return error.status;
  }
  return isRecord(error) && typeof error['status'] === 'number' ? error['status'] : undefined;
}

function isFailureOutcome(value: unknown): value is FailureOutcome {
  return typeof value === 'string' && Object.hasOwn(OUTCOMES, value);
}
