import { InjectionToken } from '@angular/core';
import type { Lifetime } from '../core/lifetime';

/**
 * The lifetime of the screen a component stands in: a route's generation, an overlay's opening, a child page's parent
 * record. Asynchronous work of a screen captures it, or a child of it, and drops its answer once the capture is stale.
 */
export const SCREEN_LIFETIME = new InjectionToken<Lifetime>('SCREEN_LIFETIME');
