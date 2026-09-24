import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { loginRedirect } from './auth.guard';
import { AuthStore } from './auth-store';

export const adminGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthStore);
  const router = inject(Router);
  if (!auth.hasValidSession()) return loginRedirect(auth, router, state.url);
  return auth.user()?.role === 'Admin' ? true : router.createUrlTree(['/manage/dashboard']);
};
