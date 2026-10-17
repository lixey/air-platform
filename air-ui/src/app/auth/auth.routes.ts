import type { Route } from '@angular/router';
import { authGuard } from './auth.guard';

/**
 * The authentication pages an application spreads beside its shell route: `login`, where `authGuard` sends a
 * signed-out user, and `change-password`, where `passwordChangedGuard` sends a user who must change the password,
 * itself behind `authGuard`. Both are loaded when first visited.
 */
export const AUTH_ROUTES: Route[] = [
  { path: 'login', loadComponent: () => import('./login-page.component').then(m => m.LoginPageComponent) },
  {
    path: 'change-password',
    canActivate: [authGuard],
    loadComponent: () => import('./change-password-page.component').then(m => m.ChangePasswordPageComponent),
  },
];
