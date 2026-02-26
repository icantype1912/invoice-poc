import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Auth } from '../../../core/services/auth';
import { Router } from '@angular/router';

@Component({
  selector: 'app-signup',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './signup.html',
  styleUrl: './signup.css',
})
export class Signup {
  email = '';
  password = '';
  confirmPassword = '';
  company = '';

  errorMessage = signal('');
  successMessage = signal('');
  isLoading = signal(false);
  showPassword = signal(false);
  showConfirmPassword = signal(false);

  togglePassword() {
    this.showPassword.update(v => !v);
  }

  toggleConfirmPassword() {
    this.showConfirmPassword.update(v => !v);
  }

  constructor(
    private auth: Auth,
    private router: Router
  ) { }

  signup() {
    this.errorMessage.set('');
    this.successMessage.set('');

    if (!this.email || !this.password || !this.company) {
      this.errorMessage.set('Email, password, and company name are required');
      return;
    }

    if (this.password !== this.confirmPassword) {
      this.errorMessage.set('Passwords do not match');
      return;
    }

    // Password Complexity Checks
    const hasLowercase = /[a-z]/.test(this.password);
    const hasUppercase = /[A-Z]/.test(this.password);
    const hasDigit = /\d/.test(this.password);
    const hasSpecial = /[!@#$%^&*(),.?":{}|<>]/.test(this.password);

    if (this.password.length < 8) {
      this.errorMessage.set('Password must be at least 8 characters');
      return;
    }
    if (!hasLowercase) {
      this.errorMessage.set('Password must include at least one lowercase character');
      return;
    }
    if (!hasUppercase) {
      this.errorMessage.set('Password must include at least one uppercase character');
      return;
    }
    if (!hasDigit) {
      this.errorMessage.set('Password must include at least one number');
      return;
    }
    if (!hasSpecial) {
      this.errorMessage.set('Password must include at least one special character');
      return;
    }

    this.isLoading.set(true);

    this.auth.signup({
      email: this.email,
      password: this.password,
      companyName: this.company
    }).subscribe({
      next: () => {
        this.isLoading.set(false);
        this.successMessage.set('Signup successful! Your account is pending approval. You will be able to login once an admin approves your account.');
      },
      error: (err) => {
        this.isLoading.set(false);

        const msg = err.error?.message || err.error?.Message;
        if (msg === "Prohibited to register contact admin") {
          this.errorMessage.set(msg);
        } else if (err.status === 400) {
          this.errorMessage.set(msg || 'Invalid signup data. Please check your inputs.');
        } else if (err.status === 429) {
          this.errorMessage.set(msg || 'Too many signup attempts. Please try again later.');
        } else {
          this.errorMessage.set('An unexpected error occurred. Please try again.');
        }
      }
    });
  }
}