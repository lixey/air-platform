import { authGuard, passwordChangedGuard } from '@air-ui/app/auth/auth.guard';
import { AUTH_ROUTES } from '@air-ui/app/auth/auth.routes';
import { LayoutComponent } from '@air-ui/layout/layout.component';
import { SCREEN_ROUTES } from '@air-ui/layout/screen.routes';
import type { Routes } from '@angular/router';

export { passwordChangedGuard };

export const appRoutes: Routes = [
  // The library's sign-in and change-password pages, beside the shell and outside its guards.
  ...AUTH_ROUTES,
  {
    path: '',
    canActivate: [authGuard, passwordChangedGuard],
    canActivateChild: [authGuard, passwordChangedGuard],
    // The library's frame is the whole shell: navigation, strip, session controls and switchers.
    component: LayoutComponent,
    children: SCREEN_ROUTES,
  },
];
