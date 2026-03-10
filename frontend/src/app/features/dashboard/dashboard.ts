import {
  Component, OnInit, OnDestroy, ElementRef, ViewChild,
  inject, signal, effect, computed
} from '@angular/core';
import { CommonModule, CurrencyPipe, NgClass, DatePipe } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { AnalyticsService, CategorySales, ProductTrend, ProductSales } from '../../core/services/analytics.service';
import { Auth } from '../../core/services/auth';
import { ChatbotService } from '../../core/services/chatbot.service';
import { forkJoin } from 'rxjs';
import { environment } from '../../../environments/environment';

const getD3 = (): any => (window as any)['d3'];

type Vendor = { id: string; email: string; companyName?: string; role: number; status: number; };

type RecentInvoice = {
  id: string;
  invoiceNumber?: string;
  invoiceDate?: string;
  vendorName?: string;
  companyName?: string;
  totalAmount?: number;
  currency?: string;
  originalFileName?: string;
  googleDriveFileId?: string;
  createdAt: string;
  lineItems: any[];
};

type AskMessage = {
  id: number;
  question: string;
  answer: string;
  loading: boolean;
  error: boolean;
  rows: Record<string, unknown>[] | null;
  columns: string[];
};

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, NgClass, DatePipe, FormsModule],
  templateUrl: './dashboard.html',
  styleUrls: ['./dashboard.css']
})
export class Dashboard implements OnInit, OnDestroy {

  private analyticsService = inject(AnalyticsService);
  private auth = inject(Auth);
  private http = inject(HttpClient);
  public chatbotService = inject(ChatbotService);

  get isAdmin(): boolean { return this.auth.isAdmin; }
  get chatOpen(): boolean { return this.chatbotService.isOpen(); }

  // ── State ────────────────────────────────────────────────────────────
  categorySales = signal<CategorySales[]>([]);
  trendingProducts = signal<ProductTrend[]>([]);
  productSales = signal<ProductSales[]>([]);
  recentInvoices = signal<RecentInvoice[]>([]);
  isLoading = signal(true);
  selectedRange = signal<'30d' | '90d' | '12m' | 'all'>('all');

  // Admin vendor filter
  vendors = signal<Vendor[]>([]);
  selectedVendorId = signal<string>('');

  // Table tabs
  activeTab = signal<'trending' | 'products' | 'categories'>('trending');

  // Real invoice total from invoices API
  totalInvoiceCount = signal<number>(0);

  // ── Ask Your Data state ──────────────────────────────────────────────
  askMessages = signal<AskMessage[]>([]);
  askLoading = signal(false);
  askQuery = '';
  private askIdCounter = 0;

  readonly askSuggestions = [
    'Which product has highest revenue?',
    'How many invoices this month?',
    'Top 5 categories by quantity',
    'Show invoices over $10,000',
  ];

  // ── Computed KPIs ────────────────────────────────────────────────────
  totalRevenue = computed(() =>
    this.categorySales().reduce((s, c) => s + (c.totalRevenue || 0), 0));

  totalInvoices = computed(() => this.totalInvoiceCount());

  totalProducts = computed(() =>
    this.categorySales().reduce((s, c) => s + (c.productCount || 0), 0));

  totalQuantity = computed(() =>
    this.categorySales().reduce((s, c) => s + (c.totalQuantity || 0), 0));

  avgOrderValue = computed(() => {
    const inv = this.totalInvoices();
    return inv > 0 ? this.totalRevenue() / inv : 0;
  });

  topCategory = computed(() => {
    const s = this.categorySales();
    return s.length ? [...s].sort((a, b) => b.totalRevenue - a.totalRevenue)[0].category : 'N/A';
  });

  topCategoryRevenue = computed(() => {
    const s = this.categorySales();
    return s.length ? [...s].sort((a, b) => b.totalRevenue - a.totalRevenue)[0].totalRevenue : 0;
  });

  selectedVendorLabel = computed(() => {
    const id = this.selectedVendorId();
    if (!id) return 'All Vendors';
    const v = this.vendors().find(v => v.id === id);
    return v ? (v.companyName || v.email) : 'All Vendors';
  });

  topProductsByRevenue = computed(() =>
    [...this.productSales()].sort((a, b) => b.totalRevenue - a.totalRevenue).slice(0, 10));

  // ── ViewChild refs ───────────────────────────────────────────────────
  @ViewChild('donutContainer') donutContainer!: ElementRef;
  @ViewChild('barContainer') barContainer!: ElementRef;
  @ViewChild('lineContainer') lineContainer!: ElementRef;
  @ViewChild('askHistory') askHistoryEl!: ElementRef;
  @ViewChild('askInput') askInputEl!: ElementRef;

