import { signal, type WritableSignal } from '@angular/core';
import type { GridModel } from '../src/core/generated/contracts';
import type { GridService } from '../src/grid/grid.service';

export interface GridBinding {
  readonly model: WritableSignal<GridModel | null>;
  readonly data: WritableSignal<unknown>;
}

const bindings = new WeakMap<GridService, GridBinding>();

export function bindGrid(grid: GridService): GridBinding {
  let binding = bindings.get(grid);
  if (!binding) {
    binding = {
      model: signal<GridModel | null>(null),
      data: signal<unknown>(null),
    };
    grid.bind(binding.model, binding.data);
    bindings.set(grid, binding);
  }
  return binding;
}
