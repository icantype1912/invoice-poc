import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { PLATFORM_ID } from '@angular/core';
import { Auth } from './auth';
import { vi } from 'vitest';
import * as jwt from 'jwt-decode';

describe('Auth Service (Senior Suite)', () => {
  let service: Auth;
  let httpMock: HttpTestingController;
  function setup(platform: 'browser' | 'server' = 'browser') {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [
        Auth,
        { provide: PLATFORM_ID, useValue: platform }
      ]
    });

    service = TestBed.inject(Auth);
    httpMock = TestBed.inject(HttpTestingController);
  }

  beforeEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
    setup('browser');
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  // ===================================================
  // HTTP TESTS
  // ===================================================

  it('should call login API with correct payload', () => {
    const payload = { email: 'a@test.com', password: '1234' };
    const mockResponse = { token: 'abc' };

    service.login(payload).subscribe(res => {
      expect(res).toEqual(mockResponse);
    });

    const req = httpMock.expectOne(r => r.url.endsWith('/auth/login'));

    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);

    req.flush(mockResponse);
  });

  it('should call signup API with correct payload', () => {
    const payload = { email: 'b@test.com', password: '1234' };

    service.signup(payload).subscribe();

    const req = httpMock.expectOne(r => r.url.endsWith('/auth/signup'));

    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);

    req.flush({});
  });

  // ===================================================
  // TOKEN STORAGE
  // ===================================================

  it('should store and retrieve token in browser', () => {
    service.setToken('abc');
    expect(service.getToken()).toBe('abc');
  });

  it('should clear token', () => {
    service.setToken('abc');
    service.clearToken();
    expect(service.getToken()).toBeNull();
  });

  it('should return false for isLoggedIn when no token', () => {
    expect(service.isLoggedIn).toBe(false);
  });

  it('should return true for isLoggedIn when token exists', () => {
    service.setToken('abc');
    expect(service.isLoggedIn).toBe(true);
  });

  // ===================================================
  // ROLE EXTRACTION
  // ===================================================

  it('should extract role from role claim', () => {
    vi.spyOn(jwt, 'jwtDecode').mockReturnValue({ role: 'Admin' } as any);

    service.setToken('token');
    expect(service.getRole()).toBe('Admin');
  });

  it('should extract role from C# schema claim', () => {
    vi.spyOn(jwt, 'jwtDecode').mockReturnValue({
      'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': 'Vendor'
    } as any);

    service.setToken('token');
    expect(service.getRole()).toBe('Vendor');
  });

  it('should return null when role claim missing', () => {
    vi.spyOn(jwt, 'jwtDecode').mockReturnValue({} as any);

    service.setToken('token');
    expect(service.getRole()).toBeNull();
  });

  it('should return true for isAdmin (case insensitive)', () => {
    vi.spyOn(jwt, 'jwtDecode').mockReturnValue({ role: 'ADMIN' } as any);

    service.setToken('token');
    expect(service.isAdmin).toBe(true);
  });

  it('should return false for isAdmin when role is vendor', () => {
    vi.spyOn(jwt, 'jwtDecode').mockReturnValue({ role: 'vendor' } as any);

    service.setToken('token');
    expect(service.isAdmin).toBe(false);
  });

  it('should return true for isUser when role is vendor', () => {
    vi.spyOn(jwt, 'jwtDecode').mockReturnValue({ role: 'vendor' } as any);

    service.setToken('token');
    expect(service.isUser).toBe(true);
  });

  // ===================================================
  // USER ID EXTRACTION
  // ===================================================

  it('should extract userId from sub claim', () => {
    vi.spyOn(jwt, 'jwtDecode').mockReturnValue({ sub: '123' } as any);

    service.setToken('token');
    expect(service.getUserId()).toBe('123');
  });

  it('should return null when userId claim missing', () => {
    vi.spyOn(jwt, 'jwtDecode').mockReturnValue({} as any);

    service.setToken('token');
    expect(service.getUserId()).toBeNull();
  });

  // ===================================================
  // EMAIL EXTRACTION
  // ===================================================

  it('should extract email from email claim', () => {
    vi.spyOn(jwt, 'jwtDecode').mockReturnValue({ email: 'a@test.com' } as any);

    service.setToken('token');
    expect(service.getEmail()).toBe('a@test.com');
  });

  it('should fallback to schema email claim', () => {
    vi.spyOn(jwt, 'jwtDecode').mockReturnValue({
      'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress': 'b@test.com'
    } as any);

    service.setToken('token');
    expect(service.getEmail()).toBe('b@test.com');
  });

  it('should return null when email missing', () => {
    vi.spyOn(jwt, 'jwtDecode').mockReturnValue({} as any);

    service.setToken('token');
    expect(service.getEmail()).toBeNull();
  });

  // ===================================================
  // ERROR HANDLING
  // ===================================================

  it('should return null if jwt decoding throws', () => {
    vi.spyOn(jwt, 'jwtDecode').mockImplementation(() => {
      throw new Error('Invalid token');
    });

    service.setToken('bad-token');

    expect(service.getRole()).toBeNull();
    expect(service.getUserId()).toBeNull();
    expect(service.getEmail()).toBeNull();
  });

  // ===================================================
  // DECODE CACHING
  // ===================================================

  it('should cache decoded token and not decode twice', () => {
    const mock = vi.spyOn(jwt, 'jwtDecode').mockReturnValue({ role: 'Admin' } as any);

    service.setToken('token');

    service.getRole();
    service.getRole();

    expect(mock).toHaveBeenCalledTimes(1);
  });
});