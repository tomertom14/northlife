import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';
import { adminGuard } from './auth/admin.guard';
import { ManagementLayout } from './layouts/management-layout';
import { PublicLayout } from './layouts/public-layout';
import { DashboardPage } from './pages/dashboard-placeholder-page';
import { EventDetailsPage } from './pages/event-details-page';
import { HomePage } from './pages/home-page';
import { LoginPage } from './pages/login-page';
import { RegisterPage } from './pages/register-page';
import { AdminPage } from './pages/admin-page';
import { MapPage } from './pages/map-page';

export const routes: Routes = [
  {
    path: '',
    component: PublicLayout,
    children: [
      { path: '', component: HomePage, title: 'NorthLife | אירועים בצפון' },
      { path: 'map', component: MapPage, title: 'מפת אירועים | NorthLife' },
      { path: 'events/:id', component: EventDetailsPage, title: 'פרטי אירוע | NorthLife' },
    ],
  },
  {
    path: 'manage',
    component: ManagementLayout,
    children: [
      { path: 'login', component: LoginPage, title: 'כניסה | NorthLife' },
      { path: 'register', component: RegisterPage, title: 'הרשמה לעסקים | NorthLife' },
      {
        path: 'dashboard',
        component: DashboardPage,
        canActivate: [authGuard],
        title: 'לוח ניהול | NorthLife',
      },
      {
        path: 'admin',
        component: AdminPage,
        canActivate: [adminGuard],
        title: 'מרכז בקרה | NorthLife',
      },
      { path: '', pathMatch: 'full', redirectTo: 'login' },
    ],
  },
  { path: '**', redirectTo: '' },
];
