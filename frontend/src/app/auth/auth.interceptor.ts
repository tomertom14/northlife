import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthStore } from './auth-store';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthStore);
  const router = inject(Router);
  const token = auth.token();
  const isAnonymousAuthCall =
    request.url.endsWith('/api/auth/login') ||
    request.url.endsWith('/api/auth/register');

  if (!token || isAnonymousAuthCall || !request.url.startsWith('/api/')) {
    return next(request);
  }

  return next(request.clone({
    setHeaders: { Authorization: `Bearer ${token}` },
  })).pipe(
    catchError((error: unknown) => {
      // The token expired or was revoked: drop the session and ask for a fresh login.
      if (error instanceof HttpErrorResponse && error.status === 401 && !request.url.endsWith('/api/auth/logout')) {
        auth.clear();
        void router.navigate(['/manage/login'], {
          queryParams: { returnUrl: router.url.startsWith('/manage/') ? router.url : undefined, expired: 1 },
        });
      }
      return throwError(() => error);
    }),
  );
};
