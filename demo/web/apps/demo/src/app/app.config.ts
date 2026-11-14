import { providePlatformUi } from '@air-ui/app';
import type { ApplicationConfig } from '@angular/core';

export const appConfig: ApplicationConfig = {
  providers: [providePlatformUi()],
};
