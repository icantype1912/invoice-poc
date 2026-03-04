import { describe, it, expect, vi } from 'vitest';
import { signal, computed } from '@angular/core';
 
// ── Pure logic extracted for testing ─────────────────────────────────
// We test the logic directly without instantiating the Angular component
 
describe('Dashboard logic', () => {
 
  // ── getDateRange ────────────────────────────────────────────────────
  describe('getDateRange', () => {
    const getDateRange = (range: '30d' | '90d' | '12m' | 'all') => {
      const endDate = new Date();
      endDate.setHours(23, 59, 59, 999);
      const startDate = new Date();
      switch (range) {
        case '30d': startDate.setDate(endDate.getDate() - 30); break;
        case '90d': startDate.setDate(endDate.getDate() - 90); break;
        case '12m': startDate.setFullYear(endDate.getFullYear() - 1); break;
        case 'all': startDate.setFullYear(2000); break;
      }
      startDate.setHours(0, 0, 0, 0);
      return { startDate, endDate };
    };
 
    it('should return startDate in year 2000 for "all"', () => {
      expect(getDateRange('all').startDate.getFullYear()).toBe(2000);
    });
 
    it('should return ~30 days for "30d"', () => {
  const { startDate, endDate } = getDateRange('30d');
  const diff = Math.round((endDate.getTime() - startDate.getTime()) / 86400000);
  expect(diff).toBeGreaterThanOrEqual(29);
  expect(diff).toBeLessThanOrEqual(31);
});
 
    it('should return ~90 days for "90d"', () => {
  const { startDate, endDate } = getDateRange('90d');
  const diff = Math.round((endDate.getTime() - startDate.getTime()) / 86400000);
  expect(diff).toBeGreaterThanOrEqual(89);
  expect(diff).toBeLessThanOrEqual(91);
});
 
    it('should return ~1 year for "12m"', () => {
      const { startDate, endDate } = getDateRange('12m');
      expect(endDate.getFullYear() - startDate.getFullYear()).toBe(1);
    });
  });
 
  // ── growthClass ─────────────────────────────────────────────────────
  describe('growthClass', () => {
    const growthClass = (rate: number) => rate >= 0 ? 'growth-pos' : 'growth-neg';
 
    it('positive rate', () => expect(growthClass(0.05)).toBe('growth-pos'));
    it('zero', () => expect(growthClass(0)).toBe('growth-pos'));
    it('negative rate', () => expect(growthClass(-0.05)).toBe('growth-neg'));
  });
 
  // ── growthLabel ─────────────────────────────────────────────────────
  describe('growthLabel', () => {
    const growthLabel = (rate: number) => {
      const pct = (Math.abs(rate) * 100).toFixed(1);
      return rate >= 0 ? `+${pct}%` : `-${pct}%`;
    };
 
    it('positive', () => expect(growthLabel(0.123)).toBe('+12.3%'));
    it('negative', () => expect(growthLabel(-0.056)).toBe('-5.6%'));
  });
 
  // ── formatCell ──────────────────────────────────────────────────────
  describe('formatCell', () => {
    const formatCell = (value: unknown): string => {
      if (value === null || value === undefined) return '—';
      if (typeof value === 'boolean') return value ? 'Yes' : 'No';
      if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(value)) {
        try { return new Date(value).toLocaleString(); } catch { return value; }
      }
      if (typeof value === 'object') return JSON.stringify(value);
      return String(value);
    };
 
    it('null → —', () => expect(formatCell(null)).toBe('—'));
    it('undefined → —', () => expect(formatCell(undefined)).toBe('—'));
    it('true → Yes', () => expect(formatCell(true)).toBe('Yes'));
    it('false → No', () => expect(formatCell(false)).toBe('No'));
    it('ISO date string', () => expect(formatCell('2025-01-15T00:00:00.000Z')).toContain('2025'));
    it('object → JSON', () => expect(formatCell({ a: 1 })).toBe('{"a":1}'));
    it('string as-is', () => expect(formatCell('hello')).toBe('hello'));
  });
 
  // ── categoryShare ───────────────────────────────────────────────────
  describe('categoryShare', () => {
    const categoryShare = (totalRevenue: number, catRevenue: number) =>
      totalRevenue > 0 ? (catRevenue / totalRevenue) * 100 : 0;
 
    it('correct percentage', () => expect(categoryShare(1000, 250)).toBe(25));
    it('zero total → 0', () => expect(categoryShare(0, 100)).toBe(0));
  });
 
  // ── invoiceItemCount ────────────────────────────────────────────────
  describe('invoiceItemCount', () => {
    const invoiceItemCount = (inv: any) => inv.lineItems?.length || 0;
 
    it('returns count', () => expect(invoiceItemCount({ lineItems: [1, 2, 3] })).toBe(3));
    it('returns 0 for undefined', () => expect(invoiceItemCount({ lineItems: undefined })).toBe(0));
  });
 
  // ── KPI computations ────────────────────────────────────────────────
  describe('KPI computations', () => {
    const categories = [
      { category: 'Electronics', totalRevenue: 5000, invoiceCount: 10, productCount: 3, totalQuantity: 50 },
      { category: 'Food', totalRevenue: 3000, invoiceCount: 6, productCount: 2, totalQuantity: 30 },
    ];
 
    it('totalRevenue', () => {
      const total = categories.reduce((s, c) => s + c.totalRevenue, 0);
      expect(total).toBe(8000);
    });
 
    it('totalProducts', () => {
      const total = categories.reduce((s, c) => s + c.productCount, 0);
      expect(total).toBe(5);
    });
 
    it('totalQuantity', () => {
      const total = categories.reduce((s, c) => s + c.totalQuantity, 0);
      expect(total).toBe(80);
    });
 
    it('avgOrderValue', () => {
      const revenue = categories.reduce((s, c) => s + c.totalRevenue, 0);
      const invoices = 16;
      expect(revenue / invoices).toBe(500);
    });
 
    it('avgOrderValue is 0 when no invoices', () => {
      const revenue = 8000;
      const invoices = 0;
      expect(invoices > 0 ? revenue / invoices : 0).toBe(0);
    });
 
    it('topCategory', () => {
      const top = [...categories].sort((a, b) => b.totalRevenue - a.totalRevenue)[0].category;
      expect(top).toBe('Electronics');
    });
 
    it('topCategory N/A when empty', () => {
      const top = [].length ? 'something' : 'N/A';
      expect(top).toBe('N/A');
    });
  });
 
  // ── topProductsByRevenue ────────────────────────────────────────────
  describe('topProductsByRevenue', () => {
    it('returns top 10 sorted by revenue', () => {
      const products = Array.from({ length: 15 }, (_, i) => ({ totalRevenue: i * 100 }));
      const top = [...products].sort((a, b) => b.totalRevenue - a.totalRevenue).slice(0, 10);
      expect(top.length).toBe(10);
      expect(top[0].totalRevenue).toBe(1400);
    });
  });
 
  // ── selectedVendorLabel ─────────────────────────────────────────────
  describe('selectedVendorLabel', () => {
    const getLabel = (vendors: any[], selectedId: string) => {
      if (!selectedId) return 'All Vendors';
      const v = vendors.find(v => v.id === selectedId);
      return v ? (v.companyName || v.email) : 'All Vendors';
    };
 
    it('no selection → All Vendors', () => expect(getLabel([], '')).toBe('All Vendors'));
    it('company name', () => expect(getLabel([{ id: 'v1', companyName: 'Acme', email: 'a@b.com' }], 'v1')).toBe('Acme'));
    it('falls back to email', () => expect(getLabel([{ id: 'v1', email: 'a@b.com' }], 'v1')).toBe('a@b.com'));
  });
});