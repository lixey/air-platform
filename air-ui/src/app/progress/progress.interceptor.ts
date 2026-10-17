import type { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { finalize } from 'rxjs';
import { ProgressService } from './progress.service';

/** Starts and stops the platform progress bar around every request. */
export const progressInterceptor: HttpInterceptorFn = (req, next) => {
  const progress = inject(ProgressService);
  progress.startRequest();

  return next(req).pipe(
    finalize(() => {
      progress.endRequest();
    }),
  );
};
