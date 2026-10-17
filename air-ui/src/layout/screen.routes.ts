import type { Route } from '@angular/router';
import { screenLeaveGuard } from '../screen/screen-leave.guard';
import { LayoutRouteScreenComponent } from './layout-route-screen.component';

/**
 * The routes an application spreads where its shell renders. An address is a module handle
 * and a feature path, as the catalogue names them; the layout resolves the entry by URL from
 * the metadata, so nothing here names a screen. `**` lands every deeper address on the layout
 * as well.
 */
export const SCREEN_ROUTES: Route[] = [
  { path: ':module/:feature', component: LayoutRouteScreenComponent, canDeactivate: [screenLeaveGuard], runGuardsAndResolvers: 'always' },
  { path: ':module', component: LayoutRouteScreenComponent, canDeactivate: [screenLeaveGuard], runGuardsAndResolvers: 'always' },
  { path: '**', component: LayoutRouteScreenComponent, canDeactivate: [screenLeaveGuard], runGuardsAndResolvers: 'always' },
];
