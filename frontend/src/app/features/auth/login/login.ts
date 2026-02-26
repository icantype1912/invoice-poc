import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Auth } from '../../../core/services/auth';
import { Router } from '@angular/router';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  constructor(
    private auth: Auth,
    private router: Router
  ) { }

  email = '';
  password = '';
  errorMessage = signal('');
  isLoading = signal(false);
  showPassword = signal(false);

  togglePassword() {
    this.showPassword.update(v => !v);
  }

  login() {
    this.errorMessage.set('');

    if (!this.email || !this.password) {
      this.errorMessage.set('Email and password are required');
      return;
    }

    this.isLoading.set(true);

    this.auth.login({
      email: this.email,
      password: this.password,
    }).subscribe({
      next: (res) => {
        this.isLoading.set(false);
        this.auth.setToken(res.accessToken);
        this.router.navigate(['/dashboard']);
      },
      error: (err) => {
        this.isLoading.set(false);

        if (err.status === 401) {
          this.errorMessage.set('Invalid email or password');
        } else if (err.status === 403) {
          this.errorMessage.set('Your account has not been approved yet. Please wait for admin approval.');
        } else if (err.status === 429) {
          const msg = err.error?.message || 'Too many login attempts. Please try again later.';
          this.errorMessage.set(msg);
        } else if (err.status === 400) {
          const msg = err.error?.message || 'Invalid email or password';
          this.errorMessage.set(msg);
        } else {
          this.errorMessage.set('An unexpected error occurred. Please try again.');
        }
      }
    });
  }
}
