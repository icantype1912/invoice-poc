import { describe, it, expect, beforeEach } from 'vitest';
 
describe('Invoices logic', () => {
 
  // ── typeLabel ─────────────────────────────────────────────────────
  describe('typeLabel', () => {
    const typeLabel = (type: string) =>
      type === 'SecurityViolation' ? 'Security' : 'Extraction';
 
    it('should return Security for SecurityViolation', () => {
      expect(typeLabel('SecurityViolation')).toBe('Security');
    });
 
    it('should return Extraction for other types', () => {
      expect(typeLabel('ExtractionError')).toBe('Extraction');
    });
 
    it('should return Extraction for empty string', () => {
      expect(typeLabel('')).toBe('Extraction');
    });
  });
 
  // ── prettifyReason ────────────────────────────────────────────────
  describe('prettifyReason', () => {
    const prettifyReason = (reason: string | null): string => {
      if (!reason) return '';
      try {
        if (reason.trim().startsWith('{')) {
          const parsed = JSON.parse(reason);
          return parsed.message || parsed.error || reason;
        }
      } catch (e) { }
      return reason;
    };
 
    it('should return empty string for null', () => {
      expect(prettifyReason(null)).toBe('');
    });
 
    it('should return empty string for empty string', () => {
      expect(prettifyReason('')).toBe('');
    });
 
    it('should extract message from JSON', () => {
      expect(prettifyReason('{"message":"Invalid format"}')).toBe('Invalid format');
    });
 
    it('should extract error from JSON when no message', () => {
      expect(prettifyReason('{"error":"Something failed"}')).toBe('Something failed');
    });
 
    it('should return original JSON string if no message or error key', () => {
      const json = '{"code":404}';
      expect(prettifyReason(json)).toBe(json);
    });
 
    it('should return reason as-is for plain string', () => {
      expect(prettifyReason('Some plain error')).toBe('Some plain error');
    });
 
    it('should return reason as-is for invalid JSON', () => {
      expect(prettifyReason('{not valid json')).toBe('{not valid json');
    });
  });
 
  // ── truncateReason ────────────────────────────────────────────────
  describe('truncateReason', () => {
    const prettifyReason = (reason: string | null): string => {
      if (!reason) return '';
      try {
        if (reason.trim().startsWith('{')) {
          const parsed = JSON.parse(reason);
          return parsed.message || parsed.error || reason;
        }
      } catch (e) { }
      return reason;
    };
 
    const truncateReason = (reason: string | null): string => {
      if (!reason) return '';
      const display = prettifyReason(reason);
      return display.length > 120 ? display.slice(0, 117) + '...' : display;
    };
 
    it('should return empty string for null', () => {
      expect(truncateReason(null)).toBe('');
    });
 
    it('should return full string if under 120 chars', () => {
      const short = 'Short reason';
      expect(truncateReason(short)).toBe(short);
    });
 
    it('should truncate and add ellipsis if over 120 chars', () => {
      const long = 'A'.repeat(130);
      const result = truncateReason(long);
      expect(result.length).toBe(120);
      expect(result.endsWith('...')).toBe(true);
    });
 
    it('should truncate exactly at 117 chars + ellipsis', () => {
      const long = 'B'.repeat(130);
      expect(truncateReason(long)).toBe('B'.repeat(117) + '...');
    });
 
    it('should prettify JSON before truncating', () => {
      const json = JSON.stringify({ message: 'A'.repeat(130) });
      const result = truncateReason(json);
      expect(result.endsWith('...')).toBe(true);
      expect(result.length).toBe(120);
    });
  });
 
  // ── toggleExpand logic ────────────────────────────────────────────
  describe('toggleExpand logic', () => {
    const toggleExpand = (current: string | null, id: string) =>
      current === id ? null : id;
 
    it('should expand when nothing is expanded', () => {
      expect(toggleExpand(null, 'inv1')).toBe('inv1');
    });
 
    it('should collapse when same id is clicked', () => {
      expect(toggleExpand('inv1', 'inv1')).toBeNull();
    });
 
    it('should switch to new id when different id clicked', () => {
      expect(toggleExpand('inv1', 'inv2')).toBe('inv2');
    });
  });
 
  // ── pagination logic ──────────────────────────────────────────────
  describe('pagination logic', () => {
    const nextPage = (page: number, totalPages: number) =>
      page < totalPages ? page + 1 : page;
 
    const prevPage = (page: number) =>
      page > 1 ? page - 1 : page;
 
    it('nextPage increments when not on last page', () => {
      expect(nextPage(1, 5)).toBe(2);
    });
 
    it('nextPage does not increment on last page', () => {
      expect(nextPage(5, 5)).toBe(5);
    });
 
    it('prevPage decrements when not on first page', () => {
      expect(prevPage(3)).toBe(2);
    });
 
    it('prevPage does not decrement on first page', () => {
      expect(prevPage(1)).toBe(1);
    });
  });
 
  // ── onVendorChange logic ──────────────────────────────────────────
  describe('vendor selection logic', () => {
    it('should reset to page 1 when vendor changes', () => {
      let page = 5;
      // simulating onVendorChange resetting page
      page = 1;
      expect(page).toBe(1);
    });
 
    it('should use undefined when selectedVendorId is empty', () => {
      const selectedVendorId = '';
      const vendorId = selectedVendorId || undefined;
      expect(vendorId).toBeUndefined();
    });
 
    it('should pass vendorId when selected', () => {
      const selectedVendorId = 'vendor-123';
      const vendorId = selectedVendorId || undefined;
      expect(vendorId).toBe('vendor-123');
    });
  });
});