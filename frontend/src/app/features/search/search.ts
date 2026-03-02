import {
  Component, signal, inject, ElementRef, ViewChild
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

interface SearchResult {
  naturalLanguageQuery:    string;
  generatedSql:            string;
  rows:                    Record<string, unknown>[];
  rowCount:                number;
  error?:                  string;
  securityRejectionReason?: string;
}

@Component({
  selector: 'app-search',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './search.html',
  styleUrl: './search.css'
})
export class Search {
  @ViewChild('queryInput') queryInput!: ElementRef<HTMLInputElement>;

  private http = inject(HttpClient);

  // ── State ─────────────────────────────────────────────────────────────────
  query   = '';          // plain string for [(ngModel)]
  loading = signal(false);
  result  = signal<SearchResult | null>(null);
  showSql = signal(false);
  error   = signal<string | null>(null);

  readonly MAX_LENGTH = 500;

  readonly exampleQueries = [
    'Show me all invoices from the last 30 days',
    'Which products have the highest total revenue?',
    'List failed jobs and their error messages',
    'Top 10 products by quantity sold',
    'Show invoices with total amount over 5000',
    'How many invoices were uploaded this month?',
    'Which vendors have the most invalid invoices?',
    'Show me products in the Furniture category',
  ];

  // ── Derived ───────────────────────────────────────────────────────────────

  get columns(): string[] {
    const r = this.result();
    if (!r || r.rows.length === 0) return [];
    return Object.keys(r.rows[0]);
  }

  get isOverLimit(): boolean {
    return this.query.length > this.MAX_LENGTH;
  }

  // ── Interactions ──────────────────────────────────────────────────────────

  useExample(ex: string): void {
    this.query = ex;
    this.result.set(null);
    this.error.set(null);
    this.showSql.set(false);
    setTimeout(() => this.queryInput?.nativeElement?.focus(), 0);
  }

  clearResult(): void {
    this.query = '';
    this.result.set(null);
    this.error.set(null);
    this.showSql.set(false);
    setTimeout(() => this.queryInput?.nativeElement?.focus(), 0);
  }

  // Alias used in template (matches original search.html)
  useExampleQuery(ex: string): void {
    this.useExample(ex);
  }

  getColumns(): string[] {
    return this.columns;
  }

  formatCell(value: unknown): string {
    if (value === null || value === undefined) return '—';
    if (typeof value === 'boolean') return value ? 'Yes' : 'No';
    if (typeof value === 'string') {
      if (/^\d{4}-\d{2}-\d{2}T/.test(value)) {
        try { return new Date(value).toLocaleString(); } catch { return value; }
      }
    }
    if (typeof value === 'object') return JSON.stringify(value);
    return String(value);
  }

  // ── Search ────────────────────────────────────────────────────────────────

  search(): void {
    const q = this.query.trim();
    if (!q || this.loading() || this.isOverLimit) return;

    this.loading.set(true);
    this.result.set(null);
    this.error.set(null);
    this.showSql.set(false);

    this.http
      .post<SearchResult>(`${environment.apiUrl}/search`, { query: q })
      .subscribe({
        next: (res) => {
          this.loading.set(false);
          if (res.error) {
            this.error.set(res.error);
            // Still set result so SQL toggle works if SQL was generated
            if (res.generatedSql) this.result.set(res);
          } else {
            this.result.set(res);
          }
        },
        error: (err) => {
          this.loading.set(false);
          if (err.status === 429) {
            this.error.set('Too many requests. Please wait a moment.');
          } else if (err.status === 401 || err.status === 403) {
            this.error.set('You do not have permission to use search.');
          } else {
            this.error.set('Something went wrong. Please try again.');
          }
        }
      });
  }
}