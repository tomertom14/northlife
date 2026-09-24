import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthStore } from './auth-store';

/** Business-owner dashboard: needs a live session; administrators are sent to their own screen. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthStore);
  const router = inject(Router);
  if (!auth.hasValidSession()) return loginRedirect(auth, router, state.url);
  return auth.user()?.role === 'Admin' ? router.createUrlTree(['/manage/admin']) : true;
};

export function loginRedirect(auth: AuthStore, router: Router, returnUrl: string) {
  const expired = auth.isAuthenticated();
  auth.clear();
  return router.createUrlTree(['/manage/login'], {
    queryParams: { returnUrl, expired: expired ? 1 : undefined },
  });
}
