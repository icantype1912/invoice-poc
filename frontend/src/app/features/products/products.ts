import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { ApiService, Product, ProductsResponse, Category } from '../../core/services/api.service';
import { Auth } from '../../core/services/auth';

@Component({
  selector: 'app-products',
  standalone: true,
  imports: [FormsModule, CurrencyPipe, DatePipe],
  templateUrl: './products.html',
  styleUrl: './products.css',
})
export class Products implements OnInit {
  private api = inject(ApiService);
  private auth = inject(Auth);

  // Normalize to boolean (Auth.isAdmin may be a method).
  get isAdmin() {
    return typeof (this.auth as any).isAdmin === 'function'
      ? (this.auth as any).isAdmin()
      : !!(this.auth as any).isAdmin;
  }

  products = signal<Product[]>([]);
  categories = signal<Category[]>([]);

  page = signal(1);
  totalPages = signal(1);
  total = signal(0);

  isLoading = signal(false);

  // IMPORTANT: ngModel expects plain fields (not signals).
  categoryFilter: string = 'All Categories';
  searchQuery: string = '';

  ngOnInit(): void {
    this.loadCategories();
    this.loadProducts();
  }

  loadCategories(): void {
    this.api.getProductCategories().subscribe({
      next: (cats) => this.categories.set(cats ?? []),
      error: (err) => console.error('Failed loading categories', err),
    });
  }

  loadProducts(): void {
    this.isLoading.set(true);

    const category =
      this.categoryFilter && this.categoryFilter !== 'All Categories'
        ? this.categoryFilter
        : undefined;

    const search =
      this.searchQuery && this.searchQuery.trim().length
        ? this.searchQuery.trim()
        : undefined;

    this.api.getProducts(this.page(), 50, category, search).subscribe({
      next: (res: ProductsResponse) => {
        this.products.set(res.products ?? []);
        this.totalPages.set(res.totalPages ?? Math.max(1, Math.ceil((res.total ?? 0) / 50)));
        this.total.set(res.total ?? 0);
        this.isLoading.set(false);
      },
      error: (err) => {
        console.error('Failed loading products', err);
        this.products.set([]);
        this.totalPages.set(1);
        this.total.set(0);
        this.isLoading.set(false);
      },
    });
  }

  onSearch(): void {
    this.page.set(1);
    this.loadProducts();
  }

  onCategoryChange(): void {
    this.page.set(1);
    this.loadProducts();
  }

  nextPage(): void {
    if (this.page() < this.totalPages()) {
      this.page.update((p) => p + 1);
      this.loadProducts();
    }
  }

  prevPage(): void {
    if (this.page() > 1) {
      this.page.update((p) => p - 1);
      this.loadProducts();
    }
  }
}
