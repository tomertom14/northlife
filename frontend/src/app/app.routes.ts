import { Routes } from '@angular/router';
import { authGuard, sessionGuard } from './auth/auth.guard';
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
        path: 'verify-email',
        loadComponent: () => import('./pages/account-pages').then((m) => m.VerifyEmailPage),
        title: 'אימות אימייל | NorthLife',
      },
      {
        path: 'forgot-password',
        loadComponent: () => import('./pages/account-pages').then((m) => m.ForgotPasswordPage),
        title: 'שכחתי סיסמה | NorthLife',
      },
      {
        path: 'reset-password',
        loadComponent: () => import('./pages/account-pages').then((m) => m.ResetPasswordPage),
        title: 'סיסמה חדשה | NorthLife',
      },
      {
        path: 'complete-profile',
        loadComponent: () => import('./pages/account-pages').then((m) => m.CompleteProfilePage),
        title: 'השלמת פרטי העסק | NorthLife',
      },
      {
        path: 'security',
        loadComponent: () => import('./pages/security-page').then((m) => m.SecurityPage),
        canActivate: [sessionGuard],
        title: 'אבטחת החשבון | NorthLife',
      },
      {
        path: 'dashboard',
        loadComponent: () => import('./pages/dashboard-placeholder-page').then((m) => m.DashboardPage),
        canActivate: [authGuard],
        title: 'האירועים שלי | NorthLife',
      },
      {
        path: 'analytics',
        loadComponent: () => import('./pages/owner-analytics-page').then((m) => m.OwnerAnalyticsPage),
        canActivate: [authGuard],
        title: 'צפיות ונתונים | NorthLife',
      },
      {
        path: 'admin',
        loadComponent: () => import('./pages/admin-page').then((m) => m.AdminPage),
        canActivate: [adminGuard],
        title: 'מרכז בקרה | NorthLife',
      },
      {
        path: 'admin/users',
        loadComponent: () => import('./pages/admin-users-page').then((m) => m.AdminUsersPage),
        canActivate: [adminGuard],
        title: 'משתמשים | NorthLife',
      },
      {
        path: 'admin/users/:id',
        loadComponent: () => import('./pages/admin-user-page').then((m) => m.AdminUserPage),
        canActivate: [adminGuard],
        title: 'פרטי משתמש | NorthLife',
      },
      {
        path: 'admin/audit',
        loadComponent: () => import('./pages/admin-audit-page').then((m) => m.AdminAuditPage),
        canActivate: [adminGuard],
        title: 'יומן פעולות | NorthLife',
      },
      {
        path: 'admin/auto-moderation',
        loadComponent: () => import('./pages/admin-auto-moderation-page').then((m) => m.AdminAutoModerationPage),
        canActivate: [adminGuard],
        title: 'אישור אוטומטי | NorthLife',
      },
      { path: '', pathMatch: 'full', redirectTo: 'login' },
    ],
  },
  { path: '**', redirectTo: '' },
];
