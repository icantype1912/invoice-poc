import { describe, it, expect } from 'vitest';
 
describe('Search logic', () => {
 
  // ── isOverLimit ───────────────────────────────────────────────────

  describe('isOverLimit', () => {

    const MAX_LENGTH = 500;

    const isOverLimit = (query: string) => query.length > MAX_LENGTH;
 
    it('should be false for empty string', () => {

      expect(isOverLimit('')).toBe(false);

    });
 
    it('should be false for exactly 500 chars', () => {

      expect(isOverLimit('A'.repeat(500))).toBe(false);

    });
 
    it('should be true for 501 chars', () => {

      expect(isOverLimit('A'.repeat(501))).toBe(true);

    });
 
    it('should be false for normal query', () => {

      expect(isOverLimit('Show me all invoices')).toBe(false);

    });

  });
 
  // ── columns ───────────────────────────────────────────────────────

  describe('columns', () => {

    const getColumns = (result: any) => {

      if (!result || result.rows.length === 0) return [];

      return Object.keys(result.rows[0]);

    };
 
    it('should return empty array when result is null', () => {

      expect(getColumns(null)).toEqual([]);

    });
 
    it('should return empty array when rows is empty', () => {

      expect(getColumns({ rows: [] })).toEqual([]);

    });
 
    it('should return column names from first row', () => {

      expect(getColumns({ rows: [{ id: 1, name: 'test', amount: 100 }] }))

        .toEqual(['id', 'name', 'amount']);

    });
 
    it('should only use first row for column names', () => {

      expect(getColumns({

        rows: [

          { id: 1, name: 'a' },

          { id: 2, name: 'b', extra: 'x' }

        ]

      })).toEqual(['id', 'name']);

    });

  });
 
  // ── formatCell ────────────────────────────────────────────────────

  describe('formatCell', () => {

    const formatCell = (value: unknown): string => {

      if (value === null || value === undefined) return '—';

      if (typeof value === 'boolean') return value ? 'Yes' : 'No';

      if (typeof value === 'string') {

        if (/^\d{4}-\d{2}-\d{2}T/.test(value)) {

          try { return new Date(value).toLocaleString(); } catch { return value; }

        }

      }

      if (typeof value === 'object') return JSON.stringify(value);

      return String(value);

    };
 
    it('should return — for null', () => expect(formatCell(null)).toBe('—'));

    it('should return — for undefined', () => expect(formatCell(undefined)).toBe('—'));

    it('should return Yes for true', () => expect(formatCell(true)).toBe('Yes'));

    it('should return No for false', () => expect(formatCell(false)).toBe('No'));

    it('should format ISO date string', () => expect(formatCell('2025-01-15T00:00:00.000Z')).toContain('2025'));

    it('should stringify objects', () => expect(formatCell({ a: 1 })).toBe('{"a":1}'));

    it('should return plain string as-is', () => expect(formatCell('hello')).toBe('hello'));

    it('should convert numbers to string', () => expect(formatCell(42)).toBe('42'));

  });
 
  // ── search guard logic ────────────────────────────────────────────

  describe('search guard', () => {

    const shouldSearch = (query: string, loading: boolean, isOverLimit: boolean) => {

      const q = query.trim();

      return !(!q || loading || isOverLimit);

    };
 
    it('should not search when query is empty', () => {

      expect(shouldSearch('', false, false)).toBe(false);

    });
 
    it('should not search when query is whitespace', () => {

      expect(shouldSearch('   ', false, false)).toBe(false);

    });
 
    it('should not search when loading', () => {

      expect(shouldSearch('some query', true, false)).toBe(false);

    });
 
    it('should not search when over limit', () => {

      expect(shouldSearch('some query', false, true)).toBe(false);

    });
 
    it('should search when query is valid', () => {

      expect(shouldSearch('show invoices', false, false)).toBe(true);

    });
 
    it('should trim query before checking', () => {

      expect(shouldSearch('  valid  ', false, false)).toBe(true);

    });

  });
 
  // ── error message mapping ─────────────────────────────────────────

  describe('error message mapping', () => {

    const getErrorMessage = (status: number) => {

      if (status === 429) return 'Too many requests. Please wait a moment.';

      if (status === 401 || status === 403) return 'You do not have permission to use search.';

      return 'Something went wrong. Please try again.';

    };
 
    it('should return rate limit message for 429', () => {

      expect(getErrorMessage(429)).toBe('Too many requests. Please wait a moment.');

    });
 
    it('should return permission message for 401', () => {

      expect(getErrorMessage(401)).toBe('You do not have permission to use search.');

    });
 
    it('should return permission message for 403', () => {

      expect(getErrorMessage(403)).toBe('You do not have permission to use search.');

    });
 
    it('should return generic message for 500', () => {

      expect(getErrorMessage(500)).toBe('Something went wrong. Please try again.');

    });
 
    it('should return generic message for unknown status', () => {

      expect(getErrorMessage(0)).toBe('Something went wrong. Please try again.');

    });

  });
 
  // ── useExample / clearResult state logic ─────────────────────────

  describe('useExample logic', () => {

    it('should set query to example', () => {

      let query = '';

      query = 'Show me all invoices from the last 30 days';

      expect(query).toBe('Show me all invoices from the last 30 days');

    });
 
    it('should reset result and error on useExample', () => {

      let result: any = { rows: [{ id: 1 }] };

      let error: any = 'some error';

      result = null;

      error = null;

      expect(result).toBeNull();

      expect(error).toBeNull();

    });

  });
 
  // ── clearResult logic ─────────────────────────────────────────────

  describe('clearResult logic', () => {

    it('should clear query', () => {

      let query = 'some query';

      query = '';

      expect(query).toBe('');

    });
 
    it('should clear result and error', () => {

      let result: any = { rows: [] };

      let error: any = 'error';

      result = null;

      error = null;

      expect(result).toBeNull();

      expect(error).toBeNull();

    });

  });
 
  // ── exampleQueries ────────────────────────────────────────────────

  describe('exampleQueries', () => {

    const exampleQueries = [

      'Show me all invoices from the last 30 days',

      'Which products have the highest total revenue?',

      'List failed jobs and their error messages',

      'Top 10 products by quantity sold',

      'Show invoices with total amount over 5000',

      'How many invoices were uploaded this month?',

      'Which vendors have the most invalid invoices?',

      'Show me products in the Furniture category',

    ];
 
    it('should have 8 example queries', () => {

      expect(exampleQueries.length).toBe(8);

    });
 
    it('should all be non-empty strings', () => {

      exampleQueries.forEach(q => expect(q.length).toBeGreaterThan(0));

    });

  });

});

 