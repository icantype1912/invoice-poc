import { Component, OnInit, signal, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApiService, FileChangeLog, LogsResponse } from '../../core/services/api.service';
import { Auth } from '../../core/services/auth';

@Component({
    selector: 'app-logs',
    standalone: true,
    imports: [DatePipe],
    templateUrl: './logcomp.html',
    styleUrl: './logcomp.css',
})
export class LogComp implements OnInit {
    private api = inject(ApiService);
    private auth = inject(Auth);

    logs = signal<FileChangeLog[]>([]);
    page = signal(1);
    totalPages = signal(1);
    total = signal(0);
    changeTypeFilter = signal<string | null>(null);
    isLoading = signal(false);
    get isAdmin() { return this.auth.isAdmin; }

    ngOnInit() {
        this.loadLogs();
    }

    loadLogs() {
        this.isLoading.set(true);
        this.api.getLogs(this.page(), 50, this.changeTypeFilter() ?? undefined).subscribe({
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
