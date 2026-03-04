import { TestBed, ComponentFixture } from '@angular/core/testing';
import { Signup } from './signup';
import { Auth } from '../../../core/services/auth';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { provideRouter } from '@angular/router';

describe('Signup Component', () => {
  let component: Signup;
  let fixture: ComponentFixture<Signup>;
  let mockAuth: { signup: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    mockAuth = {
      signup: vi.fn()
    };

    TestBed.resetTestingModule();

    TestBed.configureTestingModule({
      imports: [Signup],
      providers: [
        provideRouter([]),
        { provide: Auth, useValue: mockAuth }
      ]
    });

    fixture = TestBed.createComponent(Signup);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });


  it('should create component', () => {
    expect(component).toBeTruthy();
  });

  it('should toggle password visibility', () => {
    expect(component.showPassword()).toBe(false);
    component.togglePassword();
    expect(component.showPassword()).toBe(true);
  });

  it('should toggle confirm password visibility', () => {
    expect(component.showConfirmPassword()).toBe(false);
    component.toggleConfirmPassword();
    expect(component.showConfirmPassword()).toBe(true);
  });

  it('should show error if required fields missing', () => {
    component.signup();
    expect(component.errorMessage()).toBe(
      'Email, password, and company name are required'
    );
    expect(mockAuth.signup).not.toHaveBeenCalled();
  });

  it('should show error if passwords do not match', () => {
    component.email = 'a@test.com';
    component.password = 'Password1!';
    component.confirmPassword = 'Different1!';
    component.company = 'TestCo';

    component.signup();

    expect(component.errorMessage()).toBe('Passwords do not match');
    expect(mockAuth.signup).not.toHaveBeenCalled();
  });

  it('should call auth.signup on valid input', () => {
    mockAuth.signup.mockReturnValue(of({}));

    component.email = 'a@test.com';
    component.password = 'Password1!';
    component.confirmPassword = 'Password1!';
    component.company = 'TestCo';

    component.signup();

    expect(mockAuth.signup).toHaveBeenCalledWith({
      email: 'a@test.com',
      password: 'Password1!',
      companyName: 'TestCo'
    });

    expect(component.successMessage()).toContain('Signup successful');
    expect(component.isLoading()).toBe(false);
  });

  it('should handle 400 error', () => {
    mockAuth.signup.mockReturnValue(
      throwError(() => ({
        status: 400,
        error: { message: 'Bad request' }
      }))
    );

    component.email = 'a@test.com';
    component.password = 'Password1!';
    component.confirmPassword = 'Password1!';
    component.company = 'TestCo';

    component.signup();

    expect(component.errorMessage()).toBe('Bad request');
    expect(component.isLoading()).toBe(false);
  });

  it('should handle unexpected error', () => {
    mockAuth.signup.mockReturnValue(
      throwError(() => ({
        status: 500,
        error: {}
      }))
    );

    component.email = 'a@test.com';
    component.password = 'Password1!';
    component.confirmPassword = 'Password1!';
    component.company = 'TestCo';

    component.signup();

    expect(component.errorMessage()).toBe(
      'An unexpected error occurred. Please try again.'
    );
    expect(component.isLoading()).toBe(false);
  });


  it('should fail when password is less than 8 characters', () => {
    component.email = 'a@test.com';
    component.password = 'Ab1!';
    component.confirmPassword = 'Ab1!';
    component.company = 'TestCo';

    component.signup();

    expect(component.errorMessage()).toBe(
      'Password must be at least 8 characters'
    );
    expect(mockAuth.signup).not.toHaveBeenCalled();
  });

  it('should fail when missing lowercase character', () => {
    component.email = 'a@test.com';
    component.password = 'PASSWORD1!';
    component.confirmPassword = 'PASSWORD1!';
    component.company = 'TestCo';

    component.signup();

    expect(component.errorMessage()).toContain('lowercase');
    expect(mockAuth.signup).not.toHaveBeenCalled();
  });

  it('should fail when missing uppercase character', () => {
    component.email = 'a@test.com';
    component.password = 'password1!';
    component.confirmPassword = 'password1!';
    component.company = 'TestCo';

    component.signup();

    expect(component.errorMessage()).toContain('uppercase');
    expect(mockAuth.signup).not.toHaveBeenCalled();
  });

  it('should fail when missing number', () => {
    component.email = 'a@test.com';
    component.password = 'Password!';
    component.confirmPassword = 'Password!';
    component.company = 'TestCo';

    component.signup();

    expect(component.errorMessage()).toContain('number');
    expect(mockAuth.signup).not.toHaveBeenCalled();
  });

  it('should fail when missing special character', () => {
    component.email = 'a@test.com';
    component.password = 'Password1';
    component.confirmPassword = 'Password1';
    component.company = 'TestCo';

    component.signup();

    expect(component.errorMessage()).toContain('special');
    expect(mockAuth.signup).not.toHaveBeenCalled();
  });

  it('should handle 429 error', () => {
    mockAuth.signup.mockReturnValue(
      throwError(() => ({
        status: 429,
        error: { message: 'Too many signup attempts' }
      }))
    );

    component.email = 'a@test.com';
    component.password = 'Password1!';
    component.confirmPassword = 'Password1!';
    component.company = 'TestCo';

    component.signup();

    expect(component.errorMessage()).toBe('Too many signup attempts');
  });

  it('should handle prohibited registration message', () => {
    mockAuth.signup.mockReturnValue(
      throwError(() => ({
        status: 403,
        error: { message: 'Prohibited to register contact admin' }
      }))
    );

    component.email = 'a@test.com';
    component.password = 'Password1!';
    component.confirmPassword = 'Password1!';
    component.company = 'TestCo';

    component.signup();

    expect(component.errorMessage()).toBe(
      'Prohibited to register contact admin'
    );
  });

  it('should support capital Message property', () => {
    mockAuth.signup.mockReturnValue(
      throwError(() => ({
        status: 400,
        error: { Message: 'Capital error message' }
      }))
    );

    component.email = 'a@test.com';
    component.password = 'Password1!';
    component.confirmPassword = 'Password1!';
    component.company = 'TestCo';

    component.signup();

    expect(component.errorMessage()).toBe('Capital error message');
  });
});