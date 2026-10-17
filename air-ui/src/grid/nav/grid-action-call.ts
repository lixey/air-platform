import { serviceUrl } from '../../core/api';
import type { GridNavActionModel, GridRequest } from '../../core/generated/contracts';
import { withSelection } from '../grid-request';
import type { GridRow } from '../grid-row';

/** What a `GridNavAction` posts: the route of the method and the grid's parameters with the selection. */
export interface GridActionCall {
  readonly url: string;
  readonly body: GridRequest;
}

/**
 * The request of an action: `/api/{service}/{method}`, both segments as the server sent them, with the parameters and
 * the selection (`withSelection`). `input`, what the action's dialog asked, travels beside them: the grid's parameters
 * and the selection are written last, so no value of the dialog replaces one.
 */
export function buildActionCall(
  action: Pick<GridNavActionModel, 'service' | 'method'>,
  params: GridRequest,
  selected: readonly GridRow[],
  input: Readonly<Record<string, unknown>> = {},
): GridActionCall {
  return {
    url: serviceUrl(action.service, action.method),
    body: { ...input, ...withSelection(params, selected) },
  };
}
