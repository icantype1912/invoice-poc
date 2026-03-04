import { TestBed, ComponentFixture } from '@angular/core/testing';
import { Admin } from './admin';
import { Auth } from '../../core/services/auth';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { environment } from '../../../environments/environment';
import { vi } from 'vitest';

describe('Admin Component', () => {
  let component: Admin;
  let fixture: ComponentFixture<Admin>;
  let httpMock: HttpTestingController;
  let mockAuth: { getUserId: ReturnType<typeof vi.fn> };

  const baseUrl = environment.apiUrl + '/admin';

  beforeEach(() => {
    mockAuth = {
      getUserId: vi.fn()
    };

    TestBed.resetTestingModule();

    TestBed.configureTestingModule({
      imports: [Admin, HttpClientTestingModule],
      providers: [
        { provide: Auth, useValue: mockAuth }
      ]
    });

    fixture = TestBed.createComponent(Admin);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  // =========================================
  // LIFECYCLE
  // =========================================

  it('should call refresh on init', () => {
    const refreshSpy = vi
      .spyOn(component, 'refresh')
      .mockImplementation(() => {});

    component.ngOnInit();

    expect(refreshSpy).toHaveBeenCalled();
  });

  it('refresh should call loadPending and loadUsers', () => {
    const pendingSpy = vi
      .spyOn(component, 'loadPending')
      .mockImplementation(() => {});
    const usersSpy = vi
      .spyOn(component, 'loadUsers')
      .mockImplementation(() => {});

    component.refresh();

    expect(pendingSpy).toHaveBeenCalled();
    expect(usersSpy).toHaveBeenCalled();
  });

  // =========================================
  // LOAD DATA
  // =========================================

  it('should load pending users', () => {
    const mockData = [
      { id: '1', email: 'a@test.com', role: 1, status: 0 }
    ];

    component.loadPending();

    const req = httpMock.expectOne(`${baseUrl}/users/pending`);
    expect(req.request.method).toBe('GET');

    req.flush(mockData);

    expect(component.pendingUsers()).toEqual(mockData);
  });

  it('should load users', () => {
    const mockData = [
      { id: '2', email: 'b@test.com', role: 0, status: 1 }
    ];

    component.loadUsers();

    const req = httpMock.expectOne(`${baseUrl}/users`);
    expect(req.request.method).toBe('GET');

    req.flush(mockData);

    expect(component.users()).toEqual(mockData);
  });

  // =========================================
  // ACTION METHODS
  // =========================================

  it('approve should call API and refresh', () => {
    const refreshSpy = vi
      .spyOn(component, 'refresh')
      .mockImplementation(() => {});

    component.approve('123');

    const req = httpMock.expectOne(`${baseUrl}/users/123/approve`);
    expect(req.request.method).toBe('POST');

    req.flush({});

    expect(refreshSpy).toHaveBeenCalled();
  });

  it('reject should call API and refresh', () => {
    const refreshSpy = vi
      .spyOn(component, 'refresh')
      .mockImplementation(() => {});

    component.reject('123');

    const req = httpMock.expectOne(`${baseUrl}/users/123/reject`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      reason: 'Rejected by admin'
    });

    req.flush({});

    expect(refreshSpy).toHaveBeenCalled();
  });

  it('promote should call API and refresh', () => {
    const refreshSpy = vi
      .spyOn(component, 'refresh')
      .mockImplementation(() => {});

    component.promote('123');

    const req = httpMock.expectOne(`${baseUrl}/users/123/promote`);
    expect(req.request.method).toBe('POST');

    req.flush({});

    expect(refreshSpy).toHaveBeenCalled();
  });

  it('delete should call API and refresh', () => {
    const refreshSpy = vi
      .spyOn(component, 'refresh')
      .mockImplementation(() => {});

    component.delete('123');

    const req = httpMock.expectOne(`${baseUrl}/users/123`);
    expect(req.request.method).toBe('DELETE');

    req.flush({});

    expect(refreshSpy).toHaveBeenCalled();
  });

  it('unlock should call API and refresh', () => {
    const refreshSpy = vi
      .spyOn(component, 'refresh')
      .mockImplementation(() => {});

    component.unlock('123');

    const req = httpMock.expectOne(`${baseUrl}/users/123/unlock`);
    expect(req.request.method).toBe('POST');

    req.flush({});

    expect(refreshSpy).toHaveBeenCalled();
  });

  // =========================================
  // AUTH LOGIC
  // =========================================

  it('isCurrentUser should return true for matching id', () => {
    mockAuth.getUserId.mockReturnValue('abc');

    expect(component.isCurrentUser('abc')).toBe(true);
  });

  it('isCurrentUser should return false for non-matching id', () => {
    mockAuth.getUserId.mockReturnValue('abc');

    expect(component.isCurrentUser('xyz')).toBe(false);
  });

  // =========================================
  // LABEL HELPERS
  // =========================================

  it('roleLabel should map correctly', () => {
    expect(component.roleLabel(0)).toBe('Admin');
    expect(component.roleLabel(1)).toBe('Vendor');
    expect(component.roleLabel(99)).toBe('Unknown');
  });

  it('statusLabel should map correctly', () => {
    expect(component.statusLabel(0)).toBe('Pending');
    expect(component.statusLabel(1)).toBe('Approved');
    expect(component.statusLabel(2)).toBe('Rejected');
    expect(component.statusLabel(3)).toBe('Locked');
    expect(component.statusLabel(99)).toBe('Unknown');
  });

});