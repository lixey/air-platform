import type { PanelLayoutDto, PanelModel } from '../core/generated/contracts';

/**
 * The layout of an overlay whose whole content is one component: one column with one panel sized to its content,
 * because the overlay around it is sized to the panel. The panel is as tall as its content and as wide as the card,
 * which is its content's width or the width the opener asked for, whichever is larger. The panel holds the model,
 * whose kind names the component.
 */
export function singlePanelLayout(key: string, model: PanelModel): PanelLayoutDto {
  return {
    key,
    root: {
      key: 'root',
      direction: 'column',
      children: [{ key: 'body', model, size: 'auto' }],
    },
  };
}
