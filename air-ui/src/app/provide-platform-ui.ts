import type { HttpInterceptorFn } from '@angular/common/http';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import type { EnvironmentProviders } from '@angular/core';
import { inject, makeEnvironmentProviders, provideAppInitializer, provideZonelessChangeDetection } from '@angular/core';
import type { Routes } from '@angular/router';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import type { CheckedComponentMap, OverridingComponentMap } from '../core/dynamic-component/component-map';
import { isOverriding, provideDynamicComponents } from '../core/dynamic-component/provide-dynamic-components';
import { LayoutComponent } from '../layout/layout.component';
import { SCREEN_ROUTES } from '../layout/screen.routes';
import { UI_COMPONENTS } from '../ui-components';
import { AppService } from './app.service';
import { authGuard, passwordChangedGuard } from './auth/auth.guard';
import { AUTH_ROUTES } from './auth/auth.routes';
import { UI_INTERCEPTORS, UI_ROUTER_FEATURES } from './ui-providers';

export type DynamicComponentMap = CheckedComponentMap | OverridingComponentMap;

export const PLATFORM_UI_ROUTES: Routes = [
  ...AUTH_ROUTES,
  {
    path: '',
    canActivate: [authGuard, passwordChangedGuard],
    canActivateChild: [authGuard, passwordChangedGuard],
    component: LayoutComponent,
    children: SCREEN_ROUTES,
  },
];

export interface PlatformUiOptions {
  readonly extraRoutes?: Routes;
  readonly components?: DynamicComponentMap;
  readonly extraInterceptors?: readonly HttpInterceptorFn[];
}

export function providePlatformUi(options?: PlatformUiOptions): EnvironmentProviders {
  const dynamicComponents =
    options?.components === undefined
      ? provideDynamicComponents(UI_COMPONENTS)
      : isOverriding(options.components)
        ? provideDynamicComponents(UI_COMPONENTS, options.components)
        : provideDynamicComponents(options.components);

  const routes: Routes = options?.extraRoutes?.length
    ? [
        ...AUTH_ROUTES,
        ...options.extraRoutes,
        {
          path: '',
          canActivate: [authGuard, passwordChangedGuard],
          canActivateChild: [authGuard, passwordChangedGuard],
          component: LayoutComponent,
          children: SCREEN_ROUTES,
        },
      ]
    : PLATFORM_UI_ROUTES;

  const interceptors: readonly HttpInterceptorFn[] = options?.extraInterceptors?.length
    ? [...UI_INTERCEPTORS.slice(0, -1), ...options.extraInterceptors, UI_INTERCEPTORS[UI_INTERCEPTORS.length - 1]!]
    : UI_INTERCEPTORS;

  return makeEnvironmentProviders([
    provideZonelessChangeDetection(),
    provideRouter(routes, withComponentInputBinding(), ...UI_ROUTER_FEATURES),
    provideHttpClient(withInterceptors([...interceptors])),
    dynamicComponents,
    provideAppInitializer(() => inject(AppService).init()),
  ]);
}
