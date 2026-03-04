import { describe, it, expect, beforeEach, vi } from 'vitest';
import { AnalyticsService } from './analytics.service';
import { of } from 'rxjs';
 
const mockGet = vi.fn().mockReturnValue(of([]));
const mockHttp = { get: mockGet } as any;
 
const baseUrl = 'http://localhost:5247/api';
 
describe('AnalyticsService', () => {
  let service: AnalyticsService;
 
  const startDate = new Date('2025-01-01');
  const endDate = new Date('2025-01-31');
 
  beforeEach(() => {
    mockGet.mockClear();
    service = new AnalyticsService(mockHttp);
  });
 
  it('should call category sales endpoint with correct params', () => {
    service.getCategorySales(startDate, endDate, 'vendor1').subscribe();
 
    expect(mockGet).toHaveBeenCalledWith(
      `${baseUrl}/analytics/categories/sales`,
      expect.objectContaining({
        params: expect.anything()
      })
    );
 
    const params = mockGet.mock.calls[0][1].params;
    expect(params.get('startDate')).toBe(startDate.toISOString());
    expect(params.get('endDate')).toBe(endDate.toISOString());
    expect(params.get('vendorId')).toBe('vendor1');
  });
 
  it('should call trending products with topN and vendor', () => {
    service.getTrendingProducts(startDate, endDate, 10, 'vendor1').subscribe();
 
    const params = mockGet.mock.calls[0][1].params;
    expect(mockGet).toHaveBeenCalledWith(
      `${baseUrl}/analytics/products/trending`,
      expect.anything()
    );
    expect(params.get('topN')).toBe('10');
    expect(params.get('vendorId')).toBe('vendor1');
  });
 
  it('should call product sales with optional category', () => {
    service.getProductSales(startDate, endDate, 'Electronics').subscribe();
 
    const params = mockGet.mock.calls[0][1].params;
    expect(mockGet).toHaveBeenCalledWith(
      `${baseUrl}/analytics/products/sales`,
      expect.anything()
    );
    expect(params.get('category')).toBe('Electronics');
    expect(params.has('vendorId')).toBe(false);
  });
 
  it('should call product timeseries with productId and granularity', () => {
    service.getProductTimeSeries('prod123', startDate, endDate, 'Weekly', 'vendor1').subscribe();
 
    const params = mockGet.mock.calls[0][1].params;
    expect(mockGet).toHaveBeenCalledWith(
      `${baseUrl}/analytics/products/prod123/timeseries`,
      expect.anything()
    );
    expect(params.get('granularity')).toBe('Weekly');
    expect(params.get('vendorId')).toBe('vendor1');
  });
 
  it('should call revenue trend endpoint with default monthly granularity', () => {
    service.getRevenueTrend(startDate, endDate).subscribe();
 
    const params = mockGet.mock.calls[0][1].params;
    expect(mockGet).toHaveBeenCalledWith(
      `${baseUrl}/analytics/revenue/trend`,
      expect.anything()
    );
    expect(params.get('granularity')).toBe('Monthly');
  });
});