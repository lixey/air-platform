import type { ErrorHandler } from '@angular/core';
import type { IsCurrent } from './lifetime';

/** An approval belongs to one close attempt and must remain current until actual closure. */
export interface CloseApproval {
  result?: unknown;
  cause?: symbol;
  isCurrent?: () => boolean;
  release?: () => void;
}

export interface CloseAttempt {
  result?: unknown;
  cause?: symbol;
  signal?: AbortSignal;
}

export type CloseParticipant = (request: CloseAttempt) => boolean | CloseApproval | Promise<boolean | CloseApproval>;

/** One close attempt as its owner starts it. */
export interface CloseDecisionOptions {
  /** Who is asked, in order; the first refusal ends the attempt. */
  readonly participants: readonly CloseParticipant[];
  /** The result and cause the attempt closes with; the participants may reshape them. */
  readonly request?: CloseAttempt;
  /** Whether the owner that started the attempt is still current: once it is not, the attempt is refused. */
  readonly isOwnerCurrent: IsCurrent;
  /** What a committed attempt does with the request as the participants shaped it. */
  readonly onCommit?: (request: CloseAttempt) => void;
  /** The owner's handler of a participant, a commit or a release that throws. */
  readonly errorHandler: ErrorHandler;
}

/**
 * The close protocol of anything that can refuse to go away: an overlay, a routed screen, a form.
 * A participant is asked about one attempt and answers with an approval; the decision collects the
 * approvals and holds them until the caller commits or cancels it.
 *
 * One cancellable collection of decisions; receipts live until the caller settles it. A participant or a commit that
 * throws refuses the closure, and a release that throws is skipped; each hands its error to the owner's `ErrorHandler`.
 */
export class CloseDecision {
  private readonly abort = new AbortController();
  private readonly approvals: CloseApproval[] = [];
  private isSettled = false;
  private isApproved = false;
  private readonly readiness = pending<boolean>();
  private readonly closure = pending<boolean>();
  readonly ready = this.readiness.promise;
  readonly done = this.closure.promise;
  private _request: CloseAttempt;
  private readonly isOwnerCurrent: IsCurrent;
  private readonly onCommit: (request: CloseAttempt) => void;
  private readonly errorHandler: ErrorHandler;

  constructor({ participants, request = {}, isOwnerCurrent, onCommit = () => undefined, errorHandler }: CloseDecisionOptions) {
    this._request = { ...request, signal: this.abort.signal };
    this.isOwnerCurrent = isOwnerCurrent;
    this.onCommit = onCommit;
    this.errorHandler = errorHandler;
    Promise.resolve().then(() => this.collect(participants));
  }

  /** The attempt as the participants shaped it: the result and cause of the last approval that named one. */
  get request(): CloseAttempt {
    return this._request;
  }

  isCurrent(): boolean {
    return (
      !this.isSettled &&
      this.isApproved &&
      this.isOwnerCurrent() &&
      this.approvals.every(approval => !approval.isCurrent || approval.isCurrent())
    );
  }

  commit(): boolean {
    if (!this.isCurrent()) {
      this.cancel();
      return false;
    }
    try {
      this.onCommit(this.request);
      this.settle(true);
      return true;
    } catch (error) {
      this.cancel();
      this.errorHandler.handleError(error);
      return false;
    }
  }

  cancel(): void {
    this.settle(false);
  }

  private async collect(participants: readonly CloseParticipant[]): Promise<void> {
    try {
      for (const participant of participants) {
        if (this.isSettled || !this.isOwnerCurrent()) {
          this.cancel();
          return;
        }
        const answer = await participant(this.request);
        if (answer && answer !== true) {
          if (this.isSettled) {
            this.release(answer);
            return;
          }
          this.approvals.push(answer);
          if ('result' in answer) {
            this._request = { ...this._request, result: answer.result, cause: answer.cause };
          }
        }
        if (!answer || this.isSettled) {
          this.cancel();
          return;
        }
      }
      this.isApproved = true;
      if (this.isCurrent()) {
        this.readiness.resolve(true);
      } else {
        this.cancel();
      }
    } catch (error) {
      this.cancel();
      this.errorHandler.handleError(error);
    }
  }

  private settle(success: boolean): void {
    if (this.isSettled) {
      return;
    }
    this.isSettled = true;
    this.abort.abort();
    this.readiness.resolve(false);
    for (const approval of this.approvals.splice(0)) {
      this.release(approval);
    }
    this.closure.resolve(success);
  }

  private release(approval: CloseApproval): void {
    try {
      approval.release?.();
    } catch (error) {
      // Reported, not rethrown: one owner's cleanup must not strand another owner's receipt.
      this.errorHandler.handleError(error);
    }
  }
}

/** A promise beside the function that resolves it, for an owner that settles it later. */
function pending<T>(): { readonly promise: Promise<T>; readonly resolve: (value: T) => void } {
  let resolve: (value: T) => void = () => undefined;
  const promise = new Promise<T>(settle => (resolve = settle));
  return { promise, resolve };
}
