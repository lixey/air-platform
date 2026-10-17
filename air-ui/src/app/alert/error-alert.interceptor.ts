import { HttpContext, HttpContextToken, type HttpErrorResponse, type HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { problemOf } from '../../core/problem';
import { TextsService } from '../../core/texts.service';
import { AlertService } from './alert.service';

/** When set to true on a request's HttpContext, the global error alert is suppressed. */
export const SKIP_GLOBAL_ERROR = new HttpContextToken<boolean>(() => false);

/**
 * The options of a request whose failure its caller shows — a form's save, a grid's action, a screen's load: the global
 * error alert stays silent, so every outcome is shown once.
 */
export function shownByCaller(): { context: HttpContext } {
  return { context: new HttpContext().set(SKIP_GLOBAL_ERROR, true) };
}

/**
 * Server errors display as alerts formatted using localized platform texts.
 * `401` authentication flows, refused validations, concurrency conflicts (409 / conflict outcome),
 * and requests with `SKIP_GLOBAL_ERROR` are skipped.
 */
export const errorAlertInterceptor: HttpInterceptorFn = (req, next) => {
  const alert = inject(AlertService);
  const texts = inject(TextsService);

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      const skipAlert =
        req.context.get(SKIP_GLOBAL_ERROR) ||
        err.status === 401 ||
        err.status === 409 ||
        err.error?.outcome === 'refused' ||
        err.error?.outcome === 'conflict';

      if (!skipAlert) {
        const title = `${texts.of('Error')} ${err.status || ''}`.trim();
        const problem = problemOf(err);
        alert.error(title, problem?.title || problem?.detail || texts.of('RequestFailed'));
      }
      return throwError(() => err);
    }),
  );
};
