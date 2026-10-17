import { inject, Injector, Service } from '@angular/core';
import type { FormModel } from '../core/generated/contracts';
import { singlePanelLayout } from '../panels/single-panel-layout';
import type { DirtyChoice } from './overlay-dirty.component';
import { OverlayService } from './overlay.service';

@Service()
export class DirtyDialogService {
  private readonly overlay = inject(OverlayService);

  open(model: FormModel['dirtyDialog'], injector: Injector) {
    return this.overlay.open<DirtyChoice>(
      {
        key: 'overlay.dirty',
        presentation: 'dialog',
        layout: singlePanelLayout('overlay.dirty', model),
      },
      null,
      injector,
      { isDirtyPrompt: true },
    );
  }
}
