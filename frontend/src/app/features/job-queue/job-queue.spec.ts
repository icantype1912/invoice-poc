import { ComponentFixture, TestBed } from '@angular/core/testing';
import { JobQueue } from './job-queue';
import { ApiService } from '../../core/services/api.service';
import { Auth } from '../../core/services/auth';
import { HttpClient } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { describe, it, expect, beforeEach, vi } from 'vitest';

class MockApiService {
  getJobs = vi.fn();
  getJobById = vi.fn();
  requeueJob = vi.fn();
}

class MockHttpClient {
  get = vi.fn();
}

class MockAuth {
  isAdmin = true;
}

describe('JobQueue', () => {
  let component: JobQueue;
  let fixture: ComponentFixture<JobQueue>;
  let api: MockApiService;
  let http: MockHttpClient;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [JobQueue],
      providers: [
        { provide: ApiService, useClass: MockApiService },
        { provide: HttpClient, useClass: MockHttpClient },
        { provide: Auth, useClass: MockAuth }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(JobQueue);
    component = fixture.componentInstance;

    api = TestBed.inject(ApiService) as unknown as MockApiService;
    http = TestBed.inject(HttpClient) as unknown as MockHttpClient;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load vendors on init if admin', () => {
    http.get.mockReturnValue(of([
      { id: '1', email: 'v@test.com', role: 1 }
    ]));

    api.getJobs.mockReturnValue(of({ jobs: [], total: 0 }));

    component.ngOnInit();

    expect(http.get).toHaveBeenCalled();
  });

  it('should load jobs', () => {
    api.getJobs.mockReturnValue(of({
      jobs: [{ id: '1', status: 'PENDING', payloadJson: '{}'}],
      total: 1
    }));

    component.loadJobs();

    expect(api.getJobs).toHaveBeenCalled();
    expect(component.jobs().length).toBe(1);
    expect(component.total()).toBe(1);
  });

  it('should handle loadJobs error', () => {
    api.getJobs.mockReturnValue(throwError(() => new Error('fail')));

    component.loadJobs();

    expect(component.jobs()).toEqual([]);
    expect(component.total()).toBe(0);
  });

  it('should go to next page', () => {
    api.getJobs.mockReturnValue(of({ jobs: [], total: 200 }));

    component.total.set(200);

    component.nextPage();

    expect(component.page()).toBe(2);
  });

  it('should go to previous page', () => {
    api.getJobs.mockReturnValue(of({ jobs: [], total: 200 }));

    component.page.set(2);

    component.prevPage();

    expect(component.page()).toBe(1);
  });


  it('should view job', () => {
    api.getJobById.mockReturnValue(of({ id: '1', status: 'PENDING' }));

    component.viewJob('1');

    expect(api.getJobById).toHaveBeenCalledWith('1');
  });

  it('should close job', () => {
    component.selectedJob.set({ id: '1' } as any);

    component.closeJob();

    expect(component.selectedJob()).toBeNull();
  });

  it('should show raw json for completed job', () => {
    const job = { id: '1', status: 'COMPLETED' } as any;

    component.showRawJson(job, new Event('click'));

    expect(component.rawJsonJob()).toEqual(job);
  });

  it('should prettify error', () => {
    const err = JSON.stringify({ message: 'something broke' });

    const result = component.prettifyError(err);

    expect(result).toContain('something broke');
  });
});
