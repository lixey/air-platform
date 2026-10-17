import type { HttpInterceptorFn } from '@angular/common/http';
import { withRouterConfig } from '@angular/router';
import { errorAlertInterceptor } from './alert/error-alert.interceptor';
import { credentialsInterceptor } from './auth/credentials.interceptor';
import { sessionRefreshInterceptor } from './auth/session-refresh.interceptor';
import { progressInterceptor } from './progress/progress.interceptor';

/**
 * The router features a host of `SCREEN_ROUTES` must wire: `computed` restores the history
 * position when an owner refuses Back or Forward, which is what the leave decision relies on.
 */
export const UI_ROUTER_FEATURES = [withRouterConfig({ canceledNavigationResolution: 'computed' })] as const;

/**
 * The library's interceptors in the one order that works: the session refresh sits outermost so a
 * retried request is composed again, the credentials go on every API request, the progress bar
 * counts what leaves, and the error alert reads what comes back.
 */
export const UI_INTERCEPTORS: readonly HttpInterceptorFn[] = [
  sessionRefreshInterceptor,
  credentialsInterceptor,
  progressInterceptor,
  errorAlertInterceptor,
];
