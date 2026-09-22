import { Routes } from '@angular/router';
import { ManagementLayout } from './layouts/management-layout';
import { PublicLayout } from './layouts/public-layout';
import { DashboardPlaceholderPage } from './pages/dashboard-placeholder-page';
import { HomePage } from './pages/home-page';
import { LoginPlaceholderPage } from './pages/login-placeholder-page';

export const routes: Routes = [
  {
    path: '',
    component: PublicLayout,
    children: [{ path: '', component: HomePage }],
  },
  {
    path: 'manage',
    component: ManagementLayout,
    children: [
      { path: 'login', component: LoginPlaceholderPage },
      { path: 'dashboard', component: DashboardPlaceholderPage },
      { path: '', pathMatch: 'full', redirectTo: 'login' },
    ],
  },
  { path: '**', redirectTo: '' },
];
