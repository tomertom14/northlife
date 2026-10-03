import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { loginRedirect } from './auth.guard';
import { AuthStore } from './auth-store';

/** Administrators need the role and a session that passed TOTP; the API enforces the same rule. */
export const adminGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthStore);
  const router = inject(Router);
  if (!auth.hasValidSession()) return loginRedirect(auth, router, state.url);
  const user = auth.user();
  if (user?.role !== 'Admin') return router.createUrlTree(['/manage/dashboard']);
  return user.mfaVerified ? true : router.createUrlTree(['/manage/security'], { queryParams: { required: 1 } });
};