  private resizeListener = () => {
    if (!this.isLoading() && this.categorySales().length > 0) {
      this.renderDonut(this.categorySales());
      this.renderBar(this.categorySales());
    }
  };

  constructor() {
    effect(() => {
      const data = this.categorySales();
      if (data.length > 0 && !this.isLoading()) {
        setTimeout(() => {
          this.renderDonut(data);
          this.renderBar(data);
          this.buildAndRenderLine(data);
        }, 80);
      }
    });
  }

  ngOnInit(): void {
    if (this.isAdmin) this.loadVendors();
    this.loadData();
    window.addEventListener('resize', this.resizeListener);
  }

  ngOnDestroy(): void {
    window.removeEventListener('resize', this.resizeListener);
    getD3()?.selectAll('.dash-tooltip').remove();
  }

  // ── Data loading ─────────────────────────────────────────────────────
  loadVendors(): void {
    this.http.get<Vendor[]>(`${environment.apiUrl}/admin/users`).subscribe({
      next: (users) => this.vendors.set((users || []).filter(u => u.role === 1)),
      error: () => { }
    });
  }

  setRange(range: '30d' | '90d' | '12m' | 'all'): void {
    this.selectedRange.set(range);
    this.loadData();
  }

  onVendorChange(event: Event): void {
    this.selectedVendorId.set((event.target as HTMLSelectElement).value);
    this.loadData();
  }

  getDateRange(): { startDate: Date; endDate: Date } {
    const endDate = new Date();
    endDate.setHours(23, 59, 59, 999);
    const startDate = new Date();
    switch (this.selectedRange()) {
      case '30d': startDate.setDate(endDate.getDate() - 30); break;
      case '90d': startDate.setDate(endDate.getDate() - 90); break;
      case '12m': startDate.setFullYear(endDate.getFullYear() - 1); break;
      case 'all': startDate.setFullYear(2000); break;
    }
    startDate.setHours(0, 0, 0, 0);
    return { startDate, endDate };
  }

  loadData(): void {
    this.isLoading.set(true);
    const { startDate, endDate } = this.getDateRange();
    const vendorId = this.isAdmin ? (this.selectedVendorId() || undefined) : undefined;

    forkJoin({
      categories: this.analyticsService.getCategorySales(startDate, endDate, vendorId),
      trending: this.analyticsService.getTrendingProducts(startDate, endDate, 10, vendorId),
      products: this.analyticsService.getProductSales(startDate, endDate, undefined, vendorId),
    }).subscribe({
      next: (res) => {
        this.categorySales.set(res.categories || []);
        this.trendingProducts.set(res.trending || []);
        this.productSales.set(res.products || []);
        this.loadRecentInvoices(vendorId);
        this.isLoading.set(false);
      },
      error: (err) => {
        console.error('Dashboard load failed:', err);
        this.isLoading.set(false);
      }
    });
  }

  loadRecentInvoices(vendorId?: string): void {
    let params = new HttpParams()
      .set('page', '1')
      .set('pageSize', '8')
      .set('sortBy', 'createdAt')
      .set('sortDesc', 'true');
    if (vendorId) params = params.set('vendorId', vendorId);

    this.http.get<any>(`${environment.apiUrl}/invoices`, { params }).subscribe({
      next: (res) => {
        const invoices: RecentInvoice[] = Array.isArray(res)
          ? res
          : (res?.invoices || res?.data || res?.items || []);
        this.recentInvoices.set(invoices.slice(0, 8));
        const realTotal: number = res?.total ?? res?.totalCount ?? res?.totalItems ?? invoices.length;
        this.totalInvoiceCount.set(realTotal);
      },
      error: () => {
        this.recentInvoices.set([]);
        this.totalInvoiceCount.set(0);
      }
    });
  }

  // ── Ask Your Data ────────────────────────────────────────────────────
  askFromChip(suggestion: string): void {
    this.askQuery = suggestion;
    this.submitAsk();
  }

