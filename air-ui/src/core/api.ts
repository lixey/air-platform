import { HttpClient, type HttpContext } from '@angular/common/http';
import { inject, Service } from '@angular/core';
import type { Observable } from 'rxjs';
import type { PlatformEndpoints } from './generated/contracts';

/** Where the server maps every endpoint: `/api/{service}/{method}`. */
export const API_PREFIX = '/api/';

/** A route of the platform's endpoint map, `{service}/{method}`: a route the map does not hold does not compile. */
export type ApiRoute = keyof PlatformEndpoints;

type ServiceOf<R> = R extends `${infer S}/${string}` ? S : never;

/** The service segment of a route of the platform's map (`auth`, `metadata`). */
export type ApiService = ServiceOf<ApiRoute>;

/** What the endpoint `K` of the endpoint map `M` takes. */
export type RequestOf<M, K extends keyof M> = M[K] extends { request: infer R } ? R : never;

/** What the endpoint `K` of the endpoint map `M` answers. */
export type AnswerOf<M, K extends keyof M> = M[K] extends { answer: infer A } ? A : never;

/** The address of a route of the platform's map. */
export function apiUrl(route: ApiRoute): string {
  return API_PREFIX + route;
}

/**
 * The address of a method of a service, both route segments as the server sent them: nothing is converted. A caller
 * that knows the kind of the service names its table (`serviceUrl<GridServiceEndpoints>(service, 'get-count')`), and a
 * method the kind does not declare does not compile; a method the server named (`action.method`) goes as it came.
 */
export function serviceUrl<M = Record<string, unknown>>(service: string, method: keyof M & string): string {
  return `${API_PREFIX}${service}/${method}`;
}

/**
 * Posts to the platform's endpoints by route: the body and the answer are the map's. A caller may narrow an answer the
 * map types as a union to one of its members (`post<'auth/refresh', SessionDto>`); a request the map types as `void`
 * posts an empty body.
 */
@Service()
export class Api {
  private readonly http = inject(HttpClient);

  post<K extends ApiRoute, A extends AnswerOf<PlatformEndpoints, K> = AnswerOf<PlatformEndpoints, K>>(
    route: K,
    body: RequestOf<PlatformEndpoints, K>,
    options?: { context?: HttpContext },
  ): Observable<A> {
    return this.http.post<A>(apiUrl(route), body ?? {}, options);
  }

  /**
   * Posts to the method `K` of a service the server named, by the endpoint table `M` of its kind
   * (`postTo<FormServiceEndpoints, 'save'>(service, 'save', body)`): the body and the answer are the table's.
   */
  postTo<M, K extends keyof M & string>(
    service: string,
    method: K,
    body: RequestOf<M, K>,
    options?: { context?: HttpContext },
  ): Observable<AnswerOf<M, K>> {
    return this.http.post<AnswerOf<M, K>>(serviceUrl<M>(service, method), body ?? {}, options);
  }
}
