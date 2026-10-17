import { inject } from '@angular/core';
import { type CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Admits a route while a user is present; otherwise sends the browser to `/login` with the way back. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.isLoggedIn() ? true : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Redirects to `/change-password` if the authenticated user has `mustChangePassword == true`. */
export const passwordChangedGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  return auth.currentUser()?.mustChangePassword
    ? inject(Router).createUrlTree(['/change-password'], { queryParams: { returnUrl: state.url } })
    : true;
};
