import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { ProductContextService } from '../services/product-context.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const productContext = inject(ProductContextService);
  const token = auth.getToken();

  const isAuthEndpoint =
    req.url.includes('/api/auth/login') ||
    req.url.includes('/api/auth/signup') ||
    req.url.includes('/api/auth/plans');

  const headers: Record<string, string> = {};
  if (token && !isAuthEndpoint) {
    headers['Authorization'] = `Bearer ${token}`;
  }

  if (!isAuthEndpoint && req.url.includes('/api/')) {
    const productId = productContext.currentProductId();
    if (productId != null) {
      headers['X-Product-Id'] = String(productId);
    }
  }

  const authReq = Object.keys(headers).length > 0 ? req.clone({ setHeaders: headers }) : req;

  return next(authReq).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 401 && !isAuthEndpoint) {
        auth.logout();
        void router.navigate(['/login']);
      }
      return throwError(() => err);
    })
  );
};
