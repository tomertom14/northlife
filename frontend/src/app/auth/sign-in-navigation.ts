import { AuthUser } from './auth.models';

/** Where a freshly signed-in account lands. Administrators without 2FA must set it up first. */
export function destinationAfterSignIn(user: AuthUser | null, returnUrl: string | null): string {
  if (user?.role === 'Admin' && !user.mfaVerified) return '/manage/security?required=1';
  if (returnUrl?.startsWith('/manage/') && !returnUrl.startsWith('/manage/login')) return returnUrl;
  return user?.role === 'Admin' ? '/manage/admin' : '/manage/dashboard';
}
