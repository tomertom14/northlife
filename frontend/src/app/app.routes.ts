import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';
import { adminGuard } from './auth/admin.guard';
import { PublicLayout } from './layouts/public-layout';
import { EventDetailsPage } from './pages/event-details-page';
import { HomePage } from './pages/home-page';

// The feed and event pages ship in the initial bundle; the map and the management area load on demand.
export const routes: Routes = [
  {
    path: '',
    component: PublicLayout,
    children: [
      { path: '', component: HomePage, title: 'NorthLife | אירועים בצפון' },
      {
        path: 'map',
        loadComponent: () => import('./pages/map-page').then((m) => m.MapPage),
        title: 'מפת אירועים | NorthLife',
      },
      { path: 'events/:id', component: EventDetailsPage, title: 'פרטי אירוע | NorthLife' },
    ],
  },
  {
    path: 'manage',
    loadComponent: () => import('./layouts/management-layout').then((m) => m.ManagementLayout),
    children: [
      {
        path: 'login',
        loadComponent: () => import('./pages/login-page').then((m) => m.LoginPage),
        title: 'כניסה | NorthLife',
      },
      {
        path: 'register',
        loadComponent: () => import('./pages/register-page').then((m) => m.RegisterPage),
        title: 'הרשמה לעסקים | NorthLife',
      },
      {
        path: 'dashboard',
        loadComponent: () => import('./pages/dashboard-placeholder-page').then((m) => m.DashboardPage),
        canActivate: [authGuard],
        title: 'האירועים שלי | NorthLife',
      },
      {
        path: 'admin',
        loadComponent: () => import('./pages/admin-page').then((m) => m.AdminPage),
        canActivate: [adminGuard],
        title: 'מרכז בקרה | NorthLife',
      },
      { path: '', pathMatch: 'full', redirectTo: 'login' },
    ],
  },
  { path: '**', redirectTo: '' },
];
