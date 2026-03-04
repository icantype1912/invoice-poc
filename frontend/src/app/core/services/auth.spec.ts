import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { PLATFORM_ID } from '@angular/core';
import { Auth } from './auth';
import { vi } from 'vitest';

// Proper ESM mock
vi.mock('jwt-decode', () => ({
  jwtDecode: vi.fn()
}));

import { jwtDecode } from 'jwt-decode';

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
    vi.clearAllMocks();
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
  // TOKEN STORAGE (Browser)
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
    (jwtDecode as any).mockReturnValue({ role: 'Admin' });
    service.setToken('token');
    expect(service.getRole()).toBe('Admin');
  });

  it('should extract role from C# schema claim', () => {
    (jwtDecode as any).mockReturnValue({
      'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': 'Vendor'
    });

    service.setToken('token');
    expect(service.getRole()).toBe('Vendor');
  });

  it('should return null when role claim missing', () => {
    (jwtDecode as any).mockReturnValue({});
    service.setToken('token');
    expect(service.getRole()).toBeNull();
  });

  it('should return true for isAdmin (case insensitive)', () => {
    (jwtDecode as any).mockReturnValue({ role: 'ADMIN' });
    service.setToken('token');
    expect(service.isAdmin).toBe(true);
  });

  it('should return false for isAdmin when role is vendor', () => {
    (jwtDecode as any).mockReturnValue({ role: 'vendor' });
    service.setToken('token');
    expect(service.isAdmin).toBe(false);
  });

  it('should return true for isUser when role is vendor', () => {
    (jwtDecode as any).mockReturnValue({ role: 'vendor' });
    service.setToken('token');
    expect(service.isUser).toBe(true);
  });

  // ===================================================
  // USER ID EXTRACTION
  // ===================================================

  it('should extract userId from sub claim', () => {
    (jwtDecode as any).mockReturnValue({ sub: '123' });
    service.setToken('token');
    expect(service.getUserId()).toBe('123');
  });

  it('should return null when userId claim missing', () => {
    (jwtDecode as any).mockReturnValue({});
    service.setToken('token');
    expect(service.getUserId()).toBeNull();
  });

  // ===================================================
  // EMAIL EXTRACTION
  // ===================================================

  it('should extract email from email claim', () => {
    (jwtDecode as any).mockReturnValue({ email: 'a@test.com' });
    service.setToken('token');
    expect(service.getEmail()).toBe('a@test.com');
  });

  it('should fallback to schema email claim', () => {
    (jwtDecode as any).mockReturnValue({
      'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress': 'b@test.com'
    });

    service.setToken('token');
    expect(service.getEmail()).toBe('b@test.com');
  });

  it('should return null when email missing', () => {
    (jwtDecode as any).mockReturnValue({});
    service.setToken('token');
    expect(service.getEmail()).toBeNull();
  });

  // ===================================================
  // ERROR HANDLING
  // ===================================================

  it('should return null if jwt decoding throws', () => {
    (jwtDecode as any).mockImplementation(() => {
      throw new Error('Invalid token');
    });

    service.setToken('bad-token');

    expect(service.getRole()).toBeNull();
    expect(service.getUserId()).toBeNull();
    expect(service.getEmail()).toBeNull();
  });

  // ===================================================
  // DECODE CACHING BEHAVIOR
  // ===================================================

  it('should cache decoded token and not decode twice', () => {
    const mock = jwtDecode as any;
    mock.mockReturnValue({ role: 'Admin' });

    service.setToken('token');

    service.getRole();
    service.getRole();

    expect(mock).toHaveBeenCalledTimes(1);
  });

});