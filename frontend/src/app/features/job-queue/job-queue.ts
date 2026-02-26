import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService, JobQueueItem, JobStatus } from '../../core/services/api.service';
import { Auth } from '../../core/services/auth';

@Component({
  selector: 'app-job-queue',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './job-queue.html',
  styleUrls: ['./job-queue.css']
})
export class JobQueue implements OnInit {
  private api = inject(ApiService);
  public auth = inject(Auth);
  get isAdmin() { return this.auth.isAdmin; }

  jobs = signal<JobQueueItem[]>([]);
  page = signal(1);
  pageSize = 50;
  total = signal(0);

  // Computed signal for total pages
  totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize)));

  selectedJob = signal<JobQueueItem | null>(null);
  statusFilter = signal<JobStatus | null>(null);

  ngOnInit(): void {
    this.loadJobs();
  }

  loadJobs(): void {
    const status = this.statusFilter() ?? undefined;
    this.api.getJobs(this.page(), this.pageSize, status).subscribe({
      next: (res) => {
        this.jobs.set(res.jobs ?? []);
        this.total.set(res.total ?? 0);
        this.selectedJob.set(null);
      },
      error: (err) => {
        console.error('Failed loading jobs', err);
        this.jobs.set([]);
        this.total.set(0);
        this.selectedJob.set(null);
      }
    });
  }

  nextPage(): void {
    if (this.page() < this.totalPages()) {
      this.page.update(p => p + 1);
      this.loadJobs();
    }
  }

  prevPage(): void {
    if (this.page() > 1) {
      this.page.update(p => p - 1);
      this.loadJobs();
    }
  }

  formatError(err: any): string {
    if (!err) return '';
    if (typeof err === 'string') {
      try {
        if (err.trim().startsWith('{')) {
          const parsed = JSON.parse(err);
          return parsed.message || parsed.error || JSON.stringify(parsed);
        }
      } catch (e) { }
      return err;
    }
    if (typeof err === 'object') {
      return err.message || err.error || JSON.stringify(err);
    }
    return String(err);
  }

  getPayloadEntries(payload: any): any[] {
    if (!payload) return [];
    try {
      const data = typeof payload === 'string' ? JSON.parse(payload) : payload;
      return Object.entries(data).map(([key, value]) => ({
        key: this.formatKey(key),
        value: typeof value === 'object' ? JSON.stringify(value) : value
      }));
    } catch (e) {
      return [{ key: 'Data', value: String(payload) }];
    }
  }

  private formatKey(key: string): string {
    return key
      .replace(/([A-Z])/g, ' $1')
      .replace(/^./, str => str.toUpperCase())
      .trim();
  }

  refresh(): void {
    this.loadJobs();
  }

  viewJob(id: string): void {
    this.api.getJobById(id).subscribe({
      next: (res) => {
        this.selectedJob.set(res);
      },
      error: (err) => {
        console.error('Failed loading job', err);
      }
    });
  }

  closeJob(): void {
    this.selectedJob.set(null);
  }

  requeueJob(id: string): void {
    if (!this.isAdmin) return;
    this.api.requeueJob(id).subscribe({
      next: () => {
        this.loadJobs();
      },
      error: (err) => {
        console.error('Failed to requeue job', err);
      }
    });
  }

  setStatus(status: JobStatus | null): void {
    this.statusFilter.set(status);
    this.page.set(1);
    this.loadJobs();
  }

  prettifyError(errorMessage: unknown): string {
    if (errorMessage == null) return '';
    const raw = typeof errorMessage === 'string' ? errorMessage : JSON.stringify(errorMessage);
    try {
      const parsed = JSON.parse(raw);
      const msg = parsed?.message ?? parsed?.error ?? raw;
      return String(msg).length > 220 ? String(msg).slice(0, 217) + '...' : String(msg);
    } catch {
      return raw.length > 220 ? raw.slice(0, 217) + '...' : raw;
    }
  }

  getPrettyJson(obj: any): string {
    return JSON.stringify(obj, null, 2);
  }
}
