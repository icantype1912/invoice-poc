import { inject } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';
import { Auth } from '../services/auth';

export const jwtInterceptor: HttpInterceptorFn = (req, next) => {

  const auth = inject(Auth);
  const token = auth.getToken();

  const headers: any = {
    'ngrok-skip-browser-warning': 'true'
  };

  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  const cloned = req.clone({
    setHeaders: headers
  });

  return next(cloned);
};
