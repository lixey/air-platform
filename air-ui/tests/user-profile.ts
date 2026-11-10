import type { UserProfileDto } from '../src/core/generated/contracts';

export function userProfile(id = 1, claims: Record<string, string> = {}, timeZone = 'UTC', permissions: string[] = []): UserProfileDto {
  return {
    id,
    login: `user${id}`,
    name: `User ${id}`,
    email: `user${id}@example.com`,
    language: 'en',
    timeZone,
    mustChangePassword: false,
    permissions,
    claims,
  };
}
