import type { PanelLayoutDto, PanelModel } from '../core/generated/contracts';

/**
 * The layout of an overlay whose whole content is one component: one column with one panel sized to its content by
 * default (`size = 'auto'`), or stretched to fill the overlay (`size = '1fr'`). The panel holds the model, whose kind
 * names the component.
 */
export function singlePanelLayout(key: string, model: PanelModel, size: 'auto' | '1fr' = 'auto'): PanelLayoutDto {
  return {
    key,
    root: {
      key: 'root',
      direction: 'column',
      children: [{ key: 'body', model, size }],
    },
  };
}
