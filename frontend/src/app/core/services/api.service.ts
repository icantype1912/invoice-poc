import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

function joinUrl(base: string, path: string): string {
  const b = (base ?? '').replace(/\/+$/, '');
  const p = (path ?? '').replace(/^\/+/, '');
  return `${b}/${p}`;
}

// ----------------------------- LOGS ------------------------------
export type FileChangeLog = {
  id: number;
  fileName: string;
  fileId: string;
  changeType: string;
  detectedAt: string;
  mimeType: string;
  fileSize: number;
  modifiedBy: string;
  googleDriveModifiedTime: string;
  processed: boolean;
  processedAt: string | null;
  uploadedByVendorId: string;
  securityStatus: string;
  securityFailReason: string | null;
  securityCheckedAt: string | null;
};

export type LogsResponse = {
  logs: FileChangeLog[];
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
};

export type LogStats = {
  totalFiles: number;
  totalProcessed: number;
  totalPending: number;
  byChangeType: Array<{
    changeType: string;
    count: number;
    processed: number;
    pending: number;
  }>;
};

// ----------------------------- PRODUCTS ------------------------------
export type Product = {
  id: string;
  productId: string;
  productName: string;
  category?: string;
  primaryCategory?: string;
  secondaryCategory?: string;
  defaultUnitRate?: number;
  totalQuantitySold: number;
  totalRevenue: number;
  invoiceCount: number;
  lastSoldDate?: string;
  createdAt: string;
  updatedAt: string;
};

export type ProductsResponse = {
  products: Product[];
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
};

export type Category = {
  category: string;
  productCount: number;
  totalRevenue: number;
};

// ----------------------------- INVOICES ------------------------------
export type InvoiceLineDto = {
  id: string;
  productName: string;
  category?: string;
  productId: string;
  quantity: number;
  unitRate: number;
  amount: number;
};

export type DiscountDto = {
  percentage?: number;
  amount?: number;
};

export type ShipToDto = {
  city?: string;
  state?: string;
  country?: string;
};

export type InvoiceDto = {
  id: string;
  invoiceNumber?: string;
  invoiceDate?: string;
  orderId?: string;
  vendorName?: string;
  billToName?: string;
  shipTo?: ShipToDto;
  shipMode?: string;
  subtotal?: number;
  discount?: DiscountDto;
  shippingCost?: number;
  totalAmount?: number;
  balanceDue?: number;
  currency?: string;
  notes?: string;
  terms?: string;
  driveFileId?: string;
  originalFileName?: string;
  extractedData?: any;
  uploadedByVendorId?: string;
  createdAt: string;
  updatedAt: string;
  lineItems: InvoiceLineDto[];
};

export type InvoicesResponse = {
  invoices: InvoiceDto[];
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
};

// ----------------------------- INVALID INVOICES ------------------------------
export type InvalidInvoiceType = 'ExtractionFailure' | 'SecurityViolation';

export type InvalidInvoice = {
  id: string;
  fileId: string | null;
  fileName: string | null;
  vendorId: string | null;
  jobId: string | null;
  reason: string | null;
  createdAt: string;
  type: InvalidInvoiceType;
};

export type InvalidInvoicesResponse = {
  data: InvalidInvoice[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
};

// ----------------------------- JOB QUEUE ------------------------------
export type JobStatus = 'PENDING' | 'PROCESSING' | 'COMPLETED' | 'INVALID' | 'FAILED';

export type JobQueueItem = {
  id: string;
  jobType: string;
  status: JobStatus;
  retryCount: number;
  lockedBy: string | null;
  lockedAt: string | null;
  nextRetryAt: string | null;
  createdAt: string;
  updatedAt: string;
  payloadJson: any;
  errorMessage: any;
};

export type JobQueueResponse = {
  jobs: JobQueueItem[];
  page: number;
  pageSize: number;
  total: number;
};

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private baseUrl = environment.apiUrl;

  constructor(private http: HttpClient) { }

  // ----------------------------- LOGS ------------------------------
  getLogs(page = 1, pageSize = 50, changeType?: string): Observable<LogsResponse> {
    const url = joinUrl(this.baseUrl, 'logs');
    let params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(pageSize));
    if (changeType) {
      params = params.set('changeType', changeType);
    }
    return this.http.get<LogsResponse>(url, { params });
  }

  getLogStats(): Observable<LogStats> {
    const url = joinUrl(this.baseUrl, 'logs/stats');
    return this.http.get<LogStats>(url);
  }

  // ----------------------------- PRODUCTS ------------------------------
  getProducts(page = 1, pageSize = 50, category?: string, search?: string): Observable<ProductsResponse> {
    const url = joinUrl(this.baseUrl, 'products');
    let params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(pageSize));
    if (category) {
      params = params.set('category', category);
    }
    if (search) {
      params = params.set('search', search);
    }
    return this.http.get<ProductsResponse>(url, { params });
  }

  getProductCategories(): Observable<Category[]> {
    const url = joinUrl(this.baseUrl, 'products/categories');
    return this.http.get<Category[]>(url);
  }

  // ----------------------------- INVOICES ------------------------------
  getInvoices(page = 1, pageSize = 20): Observable<InvoicesResponse> {
    const url = joinUrl(this.baseUrl, 'invoices');
    const params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(pageSize));
    return this.http.get<InvoicesResponse>(url, { params });
  }

  getInvoiceById(id: string): Observable<InvoiceDto> {
    const url = joinUrl(this.baseUrl, `invoices/${encodeURIComponent(id)}`);
    return this.http.get<InvoiceDto>(url);
  }

  getInvoicesByFileId(fileId: string): Observable<InvoiceDto> {
    const url = joinUrl(this.baseUrl, `invoices/by-file/${encodeURIComponent(fileId)}`);
    return this.http.get<InvoiceDto>(url);
  }

  // ----------------------------- INVALID INVOICES ------------------------------
  getInvalidInvoices(page = 1, pageSize = 20): Observable<InvalidInvoicesResponse> {
    const url = joinUrl(this.baseUrl, 'invalid-invoices');
    const params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(pageSize));
    return this.http.get<InvalidInvoicesResponse>(url, { params });
  }

  requeueInvalidInvoice(jobId: string): Observable<unknown> {
    const url = joinUrl(
      this.baseUrl,
      `invalid-invoices/${encodeURIComponent(jobId)}/requeue`
    );
    return this.http.post(url, {});
  }

  // ----------------------------- JOB QUEUE ------------------------------
  getJobs(page = 1, pageSize = 50, status?: JobStatus): Observable<JobQueueResponse> {
    const url = joinUrl(this.baseUrl, 'jobs');
    let params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(pageSize));
    if (status) {
      params = params.set('status', status);
    }
    return this.http.get<JobQueueResponse>(url, { params });
  }

  getJobById(id: string): Observable<JobQueueItem> {
    const url = joinUrl(this.baseUrl, `jobs/${encodeURIComponent(id)}`);
    return this.http.get<JobQueueItem>(url);
  }

  requeueJob(id: string): Observable<unknown> {
    const url = joinUrl(this.baseUrl, `jobs/${encodeURIComponent(id)}/requeue`);
    return this.http.post(url, {});
  }
}
