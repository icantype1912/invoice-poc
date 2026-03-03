import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { ApiService, JobQueueItem, JobStatus } from '../../core/services/api.service';
import { Auth } from '../../core/services/auth';
import { environment } from '../../../environments/environment';

type Vendor = { id: string; email: string; companyName?: string; role: number };

@Component({
  selector: 'app-job-queue',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './job-queue.html',
  styleUrls: ['./job-queue.css']
})
export class JobQueue implements OnInit {
  private api = inject(ApiService);
  private http = inject(HttpClient);
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

  // Issue #10: Raw JSON popup for completed jobs
  rawJsonJob = signal<JobQueueItem | null>(null);

  // Issue #8: Admin vendor filter
  vendors = signal<Vendor[]>([]);
  selectedVendorId = signal<string>('');

  ngOnInit(): void {
    if (this.isAdmin) this.loadVendors();
    this.loadJobs();
  }

  loadVendors(): void {
    this.http.get<Vendor[]>(`${environment.apiUrl}/admin/users`).subscribe({
      next: (users) => this.vendors.set((users || []).filter(u => u.role === 1)),
      error: () => { }
    });
  }

  onVendorChange(event: Event): void {
    this.selectedVendorId.set((event.target as HTMLSelectElement).value);
    this.page.set(1);
    this.loadJobs();
  }

  loadJobs(): void {
    const status = this.statusFilter() ?? undefined;
    this.api.getJobs(this.page(), this.pageSize, status).subscribe({
      next: (res) => {
        let jobs = res.jobs ?? [];
        // Issue #8: Filter by vendor on the client side if admin has selected a vendor
        const vendorId = this.selectedVendorId();
        if (this.isAdmin && vendorId) {
          jobs = jobs.filter(j => {
            const payload = typeof j.payloadJson === 'string' ? JSON.parse(j.payloadJson) : j.payloadJson;
            return payload?.uploader === vendorId;
          });
        }
        this.jobs.set(jobs);
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

  getPayloadEntries(job: JobQueueItem | null): any[] {
    if (!job) return [];
    try {
      // Prefer resultJson for COMPLETED jobs, otherwise use payloadJson
      const data = (job.status === 'COMPLETED' && job.resultJson)
        ? (typeof job.resultJson === 'string' ? JSON.parse(job.resultJson) : job.resultJson)
        : (typeof job.payloadJson === 'string' ? JSON.parse(job.payloadJson) : job.payloadJson);

      return Object.entries(data).map(([key, value]) => ({
        key: this.formatKey(key),
        value: typeof value === 'object' ? JSON.stringify(value) : value
      }));
    } catch (e) {
      return [{ key: 'Data', value: String(job.payloadJson) }];
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

  // Issue #2: Vendor click shows limited info; admin click shows full details
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

  // Issue #10: Show raw JSON popup for completed jobs
  showRawJson(job: JobQueueItem, event: Event): void {
    event.stopPropagation();
    if (job.status === 'COMPLETED') {
      this.rawJsonJob.set(job);
    }
  }

  closeRawJson(): void {
    this.rawJsonJob.set(null);
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

