import { TestBed, ComponentFixture } from '@angular/core/testing';
import { Login } from './login';
import { Auth } from '../../../core/services/auth';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

describe('Login Component (Full Coverage)', () => {
  let component: Login;
  let fixture: ComponentFixture<Login>;
  let mockAuth: {
    login: ReturnType<typeof vi.fn>;
    setToken: ReturnType<typeof vi.fn>;
  };
  let router: Router;

  beforeEach(() => {
    mockAuth = {
      login: vi.fn(),
      setToken: vi.fn()
    };

    TestBed.resetTestingModule();

    TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        provideRouter([]), // real lightweight router
        { provide: Auth, useValue: mockAuth }
      ]
    });

    fixture = TestBed.createComponent(Login);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);

    vi.spyOn(router, 'navigate').mockResolvedValue(true);

    fixture.detectChanges();
  });

  // =========================
  // BASIC
  // =========================

  it('should create component', () => {
    expect(component).toBeTruthy();
  });

  it('should toggle password visibility', () => {
    expect(component.showPassword()).toBe(false);
    component.togglePassword();
    expect(component.showPassword()).toBe(true);
  });

  // =========================
  // VALIDATION
  // =========================

  it('should show error if fields missing', () => {
    component.login();
    expect(component.errorMessage()).toBe(
      'Email and password are required'
    );
    expect(mockAuth.login).not.toHaveBeenCalled();
  });

  // =========================
  // SUCCESS
  // =========================

  it('should login successfully and navigate', () => {
    mockAuth.login.mockReturnValue(of({ accessToken: 'abc-token' }));

    component.email = 'a@test.com';
    component.password = 'Password1!';

    component.login();

    expect(mockAuth.login).toHaveBeenCalledWith({
      email: 'a@test.com',
      password: 'Password1!'
    });

    expect(mockAuth.setToken).toHaveBeenCalledWith('abc-token');
    expect(router.navigate).toHaveBeenCalledWith(['/dashboard']);
    expect(component.isLoading()).toBe(false);
  });

  // =========================
  // ERROR CASES
  // =========================

  it('should handle 401 error', () => {
    mockAuth.login.mockReturnValue(
      throwError(() => ({ status: 401 }))
    );

    component.email = 'a@test.com';
    component.password = 'Password1!';

    component.login();

    expect(component.errorMessage()).toBe(
      'Invalid email or password'
    );
    expect(component.isLoading()).toBe(false);
  });

  it('should handle 403 error', () => {
    mockAuth.login.mockReturnValue(
      throwError(() => ({ status: 403 }))
    );

    component.email = 'a@test.com';
    component.password = 'Password1!';

    component.login();

    expect(component.errorMessage()).toContain('not been approved');
  });

  it('should handle 429 error with message', () => {
    mockAuth.login.mockReturnValue(
      throwError(() => ({
        status: 429,
        error: { message: 'Too many attempts' }
      }))
    );

    component.email = 'a@test.com';
    component.password = 'Password1!';

    component.login();

    expect(component.errorMessage()).toBe('Too many attempts');
  });

  it('should handle 429 error without message', () => {
    mockAuth.login.mockReturnValue(
      throwError(() => ({
        status: 429,
        error: {}
      }))
    );

    component.email = 'a@test.com';
    component.password = 'Password1!';

    component.login();

    expect(component.errorMessage()).toBe(
      'Too many login attempts. Please try again later.'
    );
  });

  it('should handle 400 error with message', () => {
    mockAuth.login.mockReturnValue(
      throwError(() => ({
        status: 400,
        error: { message: 'Bad request' }
      }))
    );

    component.email = 'a@test.com';
    component.password = 'Password1!';

    component.login();

    expect(component.errorMessage()).toBe('Bad request');
  });

  it('should handle 400 error without message', () => {
    mockAuth.login.mockReturnValue(
      throwError(() => ({
        status: 400,
        error: {}
      }))
    );

    component.email = 'a@test.com';
    component.password = 'Password1!';

    component.login();

    expect(component.errorMessage()).toBe(
      'Invalid email or password'
    );
  });

  it('should handle unexpected error', () => {
    mockAuth.login.mockReturnValue(
      throwError(() => ({ status: 500 }))
    );

    component.email = 'a@test.com';
    component.password = 'Password1!';

    component.login();

    expect(component.errorMessage()).toBe(
      'An unexpected error occurred. Please try again.'
    );
  });
});