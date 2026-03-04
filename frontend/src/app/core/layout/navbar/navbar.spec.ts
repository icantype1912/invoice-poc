import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Navbar } from './navbar';
import { provideRouter, Router } from '@angular/router';
import { Theme } from '../../services/theme';
import { Auth } from '../../services/auth';
import { ChatbotService } from '../../services/chatbot.service';
import { signal } from '@angular/core';
import { describe, it, expect, beforeEach, vi } from 'vitest';

class MockTheme {
  dark = signal(false);

  isDark() {
    return this.dark();
  }

  toggle() {
    this.dark.set(!this.dark());
  }
}

class MockAuth {
  isLoggedIn = true;
  isAdmin = false;
  isUser = true;

  getEmail() {
    return 'test.user@example.com';
  }

  getRole() {
    return 'Vendor';
  }

  clearToken = vi.fn();
}

class MockChatbotService {}

describe('Navbar', () => {
  let component: Navbar;
  let fixture: ComponentFixture<Navbar>;
  let theme: MockTheme;
  let auth: MockAuth;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Navbar],
      providers: [
        provideRouter([]), // ✅ Provides Router + ActivatedRoute
        { provide: Theme, useClass: MockTheme },
        { provide: Auth, useClass: MockAuth },
        { provide: ChatbotService, useClass: MockChatbotService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Navbar);
    component = fixture.componentInstance;

    theme = TestBed.inject(Theme) as unknown as MockTheme;
    auth = TestBed.inject(Auth) as unknown as MockAuth;
    router = TestBed.inject(Router);

    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should compute logo based on theme', () => {
    theme.dark.set(true);
    expect(component.logoSrc()).toBe('/logo.svg');

    theme.dark.set(false);
    expect(component.logoSrc()).toBe('/logolight.svg');
  });

  it('should toggle theme', () => {
    const initial = theme.isDark();
    component.toggleTheme();
    expect(theme.isDark()).toBe(!initial);
  });

  it('should toggle dropdown', () => {
    expect(component.dropdownOpen()).toBe(false);
    component.toggleDropdown();
    expect(component.dropdownOpen()).toBe(true);
  });

  it('should close dropdown on outside click', () => {
    component.dropdownOpen.set(true);

    const event = {
      target: document.createElement('div'),
    } as unknown as MouseEvent;

    component.onDocumentClick(event);

    expect(component.dropdownOpen()).toBe(false);
  });

  it('should return correct user initials', () => {
    expect(component.getUserInitials()).toBe('TU');
  });

  it('should return correct role label', () => {
    expect(component.getRoleLabel()).toBe('Vendor');
  });

  it('should detect landing page', () => {
    Object.defineProperty(router, 'url', { value: '/', writable: true });
    expect(component.isLandingPage).toBe(true);

    Object.defineProperty(router, 'url', { value: '/dashboard', writable: true });
    expect(component.isLandingPage).toBe(false);
  });

  it('should logout and navigate to login', () => {
    const navigateSpy = vi.spyOn(router, 'navigate');

    component.logout();

    expect(auth.clearToken).toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith(['/login']);
  });

  it('should show chatbot toggle when logged in and on dashboard', () => {
    auth.isLoggedIn = true;
    Object.defineProperty(router, 'url', { value: '/dashboard', writable: true });

    expect(component.showChatToggle).toBe(true);
  });
});
