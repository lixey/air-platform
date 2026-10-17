import { HttpErrorResponse } from '@angular/common/http';
import { isRecord } from './json';

/** What a failed request says for a human: the server's problem title and detail. */
export interface Problem {
  readonly title?: string;
  readonly detail?: string;
}

/**
 * The problem of a failed request, wherever it sits: the body of an `HttpErrorResponse`, a body
 * rethrown on its own, or one still wrapped in `{ error }`. `message` stands in for a missing
 * detail. A response that says nothing at all falls back to its status text — unless it is one of
 * the two the browser invents — because then the status is the whole answer; a response that does
 * name a title or a detail never has its status text added to it. Null when nothing says anything.
 */
export function problemOf(error: unknown): Problem | null {
  const body = bodyOf(error);
  const title = typeof body?.['title'] === 'string' ? body['title'] : undefined;
  const detail = detailOf(body);

  if (title === undefined && detail === undefined) {
    const status = statusTextOf(error);
    return status === undefined ? null : { detail: status };
  }

  return { ...(title !== undefined && { title }), ...(detail !== undefined && { detail }) };
}

/** The body's `detail`, else its `message`. */
function detailOf(body: Record<string, unknown> | undefined): string | undefined {
  if (typeof body?.['detail'] === 'string') {
    return body['detail'];
  }
  return typeof body?.['message'] === 'string' ? body['message'] : undefined;
}

function bodyOf(error: unknown): Record<string, unknown> | undefined {
  if (error instanceof HttpErrorResponse) {
    return isRecord(error.error) ? error.error : undefined;
  }
  if (!isRecord(error)) {
    return undefined;
  }
  return isRecord(error['error']) ? error['error'] : error;
}

function statusTextOf(error: unknown): string | undefined {
  if (!(error instanceof HttpErrorResponse)) {
    return undefined;
  }
  const text = error.statusText;
  return text && text !== 'Unknown Error' && text !== 'OK' ? text : undefined;
}
