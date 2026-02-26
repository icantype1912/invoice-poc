import { Injectable, Inject, PLATFORM_ID } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { jwtDecode } from 'jwt-decode';
import { environment } from '../../../environments/environment';

function joinUrl(base: string, path: string): string {
  const b = (base ?? '').replace(/\/+$/, '');
  const p = (path ?? '').replace(/^\/+/, '');
  return `${b}/${p}`;
}

export interface LoginRequest { email?: string; password?: string; }
export interface SignupRequest { email?: string; password?: string; companyName?: string; address?: string; phoneNumber?: string; driveFolderId?: string; }

@Injectable({ providedIn: 'root' })
export class Auth {
  private api = joinUrl(environment.apiUrl, 'auth');
  private decoded: any | null = null;

  constructor(
    private http: HttpClient,
    @Inject(PLATFORM_ID) private platformId: Object
  ) { }

  login(data: LoginRequest): Observable<any> {
    const url = joinUrl(this.api, 'login');
    return this.http.post(url, data);
  }

  signup(data: SignupRequest): Observable<any> {
    const url = joinUrl(this.api, 'signup');
    return this.http.post(url, data);
  }

  getToken(): string | null {
    if (isPlatformBrowser(this.platformId)) {
      return localStorage.getItem('token');
    }
    return null;
  }

  setToken(token: string): void {
    if (isPlatformBrowser(this.platformId)) {
      localStorage.setItem('token', token);
      this.decoded = null;
    }
  }

  clearToken(): void {
    if (isPlatformBrowser(this.platformId)) {
      localStorage.removeItem('token');
      this.decoded = null;
    }
  }

  get isLoggedIn(): boolean {
    return !!this.getToken();
  }

  private getDecoded(): any | null {
    if (this.decoded) return this.decoded;
    const token = this.getToken();
    if (!token) return null;
    try {
      this.decoded = jwtDecode(token);
      return this.decoded;
    } catch {
      return null;
    }
  }

  getRole(): string | null {
    const decoded = this.getDecoded();
    if (!decoded) return null;
    // C# sets claims as either 'role' or the full schema URL
    return decoded.role || decoded['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] || null;
  }

  get isAdmin(): boolean {
    const role = this.getRole();
    return typeof role === 'string' && role.toLowerCase() === 'admin';
  }

  get isUser(): boolean {
    const role = this.getRole();
    return typeof role === 'string' && role.toLowerCase() === 'vendor';
  }

  getUserId(): string | null {
    const decoded = this.getDecoded();
    if (!decoded) return null;
    return decoded.sub || decoded.nameid || decoded['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'] || null;
  }

  getEmail(): string | null {
    const decoded = this.getDecoded();
    if (!decoded) return null;
    return decoded.email || decoded['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress'] || decoded.unique_name || null;
  }
}