  submitAsk(): void {
    const q = this.askQuery.trim();
    if (!q || this.askLoading()) return;

    this.askQuery = '';
    this.askLoading.set(true);

    const id = ++this.askIdCounter;
    const msg: AskMessage = {
      id,
      question: q,
      answer: '',
      loading: true,
      error: false,
      rows: null,
      columns: [],
    };

    this.askMessages.update(msgs => [...msgs, msg]);
    this.scrollAskHistory();

    this.http.post<any>(`${environment.apiUrl}/search`, { query: q }).subscribe({
      next: (res) => {
        this.askLoading.set(false);

        const rows: Record<string, unknown>[] = res?.rows ?? [];
        const hasRows = rows.length > 0;
        const isError = !!res?.error;

        let answer = '';
        if (isError) {
          answer = res.error;
        } else if (!hasRows) {
          answer = 'No results found. Try rephrasing your question.';
        }

        this.askMessages.update(msgs =>
          msgs.map(m => m.id === id
            ? {
              ...m,
              loading: false,
              error: isError,
              rows: hasRows && !isError ? rows : null,
              columns: hasRows && !isError ? Object.keys(rows[0]) : [],
              answer,
            }
            : m
          )
        );
        this.scrollAskHistory();
      },
      error: (err) => {
        this.askLoading.set(false);
        const errMsg = err.status === 429
          ? 'Too many requests — please wait a moment.'
          : err.status === 401 || err.status === 403
            ? 'You do not have permission to use search.'
            : 'Something went wrong. Please try again.';

        this.askMessages.update(msgs =>
          msgs.map(m => m.id === id
            ? { ...m, loading: false, error: true, answer: errMsg }
            : m
          )
        );
        this.scrollAskHistory();
      },
    });
  }

  private scrollAskHistory(): void {
    setTimeout(() => {
      const el = this.askHistoryEl?.nativeElement;
      if (el) el.scrollTop = el.scrollHeight;
    }, 60);
  }

