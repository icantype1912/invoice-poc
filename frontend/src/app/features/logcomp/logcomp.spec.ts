import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LogComp } from './logcomp';
import { ApiService } from '../../core/services/api.service';
import { Auth } from '../../core/services/auth';
import { HttpClient } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { describe, it, expect, beforeEach, vi } from 'vitest';

class MockApiService {
  getLogs = vi.fn();
}

class MockHttpClient {
  get = vi.fn();
}

class MockAuth {
  isAdmin = true;
}

describe('LogComp', () => {
  let component: LogComp;
  let fixture: ComponentFixture<LogComp>;
  let api: MockApiService;
  let http: MockHttpClient;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LogComp],
      providers: [
        { provide: ApiService, useClass: MockApiService },
        { provide: HttpClient, useClass: MockHttpClient },
        { provide: Auth, useClass: MockAuth }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(LogComp);
    component = fixture.componentInstance;

    api = TestBed.inject(ApiService) as unknown as MockApiService;
    http = TestBed.inject(HttpClient) as unknown as MockHttpClient;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load vendors if admin on init', () => {
    http.get.mockReturnValue(of([{ id: '1', email: 'v@test.com', role: 1 }]));
    api.getLogs.mockReturnValue(of({ logs: [], totalPages: 1, total: 0 }));

    component.ngOnInit();

    expect(http.get).toHaveBeenCalled();
  });

  it('should load logs', () => {
    api.getLogs.mockReturnValue(of({
      logs: [{ id: '1', uploadedByVendorId: '1' }],
      totalPages: 2,
      total: 1
    }));

    component.loadLogs();

    expect(api.getLogs).toHaveBeenCalled();
    expect(component.logs().length).toBe(1);
    expect(component.totalPages()).toBe(2);
  });

  it('should handle loadLogs error', () => {
    api.getLogs.mockReturnValue(throwError(() => new Error('fail')));

    component.loadLogs();

    expect(component.isLoading()).toBe(false);
  });

  it('should filter logs by vendor when admin selects vendor', () => {
    component.selectedVendorId.set('vendor1');

    api.getLogs.mockReturnValue(of({
      logs: [
        { id: '1', uploadedByVendorId: 'vendor1' },
        { id: '2', uploadedByVendorId: 'vendor2' }
      ],
      totalPages: 1,
      total: 2
    }));

    component.loadLogs();

    expect(component.logs().length).toBe(1);
  });

  it('should go to next page', () => {
    api.getLogs.mockReturnValue(of({ logs: [], totalPages: 5, total: 0 }));

    component.totalPages.set(5);

    component.nextPage();

    expect(component.page()).toBe(2);
  });

  it('should go to previous page', () => {
    api.getLogs.mockReturnValue(of({ logs: [], totalPages: 5, total: 0 }));

    component.page.set(2);

    component.prevPage();

    expect(component.page()).toBe(1);
  });

  it('should format file size', () => {
    expect(component.formatFileSize(500)).toBe('500 B');
    expect(component.formatFileSize(2048)).toContain('KB');
    expect(component.formatFileSize(10485760)).toContain('MB');
  });

  it('should return correct security label', () => {
    expect(component.securityLabel('Healthy')).toContain('Healthy');
    expect(component.securityLabel('Unhealthy')).toContain('Unhealthy');
    expect(component.securityLabel('N/A')).toContain('N/A');
  });
});
