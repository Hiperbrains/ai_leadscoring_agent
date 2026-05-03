import { Routes } from '@angular/router';
import { WorkspaceComponent } from './workspace/workspace.component';

export const routes: Routes = [
  {
    path: 'email',
    loadComponent: () =>
      import('./email-capture/email-capture.component').then((m) => m.EmailCaptureComponent)
  },
  { path: '', component: WorkspaceComponent }
];
