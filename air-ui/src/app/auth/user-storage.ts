import type { UserProfileDto } from '../../core/generated/contracts';

/** What the library keeps in the browser for the signed-in user. */
export type UserStorageEntry = 'layout:pins' | `panels:${string}` | `named-filters:${string}`;

/**
 * The browser key of an entry of the signed-in user's own: `ui:u{id}:{entry}` — `ui:u7:layout:pins`,
 * `ui:u7:panels:{layoutKey}`, `ui:u7:named-filters:{service}`. A persistent key, declared here and nowhere else:
 * once deployed its format does not change, since a change loses what every user kept. The tenant is not part of it.
 */
export function userStorageKey(user: Pick<UserProfileDto, 'id'>, entry: UserStorageEntry): string {
  return `ui:u${user.id}:${entry}`;
}

/** The browser's storage, where there is one. */
export function browserStorage(): Storage | undefined {
  return typeof localStorage === 'undefined' ? undefined : localStorage;
}
