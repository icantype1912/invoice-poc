import { Component, OnInit, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { ApiService, InvoiceDto, InvoicesResponse, InvalidInvoice, InvalidInvoicesResponse } from '../../core/services/api.service';
import { Auth } from '../../core/services/auth';

@Component({
  selector: 'app-invoices',
  standalone: true,
  imports: [CurrencyPipe, DatePipe],
  templateUrl: './invoices.html',
  styleUrls: ['./invoices.css']
})
export class Invoices implements OnInit {
  private api = inject(ApiService);
  private auth = inject(Auth);

  get isAdmin() {
    return typeof (this.auth as any).isAdmin === 'function'
      ? (this.auth as any).isAdmin()
      : !!(this.auth as any).isAdmin;
  }

  validInvoices = signal<InvoiceDto[]>([]);
  validPage = signal(1);
  validTotalPages = signal(1);
  validTotal = signal(0);
  validOpen = signal(true);

  invalidInvoices = signal<InvalidInvoice[]>([]);
  invalidPage = signal(1);
  invalidTotalPages = signal(1);
  invalidTotal = signal(0);
  invalidOpen = signal(false);

  expandedInvoiceId = signal<string | null>(null);
  isLoading = signal(false);

  ngOnInit(): void {
    this.loadValid();
    this.loadInvalid();
  }

  loadValid(): void {
    this.isLoading.set(true);
    this.api.getInvoices(this.validPage(), 20).subscribe({
      next: (res: InvoicesResponse) => {
        this.validInvoices.set(res.invoices ?? []);
        this.validTotalPages.set(res.totalPages ?? 1);
        this.validTotal.set(res.total ?? 0);
        this.isLoading.set(false);
      },
      error: (err) => {
        console.error('Failed loading invoices', err);
        this.validInvoices.set([]);
        this.validTotalPages.set(1);
        this.validTotal.set(0);
        this.isLoading.set(false);
      }
    });
  }

  loadInvalid(): void {
    this.api.getInvalidInvoices(this.invalidPage(), 20).subscribe({
      next: (res: InvalidInvoicesResponse) => {
        this.invalidInvoices.set(res.data ?? []);
        this.invalidTotalPages.set(res.totalPages ?? 1);
        this.invalidTotal.set(res.totalCount ?? 0);
      },
      error: (err) => {
        console.error('Failed loading invalid invoices', err);
      }
    });
  }

  toggleValid(): void {
    this.validOpen.update(v => !v);
  }

  toggleInvalid(): void {
    this.invalidOpen.update(v => !v);
  }

  toggleExpand(id: string): void {
    this.expandedInvoiceId.update(cur => cur === id ? null : id);
  }

  validNextPage(): void {
    if (this.validPage() < this.validTotalPages()) {
      this.validPage.update(p => p + 1);
      this.loadValid();
    }
  }

  validPrevPage(): void {
    if (this.validPage() > 1) {
      this.validPage.update(p => p - 1);
      this.loadValid();
    }
  }

  invalidNextPage(): void {
    if (this.invalidPage() < this.invalidTotalPages()) {
      this.invalidPage.update(p => p + 1);
      this.loadInvalid();
    }
  }

  invalidPrevPage(): void {
    if (this.invalidPage() > 1) {
      this.invalidPage.update(p => p - 1);
      this.loadInvalid();
    }
  }

  requeueJob(jobId: string): void {
    this.api.requeueInvalidInvoice(jobId).subscribe({
      next: () => this.loadInvalid(),
      error: (err) => console.error('Failed to requeue', err)
    });
  }

  typeLabel(type: string): string {
    return type === 'SecurityViolation' ? 'Security' : 'Extraction';
  }

  truncateReason(reason: string | null): string {
    if (!reason) return '';
    const display = this.prettifyReason(reason);
    return display.length > 120 ? display.slice(0, 117) + '...' : display;
  }

  prettifyReason(reason: string | null): string {
    if (!reason) return '';
    try {
      // If it's a JSON string, try to extract the 'message'
      if (reason.trim().startsWith('{')) {
        const parsed = JSON.parse(reason);
        return parsed.message || parsed.error || reason;
      }
    } catch (e) {
      // Not JSON or parse failed, return original
    }
    return reason;
  }

  viewInvoice(fileId: string | null | undefined): void {
    if (!fileId) return;
    window.open(`https://drive.google.com/file/d/${fileId}/view`, '_blank');
  }
}
