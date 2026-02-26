import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export type CategorySales = {
    category: string;
    totalRevenue: number;
    invoiceCount: number;
    productCount: number;
    totalQuantity: number;
    averageOrderValue: number;
};

export type ProductTrend = {
    rank: number;
    productId: string;
    productName: string;
    category: string;
    totalQuantity: number;
    totalRevenue: number;
    invoiceCount: number;
    growthRate: number;
};

export type ProductSales = {
    productId: string;
    productName: string;
    category: string;
    totalQuantity: number;
    totalRevenue: number;
    invoiceCount: number;
    averageUnitRate: number;
};

export type ProductTimeSeries = {
    period: string;
    productId: string;
    productName: string;
    quantity: number;
    revenue: number;
    invoiceCount: number;
};

@Injectable({ providedIn: 'root' })
export class AnalyticsService {
    private baseUrl = environment.apiUrl;

    constructor(private http: HttpClient) { }

    getCategorySales(startDate: Date, endDate: Date): Observable<CategorySales[]> {
        const params = new HttpParams()
            .set('startDate', startDate.toISOString())
            .set('endDate', endDate.toISOString());
        return this.http.get<CategorySales[]>(`${this.baseUrl}/analytics/categories/sales`, { params });
    }

    getTrendingProducts(startDate: Date, endDate: Date, topN = 5): Observable<ProductTrend[]> {
        const params = new HttpParams()
            .set('startDate', startDate.toISOString())
            .set('endDate', endDate.toISOString())
            .set('topN', topN);
        return this.http.get<ProductTrend[]>(`${this.baseUrl}/analytics/products/trending`, { params });
    }

    getProductSales(startDate: Date, endDate: Date, category?: string): Observable<ProductSales[]> {
        let params = new HttpParams()
            .set('startDate', startDate.toISOString())
            .set('endDate', endDate.toISOString());
        if (category) params = params.set('category', category);
        return this.http.get<ProductSales[]>(`${this.baseUrl}/analytics/products/sales`, { params });
    }

    getProductTimeSeries(
        productId: string,
        startDate: Date,
        endDate: Date,
        granularity = 'Monthly'
    ): Observable<ProductTimeSeries[]> {
        const params = new HttpParams()
            .set('startDate', startDate.toISOString())
            .set('endDate', endDate.toISOString())
            .set('granularity', granularity);
        return this.http.get<ProductTimeSeries[]>(
            `${this.baseUrl}/analytics/products/${productId}/timeseries`,
            { params }
        );
    }
}
