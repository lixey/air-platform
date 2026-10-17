import { inject } from '@angular/core';
import { type CanDeactivateFn, Router, UrlHandlingStrategy } from '@angular/router';
import type { ScreenLifecycle } from './screen-lifecycle';

/** A reused route participates too: its route entry runs guards for every address change. */
export const screenLeaveGuard: CanDeactivateFn<{ lifecycle: ScreenLifecycle }> = (component, _route, _state, next) => {
  const router = inject(Router);
  const strategy = inject(UrlHandlingStrategy);
  const navigation = router.currentNavigation();
  for (const url of [router.url, next.url, router.lastSuccessfulNavigation()?.initialUrl.toString(), navigation?.initialUrl.toString()]) {
    if (url === undefined) {
      continue;
    }
    const tree = router.parseUrl(url);
    if (!strategy.shouldProcessUrl(tree) || strategy.extract(tree).toString() !== tree.toString()) {
      throw new Error('Screen leave protection requires a full-URL UrlHandlingStrategy and computed navigation cancellation.');
    }
  }
  return navigation ? component.lifecycle.requestLeave(navigation.id) : false;
};
