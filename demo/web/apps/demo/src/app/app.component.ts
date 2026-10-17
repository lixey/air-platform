import { AlertComponent } from '@air-ui/app/alert/alert.component';
import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/**
 * The demo runs the same frame the platform does: navigation on the left, the screen the
 * route names beside it. Everything below that arrives from the server.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, AlertComponent],
  template: '<router-outlet/><ui-alert/>',
  host: {
    class: 'block h-screen w-screen overflow-hidden bg-page',
  },
})
export class AppComponent {}