  formatCell(value: unknown): string {
    if (value === null || value === undefined) return '—';
    if (typeof value === 'boolean') return value ? 'Yes' : 'No';
    if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(value)) {
      try { return new Date(value).toLocaleString(); } catch { return value; }
    }
    if (typeof value === 'object') return JSON.stringify(value);
    return String(value);
  }

  // ── Revenue Over Time ────────────────────────────────────────────────
  buildAndRenderLine(categories: CategorySales[]): void {
    if (!categories.length) return;
    const { startDate, endDate } = this.getDateRange();
    const vendorId = this.isAdmin ? (this.selectedVendorId() || undefined) : undefined;
    const range = this.selectedRange();

    const fetchGranularity = range === '30d' ? 'Daily' : range === '90d' ? 'Weekly' : 'Monthly';
    const displayGranularity = range === '30d' ? 'daily' : range === '90d' ? 'weekly' : 'monthly';

    this.analyticsService.getRevenueTrend(startDate, endDate, fetchGranularity, vendorId).subscribe({
      next: (trend) => {
        const mapped = (trend || []).map(t => ({
          period: new Date(t.period),
          revenue: t.revenue,
          invoices: t.invoiceCount
        })).sort((a, b) => a.period.getTime() - b.period.getTime());

        if (!mapped.length) return;
        setTimeout(() => this.renderLine(mapped, displayGranularity), 50);
      },
      error: () => { }
    });
  }

  private getPeriodKey(date: Date, granularity: string): string {
    switch (granularity) {
      case 'daily': return date.toISOString().slice(0, 10);
      case 'weekly': {
        const d = new Date(date);
        d.setDate(d.getDate() - d.getDay());
        return d.toISOString().slice(0, 10);
      }
      case 'monthly': return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
      default: return date.toISOString().slice(0, 10);
    }
  }

  // ── UI helpers ───────────────────────────────────────────────────────
  setTab(tab: 'trending' | 'products' | 'categories'): void {
    this.activeTab.set(tab);
  }

  growthClass(rate: number): string { return rate >= 0 ? 'growth-pos' : 'growth-neg'; }

  growthLabel(rate: number): string {
    const pct = (Math.abs(rate) * 100).toFixed(1);
    return rate >= 0 ? `+${pct}%` : `-${pct}%`;
  }

  categoryShare(cat: CategorySales): number {
    const total = this.totalRevenue();
    return total > 0 ? (cat.totalRevenue / total) * 100 : 0;
  }

  invoiceItemCount(inv: RecentInvoice): number {
    return inv.lineItems?.length || 0;
  }

  openInvoice(inv: RecentInvoice): void {
    if (inv.googleDriveFileId) {
      window.open(`https://drive.google.com/file/d/${inv.googleDriveFileId}/view`, '_blank');
    }
  }

  // ── Chart: Donut ─────────────────────────────────────────────────────
  renderDonut(data: CategorySales[]): void {
    if (!this.donutContainer) return;
    const d3: any = getD3();
    if (!d3) return;
    const el = this.donutContainer.nativeElement;
    d3.select(el).selectAll('*').remove();

    const W: number = (el.offsetWidth as number) || 320;
    const H = 260;
    const R = Math.min(W, H) / 2 - 18;

    const svgRoot = d3.select(el).append('svg').attr('width', W).attr('height', H);
    const defs = svgRoot.append('defs');
    const svg = svgRoot.append('g').attr('transform', `translate(${W / 2},${H / 2})`);

    const palette = ['#a855f7', '#00e5ff', '#4ade80', '#fbbf24', '#f472b6', '#60a5fa', '#fb7185', '#34d399'];

    palette.forEach((c: string, i: number) => {
      const g = defs.append('radialGradient').attr('id', `rg${i}`).attr('cx', '40%').attr('cy', '40%');
      g.append('stop').attr('offset', '0%').attr('stop-color', c).attr('stop-opacity', '1');
      g.append('stop').attr('offset', '100%').attr('stop-color', c).attr('stop-opacity', '0.65');
    });

    const makeArc = (inner: number, outer: number) =>
      d3.arc().innerRadius(inner).outerRadius(outer).cornerRadius(5).padAngle(0.028);
    const arc = makeArc(R * 0.54, R);
    const arcH = makeArc(R * 0.54, R + 10);
    const pie = d3.pie().value((d: CategorySales) => d.totalRevenue).sort(null);

    const tip = this.makeTip(d3);

    const pieData = pie(data);

    svg.selectAll('.slice').data(pieData).enter().append('path')
      .attr('class', 'slice')
      .attr('d', (d: any) => arc(d))
      .attr('fill', (_d: any, i: number) => `url(#rg${i % palette.length})`)
      .style('opacity', 0)
      .style('pointer-events', 'none')
      .transition().duration(550).delay((_d: any, i: number) => i * 70).style('opacity', 1);

    svg.selectAll('.slice-hit').data(pieData).enter().append('path')
      .attr('class', 'slice-hit')
      .attr('d', (d: any) => arc(d))
      .attr('fill', 'transparent')
      .style('cursor', 'pointer')
      .on('mouseover', (event: any, d: any) => {
        svg.selectAll('.slice')
          .filter((_: any, i: number) => i === pieData.indexOf(d))
          .transition().duration(50).attr('d', (dd: any) => arcH(dd));
        const pct = this.totalRevenue() > 0
          ? ((d.data.totalRevenue / this.totalRevenue()) * 100).toFixed(1) : '0';
        const avgRev = d.data.invoiceCount > 0 ? (d.data.totalRevenue / d.data.invoiceCount) : 0;
        tip.style('opacity', '1').html(
          `<div style="font-weight:600;color:var(--glow-purple);margin-bottom:3px">${d.data.category}</div>` +
          `<div style="font-size:15px;font-weight:700">$${(d.data.totalRevenue as number).toLocaleString(undefined, { maximumFractionDigits: 2 })}</div>` +
          `<div style="color:var(--text-muted);font-size:12px;margin-top:2px">${pct}% Share &nbsp;·&nbsp; ${d.data.invoiceCount} Invoice${d.data.invoiceCount !== 1 ? 's' : ''}</div>` +
          `<div style="color:var(--text-muted);font-size:12px;margin-top:2px">$${Math.round(avgRev).toLocaleString()} Avg / Invoice</div>`
        );
      })
      .on('mousemove', (event: any) => {
        tip.style('left', `${event.clientX + 16}px`).style('top', `${event.clientY - 44}px`);
      })
      .on('mouseout', (event: any, d: any) => {
        svg.selectAll('.slice')
          .filter((_: any, i: number) => i === pieData.indexOf(d))
          .transition().duration(50).attr('d', (dd: any) => arc(dd));
        tip.style('opacity', '0');
      });

    svg.append('text').attr('text-anchor', 'middle').attr('dy', '-0.35em')
      .style('fill', 'var(--text-muted)').style('font-size', '11px').style('letter-spacing', '1px').text('TOTAL');
    svg.append('text').attr('text-anchor', 'middle').attr('dy', '1.1em')
      .style('fill', 'var(--text-primary)').style('font-size', '22px').style('font-weight', '700')
      .text(`$${(this.totalRevenue() / 1000).toFixed(1)}k`);
  }

  // ── Chart: Bar ───────────────────────────────────────────────────────
  renderBar(data: CategorySales[]): void {
    if (!this.barContainer) return;
    const d3: any = getD3();
    if (!d3) return;

    const el = this.barContainer.nativeElement;
    // Walk up to the scroll wrapper so we know the available viewport width
    const wrapper = el.parentElement as HTMLElement;
    const wrapperW = wrapper ? wrapper.offsetWidth : 600;

    d3.select(el).selectAll('*').remove();

    const margin = { top: 16, right: 24, bottom: 88, left: 50 };
    const H = 280 - margin.top - margin.bottom;
    const barWidth = 72;

    // W is always driven by data — never clamped to container.
    // This guarantees the SVG overflows the wrapper when there are many bars.
    const W = Math.max(data.length * barWidth, wrapperW - margin.left - margin.right);

    // Explicitly size the host div to match the SVG so overflow-x triggers on the wrapper.
    el.style.width = `${W + margin.left + margin.right}px`;
    el.style.minWidth = 'unset';

    const svgRoot = d3.select(el).append('svg')
      .attr('width', W + margin.left + margin.right)
      .attr('height', H + margin.top + margin.bottom);
    const defs = svgRoot.append('defs');
    const svg = svgRoot.append('g').attr('transform', `translate(${margin.left},${margin.top})`);

    const bg = defs.append('linearGradient').attr('id', 'barG')
      .attr('x1', '0%').attr('y1', '0%').attr('x2', '0%').attr('y2', '100%');
    bg.append('stop').attr('offset', '0%').attr('stop-color', '#a855f7');
    bg.append('stop').attr('offset', '100%').attr('stop-color', '#00e5ff').attr('stop-opacity', '0.75');

    const bgh = defs.append('linearGradient').attr('id', 'barGH')
      .attr('x1', '0%').attr('y1', '0%').attr('x2', '0%').attr('y2', '100%');
    bgh.append('stop').attr('offset', '0%').attr('stop-color', '#c084fc');
    bgh.append('stop').attr('offset', '100%').attr('stop-color', '#38bdf8').attr('stop-opacity', '0.9');

    const x = d3.scaleBand()
      .range([0, W]).padding(0.35)
      .domain(data.map((d: CategorySales) => d.category));
    const maxVal: number = data.reduce((m: number, d: CategorySales) => Math.max(m, d.invoiceCount), 0);
    const y = d3.scaleLinear().range([H, 0]).domain([0, (maxVal || 1) * 1.2]);

    // Grid lines
    const gg = svg.append('g');
    d3.axisLeft(y).tickSize(-W).tickFormat(() => '')(gg);
    gg.selectAll('line').style('stroke', 'rgba(255,255,255,0.05)').style('stroke-dasharray', '4,4');
    gg.select('.domain').remove();
    gg.selectAll('text').remove();

    const tip = this.makeTip(d3);

    svg.selectAll('.bar').data(data).enter().append('rect')
      .attr('class', 'bar')
      .attr('x', (d: CategorySales) => x(d.category) as number)
      .attr('width', x.bandwidth())
      .attr('y', H).attr('height', 0).attr('rx', 6)
      .style('fill', 'url(#barG)')
      .transition().duration(650).ease(d3.easeCubicOut)
      .delay((_d: any, i: number) => i * 40)
      .attr('y', (d: CategorySales) => y(d.invoiceCount) as number)
      .attr('height', (d: CategorySales) => H - (y(d.invoiceCount) as number));

    svg.selectAll('.blabel').data(data).enter().append('text')
      .attr('class', 'blabel')
      .attr('x', (d: CategorySales) => (x(d.category) as number) + x.bandwidth() / 2)
      .attr('y', (d: CategorySales) => (y(d.invoiceCount) as number) - 7)
      .attr('text-anchor', 'middle')
      .style('fill', 'var(--text-muted)').style('font-size', '12px').style('opacity', 0)
      .text((d: CategorySales) => String(d.invoiceCount))
      .transition().delay(720).duration(250).style('opacity', 1);

    svg.selectAll('.bar-hit').data(data).enter().append('rect')
      .attr('class', 'bar-hit')
      .attr('x', (d: CategorySales) => x(d.category) as number)
      .attr('width', x.bandwidth())
      .attr('y', 0).attr('height', H)
      .style('fill', 'transparent').style('cursor', 'pointer')
      .on('mouseover', (_event: any, d: any) => {
        svg.selectAll('.bar')
          .filter((dd: CategorySales) => dd.category === d.category)
          .transition().duration(50)
          .style('fill', 'url(#barGH)');
        const avgOrder = d.invoiceCount > 0 ? (d.totalRevenue / d.invoiceCount) : 0;
        tip.style('opacity', '1').html(
          `<div style="font-weight:600;color:var(--glow-purple);margin-bottom:3px">${d.category}</div>` +
          `<div style="font-size:15px;font-weight:700">${d.invoiceCount} Invoice${d.invoiceCount !== 1 ? 's' : ''}</div>` +
          `<div style="color:var(--text-muted);font-size:12px;margin-top:2px">` +
          `Total: $${(d.totalRevenue as number).toLocaleString(undefined, { maximumFractionDigits: 2 })}</div>` +
          `<div style="color:var(--text-muted);font-size:12px;margin-top:2px">Avg: $${Math.round(avgOrder).toLocaleString()} / order</div>`
        );
      })
      .on('mousemove', (event: any) => {
        tip.style('left', `${event.clientX + 16}px`).style('top', `${event.clientY - 44}px`);
      })
      .on('mouseout', (_event: any, d: any) => {
        svg.selectAll('.bar')
          .filter((dd: CategorySales) => dd.category === d.category)
          .transition().duration(50)
          .style('fill', 'url(#barG)');
        tip.style('opacity', '0');
      });

    const xA = svg.append('g')
      .attr('transform', `translate(0,${H})`)
      .call(d3.axisBottom(x).tickSize(0));
    xA.select('.domain').style('stroke', 'rgba(255,255,255,0.1)');
    xA.selectAll('text')
      .style('fill', 'var(--text-muted)').style('font-size', '11px')
      .attr('dy', '0.4em').attr('dx', '-0.8em')
      .attr('transform', 'rotate(-40)')
      .style('text-anchor', 'end');

    const yA = svg.append('g').call(d3.axisLeft(y).ticks(4));
    yA.select('.domain').remove();
    yA.selectAll('text').style('fill', 'var(--text-muted)').style('font-size', '11px');
    yA.selectAll('.tick line').style('stroke', 'rgba(255,255,255,0.07)');
  }

  // ── Chart: Line ───────────────────────────────────────────────────────
  renderLine(data: { period: Date; revenue: number; invoices: number }[], displayGranularity = 'monthly'): void {
    if (!this.lineContainer) return;
    const d3: any = getD3();
    if (!d3 || !data.length) return;

    data.forEach((d: any) => {
      if (typeof d.period === 'string') d.period = new Date(d.period);
    });

    const el = this.lineContainer.nativeElement;
    d3.select(el).selectAll('*').remove();

    const TOTAL_H = Math.max((el.offsetHeight as number) || 320, 280);
    const brushH = 48;
    const gap = 10;
    const margin = { top: 16, right: 20, bottom: brushH + gap + 28, left: 64 };
    const W = ((el.offsetWidth as number) || 700) - margin.left - margin.right;
    const H = TOTAL_H - margin.top - margin.bottom;

    const svgRoot = d3.select(el).append('svg')
      .attr('width', W + margin.left + margin.right)
      .attr('height', TOTAL_H)
      .style('display', 'block');

    const defs = svgRoot.append('defs');

    const ag = defs.append('linearGradient').attr('id', 'lineAreaFill')
      .attr('x1', '0%').attr('y1', '0%').attr('x2', '0%').attr('y2', '100%');
    ag.append('stop').attr('offset', '0%').attr('stop-color', '#a855f7').attr('stop-opacity', '0.30');
    ag.append('stop').attr('offset', '100%').attr('stop-color', '#a855f7').attr('stop-opacity', '0.00');

    defs.append('clipPath').attr('id', 'mainClip')
      .append('rect').attr('width', W).attr('height', H + 10).attr('y', -5);
    defs.append('clipPath').attr('id', 'brushClip')
      .append('rect').attr('width', W).attr('height', brushH);

    const chart = svgRoot.append('g').attr('transform', `translate(${margin.left},${margin.top})`);

    const xFull = d3.scaleTime().range([0, W]).domain(d3.extent(data, (d: any) => d.period) as [Date, Date]);
    const maxRev = (d3.max(data, (d: any) => d.revenue) as number) || 1;
    const yFull = d3.scaleLinear().range([H, 0]).domain([0, maxRev * 1.18]);

    let xNow = xFull.copy();

    const gridG = chart.append('g').attr('class', 'grid-group');
    const drawGrid = (_xScale: any) => {
      gridG.selectAll('*').remove();
      gridG.call(d3.axisLeft(yFull).tickSize(-W).ticks(5).tickFormat(() => ''));
      gridG.selectAll('line').style('stroke', 'rgba(255,255,255,0.05)').style('stroke-dasharray', '4,4');
      gridG.select('.domain').remove();
      gridG.selectAll('text').remove();
    };
    drawGrid(xNow);

    const buildArea = (xScale: any) => d3.area()
      .x((d: any) => xScale(d.period))
      .y0(H).y1((d: any) => yFull(d.revenue))
      .curve(d3.curveCatmullRom.alpha(0.5));

    const buildLine = (xScale: any) => d3.line()
      .x((d: any) => xScale(d.period))
      .y((d: any) => yFull(d.revenue))
      .curve(d3.curveCatmullRom.alpha(0.5));

    const areaPath = chart.append('path')
      .datum(data)
      .attr('fill', 'url(#lineAreaFill)')
      .attr('clip-path', 'url(#mainClip)')
      .attr('d', buildArea(xNow));

    const linePath = chart.append('path')
      .datum(data)
      .attr('fill', 'none')
      .attr('stroke', '#a855f7').attr('stroke-width', 2)
      .attr('clip-path', 'url(#mainClip)')
      .attr('d', buildLine(xNow));

    const dotsG = chart.append('g').attr('clip-path', 'url(#mainClip)');
    const renderDots = (xScale: any) => {
      dotsG.selectAll('.ldot').remove();
      const [t0, t1] = xScale.domain();
      const visible = data.filter((d: any) => d.period && d.period >= t0 && d.period <= t1);
      if (visible.length > 5000) return;
      dotsG.selectAll('.ldot').data(visible).enter().append('circle')
        .attr('class', 'ldot')
        .attr('cx', (d: any) => xScale(d.period))
        .attr('cy', (d: any) => yFull(d.revenue))
        .attr('r', visible.length < 50 ? 4 : 3)
        .attr('fill', '#a855f7').attr('stroke', 'rgba(15,10,30,0.8)').attr('stroke-width', 1.5)
        .style('pointer-events', 'none').style('opacity', 0);

      dotsG.selectAll('.ldot').transition().duration(400).style('opacity', 1);
    };
    renderDots(xNow);

    const xAxisG = chart.append('g').attr('transform', `translate(0,${H})`);
    const yAxisG = chart.append('g');

    const getTickFormat = () => {
      return (d: any) => (d as Date).toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
    };

    const drawAxes = (xScale: any) => {
      xAxisG.call(d3.axisBottom(xScale).ticks(6).tickFormat(getTickFormat()).tickSize(4));
      xAxisG.select('.domain').style('stroke', 'rgba(255,255,255,0.1)');
      xAxisG.selectAll('text').style('fill', 'var(--text-muted)').style('font-size', '11px').attr('dy', '1.4em');
      xAxisG.selectAll('.tick line').style('stroke', 'rgba(255,255,255,0.1)');

      yAxisG.call(
        d3.axisLeft(yFull).ticks(5)
          .tickFormat((d: any) => `$${Number(d) >= 1000 ? (Number(d) / 1000).toFixed(0) + 'k' : d}`)
      );
      yAxisG.select('.domain').remove();
      yAxisG.selectAll('text').style('fill', 'var(--text-muted)').style('font-size', '11px');
      yAxisG.selectAll('.tick line').remove();
    };
    drawAxes(xNow);

    const tip = this.makeTip(d3);
    const crossV = chart.append('line').attr('class', 'crosshair-v')
      .attr('y1', 0).attr('y2', H)
      .style('stroke', 'rgba(168,85,247,0.5)').style('stroke-width', '1')
      .style('stroke-dasharray', '4,3').style('pointer-events', 'none').style('opacity', 0);
    const crossH = chart.append('line').attr('class', 'crosshair-h')
      .attr('x1', 0).attr('x2', W)
      .style('stroke', 'rgba(168,85,247,0.3)').style('stroke-width', '1')
      .style('stroke-dasharray', '4,3').style('pointer-events', 'none').style('opacity', 0);
    const crossDot = chart.append('circle').attr('r', 5)
      .attr('fill', '#a855f7').attr('stroke', 'rgba(15,10,30,0.9)').attr('stroke-width', 2)
      .style('pointer-events', 'none').style('opacity', 0);

    const bisect = d3.bisector((d: any) => d.period).left;
    const overlay = chart.append('rect')
      .attr('width', W).attr('height', H)
      .style('fill', 'none').style('pointer-events', 'all').style('cursor', 'crosshair');

    overlay.on('mousemove', (event: any) => {
      const [mx] = d3.pointer(event);
      const x0 = xNow.invert(mx);
      const idx = bisect(data, x0, 1);
      const d0: any = data[idx - 1], d1: any = data[idx];
      if (!d0) return;
      const d: any = d1 && (x0.getTime() - d0.period.getTime() > d1.period.getTime() - x0.getTime()) ? d1 : d0;
      const cx = xNow(d.period), cy = yFull(d.revenue);

      crossV.attr('x1', cx).attr('x2', cx).style('opacity', 1);
      crossH.attr('y1', cy).attr('y2', cy).style('opacity', 1);
      crossDot.attr('cx', cx).attr('cy', cy).style('opacity', 1);

      const dateStr = (d.period as Date).toLocaleDateString('en-US', { month: 'long', year: 'numeric' });
      const revPerInv = d.invoices > 0 ? (d.revenue / d.invoices) : 0;

      tip.style('opacity', '1').html(
        `<div style="font-weight:700;color:var(--glow-purple);margin-bottom:4px">${dateStr}</div>` +
        `<div style="font-size:18px;font-weight:700">$${Math.round(d.revenue).toLocaleString()}</div>` +
        `<div style="color:var(--text-muted);font-size:12px;margin-top:2px">${d.invoices} invoice${d.invoices !== 1 ? 's' : ''} &nbsp;·&nbsp; $${Math.round(revPerInv).toLocaleString()}/avg</div>`
      );
      const bx = event.clientX, by = event.clientY;
      const tw = 180;
      tip.style('left', `${bx + (bx > window.innerWidth - tw - 30 ? -tw - 16 : 16)}px`)
        .style('top', `${by - 60}px`);
    });
    overlay.on('mouseleave', () => {
      crossV.style('opacity', 0);
      crossH.style('opacity', 0);
      crossDot.style('opacity', 0);
      tip.style('opacity', '0');
    });

    const brushTop = margin.top + H + gap + 28;
    const brushG = svgRoot.append('g').attr('transform', `translate(${margin.left},${brushTop})`);

    const yMini = d3.scaleLinear().range([brushH, 0]).domain(yFull.domain());
    brushG.append('path').datum(data)
      .attr('fill', 'rgba(168,85,247,0.15)')
      .attr('stroke', 'rgba(168,85,247,0.5)').attr('stroke-width', 1)
      .attr('d', d3.area()
        .x((d: any) => xFull(d.period))
        .y0(brushH).y1((d: any) => yMini(d.revenue))
        .curve(d3.curveCatmullRom.alpha(0.5))
      );

    const brush = d3.brushX()
      .extent([[0, 0], [W, brushH]])
      .on('brush end', (event: any) => {
        if (!event.selection) return;
        const [x0, x1] = event.selection.map((v: number) => xFull.invert(v));
        xNow = xFull.copy().domain([x0, x1]);
        areaPath.attr('d', buildArea(xNow));
        linePath.attr('d', buildLine(xNow));
        renderDots(xNow);
        drawAxes(xNow);
        drawGrid(xNow);
      });

    const brushSel = brushG.append('g').attr('class', 'brush').call(brush);
    brushSel.select('.selection')
      .style('fill', 'rgba(168,85,247,0.2)')
      .style('stroke', 'rgba(168,85,247,0.6)')
      .style('stroke-width', '1');
    brushSel.selectAll('.handle')
      .style('fill', 'rgba(168,85,247,0.8)')
      .style('width', '4px');

    const zoom = d3.zoom()
      .scaleExtent([1, data.length > 1 ? data.length : 50])
      .translateExtent([[0, 0], [W, H]])
      .extent([[0, 0], [W, H]])
      .on('zoom', (event: any) => {
        const newX = event.transform.rescaleX(xFull);
        xNow = newX;
        areaPath.attr('d', buildArea(xNow));
        linePath.attr('d', buildLine(xNow));
        renderDots(xNow);
        drawAxes(xNow);
        drawGrid(xNow);
        const [d0, d1] = xNow.domain();
        brushG.select('.brush').call(brush.move, [xFull(d0), xFull(d1)]);
      });

    overlay.call(zoom as any);
    chart.append('text')
      .attr('x', W).attr('y', -4)
      .attr('text-anchor', 'end')
      .style('fill', 'rgba(255,255,255,0.2)').style('font-size', '10px')
      .text('scroll to zoom · drag to pan');

    brushG.select('.brush').call(brush.move as any, [0, W]);
  }

  // ── Tooltip factory ───────────────────────────────────────────────────
  private makeTip(d3: any): any {
    let tip = d3.select('body').select('.dash-tooltip');
    if (tip.empty()) {
      tip = d3.select('body').append('div').attr('class', 'dash-tooltip');
    }
    tip
      .style('position', 'fixed')
      .style('pointer-events', 'none')
      .style('background', 'var(--bg-elevated)').style('backdrop-filter', 'blur(16px)')
      .style('color', 'var(--text-primary)')
      .style('border', '1px solid rgba(168,85,247,0.35)')
      .style('border-radius', '12px').style('padding', '10px 14px')
      .style('font-size', '13px').style('line-height', '1.6')
      .style('opacity', '0').style('transition', 'none').style('z-index', '9999')
      .style('box-shadow', '0 8px 32px rgba(0,0,0,0.35)');
    return tip;
  }
}