import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthStore } from './auth-store';

export const adminGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthStore);
  const router = inject(Router);
  if (!auth.isAuthenticated()) {
    return router.createUrlTree(['/manage/login'], { queryParams: { returnUrl: state.url } });
  }
  return auth.user()?.role === 'Admin' ? true : router.createUrlTree(['/manage/dashboard']);
};
