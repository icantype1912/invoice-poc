import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Auth } from '../services/auth';

export const guestGuard: CanActivateFn = () => {

  const auth = inject(Auth);
  const router = inject(Router);

  if (auth.isLoggedIn) {
    router.navigate(['/dashboard']);
    return false;
  }

  return true;
};
