import { HttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationInitStatus, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { Routes } from '@angular/router';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { defineComponents, overriding } from '../core/dynamic-component/component-map';
import { DynamicComponentRegistry } from '../core/dynamic-component/dynamic-component-registry';
import { LayoutComponent } from '../layout/layout.component';
import { SCREEN_ROUTES } from '../layout/screen.routes';
import { AppService } from './app.service';
import { authGuard, passwordChangedGuard } from './auth/auth.guard';
import { AUTH_ROUTES } from './auth/auth.routes';
import { PLATFORM_UI_ROUTES, providePlatformUi } from './provide-platform-ui';

@Component({ template: '' })
class CustomSeparatorComponent {}

describe('providePlatformUi and PLATFORM_UI_ROUTES', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('wires AUTH_ROUTES outside the shell and guards LayoutComponent + SCREEN_ROUTES', () => {
    expect(PLATFORM_UI_ROUTES.slice(0, AUTH_ROUTES.length)).toEqual(AUTH_ROUTES);
    const shell = PLATFORM_UI_ROUTES[AUTH_ROUTES.length];
    expect(shell).toBeDefined();
    expect(shell?.path).toBe('');
    expect(shell?.component).toBe(LayoutComponent);
    expect(shell?.canActivate).toEqual([authGuard, passwordChangedGuard]);
    expect(shell?.canActivateChild).toEqual([authGuard, passwordChangedGuard]);
    expect(shell?.children).toBe(SCREEN_ROUTES);
  });

  it('provides router, dynamic components, HTTP interceptors, and runs AppService.init()', async () => {
    const initSpy = vi.fn().mockResolvedValue(undefined);
    TestBed.configureTestingModule({
      providers: [providePlatformUi(), { provide: AppService, useValue: { init: initSpy } }],
    });

    await TestBed.inject(ApplicationInitStatus).donePromise;
    expect(initSpy).toHaveBeenCalledTimes(1);

    const router = TestBed.inject(Router);
    expect(router.config).toEqual(PLATFORM_UI_ROUTES);

    const registry = TestBed.inject(DynamicComponentRegistry);
    expect(registry.resolve('Grid')).toBeDefined();
    expect(registry.resolve('Form')).toBeDefined();
  });

  it('additively composes extraRoutes, overriding component maps, and extraInterceptors without dropping platform guards/credentials', async () => {
    const extraRoutes: Routes = [{ path: 'custom', component: CustomSeparatorComponent }];
    let interceptedHeader: string | null = null;
    let sawCredentialsFlag: boolean | null = null;

    TestBed.configureTestingModule({
      providers: [
        providePlatformUi({
          extraRoutes,
          components: overriding(defineComponents({ NavSeparator: CustomSeparatorComponent }), ['NavSeparator']),
          extraInterceptors: [
            (req, next) => {
              sawCredentialsFlag = req.withCredentials;
              const cloned = req.clone({ setHeaders: { 'X-Test-Ui': 'ok' } });
              interceptedHeader = cloned.headers.get('X-Test-Ui');
              return next(cloned);
            },
          ],
        }),
        provideHttpClientTesting(),
        { provide: AppService, useValue: { init: () => Promise.resolve() } },
      ],
    });

    await TestBed.inject(ApplicationInitStatus).donePromise;

    const routerConfig = TestBed.inject(Router).config;
    expect(routerConfig.slice(0, AUTH_ROUTES.length)).toEqual(AUTH_ROUTES);
    expect(routerConfig[AUTH_ROUTES.length]).toEqual(extraRoutes[0]);
    expect(routerConfig[AUTH_ROUTES.length + 1]?.component).toBe(LayoutComponent);

    const registry = TestBed.inject(DynamicComponentRegistry);
    expect(registry.resolve('NavSeparator')).toBe(CustomSeparatorComponent);
    expect(registry.resolve('Grid')).toBeDefined();

    const http = TestBed.inject(HttpClient);
    const httpMock = TestBed.inject(HttpTestingController);
    const pending = firstValueFrom(http.get('/api/ping'));
    httpMock.expectOne('/api/ping').flush({ ok: true });
    await pending;
    expect(sawCredentialsFlag).toBe(true);
    expect(interceptedHeader).toBe('ok');
    httpMock.verify();
  });
});
