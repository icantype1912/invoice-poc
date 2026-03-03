import { Component, OnInit, signal, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ApiService, FileChangeLog, LogsResponse } from '../../core/services/api.service';
import { Auth } from '../../core/services/auth';
import { environment } from '../../../environments/environment';

type Vendor = { id: string; email: string; companyName?: string; role: number };

@Component({
    selector: 'app-logs',
    standalone: true,
    imports: [DatePipe],
    templateUrl: './logs-component.html',
    styleUrl: './logs-component.css',
})
export class Logs implements OnInit {
    private api = inject(ApiService);
    private auth = inject(Auth);
    private http = inject(HttpClient);

    logs = signal<FileChangeLog[]>([]);
    page = signal(1);
    totalPages = signal(1);
    total = signal(0);
    changeTypeFilter = signal<string | null>(null);
    isLoading = signal(false);

    // Vendor filter
    vendors = signal<Vendor[]>([]);
    selectedVendorId = signal<string>('');

    get isAdmin() {
        return typeof (this.auth as any).isAdmin === 'function'
            ? (this.auth as any).isAdmin()
            : !!(this.auth as any).isAdmin;
    }

    ngOnInit() {
        if (this.isAdmin) this.loadVendors();
        this.loadLogs();
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
        this.loadLogs();
    }

    loadLogs() {
        this.isLoading.set(true);
        this.api.getLogs(this.page(), 50, this.changeTypeFilter() ?? undefined, this.selectedVendorId() || undefined).subscribe({
            next: (res: LogsResponse) => {
                this.logs.set(res.logs);
                this.totalPages.set(res.totalPages);
                this.total.set(res.total);
                this.isLoading.set(false);
            },
            error: (err) => {
                console.error('Failed loading logs', err);
                this.isLoading.set(false);
            }
        });
    }

    setChangeType(type: string | null) {
        this.changeTypeFilter.set(type);
        this.page.set(1);
        this.loadLogs();
    }

    nextPage() {
        if (this.page() < this.totalPages()) {
            this.page.update(p => p + 1);
            this.loadLogs();
        }
    }

    prevPage() {
        if (this.page() > 1) {
            this.page.update(p => p - 1);
            this.loadLogs();
        }
    }

    formatFileSize(bytes: number): string {
        if (!bytes) return '—';
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
        return (bytes / 1048576).toFixed(1) + ' MB';
    }

    securityLabel(status: string): string {
        if (!status) return '—';
        return status === 'Healthy' ? '✓ Healthy' : '✗ Unhealthy';
    }
}
