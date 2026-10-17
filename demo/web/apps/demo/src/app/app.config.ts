import { AppService } from '@air-ui/app/app.service';
import { UI_INTERCEPTORS, UI_ROUTER_FEATURES } from '@air-ui/app/ui-providers';
import { provideDynamicComponents } from '@air-ui/core/dynamic-component/provide-dynamic-components';
import { UI_COMPONENTS } from '@air-ui/ui-components';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import type { ApplicationConfig } from '@angular/core';
import { inject, provideAppInitializer, provideZonelessChangeDetection } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { appRoutes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZonelessChangeDetection(),
    provideRouter(appRoutes, withComponentInputBinding(), ...UI_ROUTER_FEATURES),
    provideHttpClient(withInterceptors([...UI_INTERCEPTORS])),
    provideDynamicComponents(UI_COMPONENTS),
    provideAppInitializer(() => initializeDemo(inject(AppService))),
  ],
};

export function initializeDemo(app: AppService): Promise<void> {
  return app.init();
}
