import {
  Component, OnInit, OnDestroy, ElementRef, ViewChild,
  inject, signal, effect, computed
} from '@angular/core';
import { CommonModule, CurrencyPipe, NgClass } from '@angular/common';
import { AnalyticsService, CategorySales, ProductTrend } from '../../core/services/analytics.service';
import { forkJoin } from 'rxjs';

const getD3 = (): any => (window as any)['d3'];

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, CurrencyPipe, NgClass],
  templateUrl: './dashboard.html',
  styleUrls: ['./dashboard.css']
})
export class Dashboard implements OnInit, OnDestroy {

  private analyticsService = inject(AnalyticsService);

  categorySales = signal<CategorySales[]>([]);
  trendingProducts = signal<ProductTrend[]>([]);
  isLoading = signal(true);
  selectedRange = signal<'30d' | '90d' | '12m' | 'all'>('all');

  totalRevenue = computed(() =>
    this.categorySales().reduce((s, c) => s + (c.totalRevenue || 0), 0)
  );
  totalInvoices = computed(() =>
    this.categorySales().reduce((s, c) => s + (c.invoiceCount || 0), 0)
  );
  avgOrderValue = computed(() => {
    const inv = this.totalInvoices();
    return inv > 0 ? this.totalRevenue() / inv : 0;
  });
  topCategory = computed(() => {
    const sales = this.categorySales();
    return sales.length > 0
      ? [...sales].sort((a, b) => b.totalRevenue - a.totalRevenue)[0].category
      : 'N/A';
  });

  @ViewChild('donutContainer') donutContainer!: ElementRef;
  @ViewChild('barContainer') barContainer!: ElementRef;

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
        }, 80);
      }
    });
  }

  ngOnInit(): void {
    this.loadData();
    window.addEventListener('resize', this.resizeListener);
  }

  ngOnDestroy(): void {
    window.removeEventListener('resize', this.resizeListener);
    getD3()?.selectAll('.dash-tooltip').remove();
  }

  setRange(range: '30d' | '90d' | '12m' | 'all'): void {
    this.selectedRange.set(range);
    this.loadData();
  }

  loadData(): void {
    this.isLoading.set(true);
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

    forkJoin({
      categories: this.analyticsService.getCategorySales(startDate, endDate),
      trending: this.analyticsService.getTrendingProducts(startDate, endDate, 5)
    }).subscribe({
      next: (res: { categories: CategorySales[]; trending: ProductTrend[] }) => {
        this.categorySales.set(res.categories || []);
        this.trendingProducts.set(res.trending || []);
        this.isLoading.set(false);
      },
      error: (err: unknown) => {
        console.error('Dashboard load failed:', err);
        this.isLoading.set(false);
      }
    });
  }

  growthClass(rate: number): string {
    return rate >= 0 ? 'growth-pos' : 'growth-neg';
  }

  growthLabel(rate: number): string {
    const pct = (Math.abs(rate) * 100).toFixed(1);
    return rate >= 0 ? `+${pct}%` : `-${pct}%`;
  }

  renderDonut(data: CategorySales[]): void {
    if (!this.donutContainer) return;
    const d3: any = getD3();
    if (!d3) return;
    const el = this.donutContainer.nativeElement;
    d3.select(el).selectAll('*').remove();

    const W: number = (el.offsetWidth as number) || 320;
    const H = 300;
    const R = Math.min(W, H) / 2 - 24;

    const svgRoot = d3.select(el).append('svg').attr('width', W).attr('height', H);
    const defs = svgRoot.append('defs');
    const svg = svgRoot.append('g').attr('transform', `translate(${W / 2},${H / 2})`);

    const palette = ['#a855f7', '#00e5ff', '#4ade80', '#fbbf24', '#f472b6', '#60a5fa'];

    palette.forEach((c: string, i: number) => {
      const g = defs.append('radialGradient').attr('id', `rg${i}`).attr('cx', '40%').attr('cy', '40%');
      g.append('stop').attr('offset', '0%').attr('stop-color', c).attr('stop-opacity', '1');
      g.append('stop').attr('offset', '100%').attr('stop-color', c).attr('stop-opacity', '0.65');
    });

    const color = d3.scaleOrdinal()
      .domain(data.map((d: CategorySales) => d.category))
      .range(palette);

    const makeArc = (inner: number, outer: number) =>
      d3.arc().innerRadius(inner).outerRadius(outer).cornerRadius(5).padAngle(0.028);
    const arc = makeArc(R * 0.54, R);
    const arcH = makeArc(R * 0.54, R + 9);

    const pie = d3.pie().value((d: CategorySales) => d.totalRevenue).sort(null);

    d3.selectAll('.dash-tooltip').remove();
    const tip = d3.select('body').append('div').attr('class', 'dash-tooltip')
      .style('position', 'fixed').style('pointer-events', 'none')
      .style('background', 'var(--bg-elevated)').style('backdrop-filter', 'blur(16px)')
      .style('color', 'var(--text-primary)')
      .style('border', '1px solid rgba(168,85,247,0.35)')
      .style('border-radius', '12px').style('padding', '10px 14px')
      .style('font-size', '13px').style('line-height', '1.6')
      .style('opacity', '0').style('transition', 'opacity 0.15s').style('z-index', '9999')
      .style('box-shadow', '0 8px 32px rgba(0,0,0,0.35)');

    svg.selectAll('path').data(pie(data)).enter().append('path')
      .attr('d', (d: any) => arc(d))
      .attr('fill', (_d: any, i: number) => `url(#rg${i % palette.length})`)
      .style('opacity', 0)
      .on('mouseover', (event: any, d: any) => {
        d3.select(event.currentTarget).transition().duration(150).attr('d', (dd: any) => arcH(dd));
        const pct = this.totalRevenue() > 0
          ? ((d.data.totalRevenue / this.totalRevenue()) * 100).toFixed(1) : '0';
        tip.style('opacity', '1').html(
          `<div style="font-weight:600;color:var(--glow-purple)">${d.data.category}</div>` +
          `<div>$${(d.data.totalRevenue as number).toLocaleString()}</div>` +
          `<div style="color:var(--text-muted)">${pct}% of total</div>`
        );
      })
      .on('mousemove', (event: any) => {
        tip.style('left', `${event.clientX + 16}px`).style('top', `${event.clientY - 44}px`);
      })
      .on('mouseout', (event: any) => {
        d3.select(event.currentTarget).transition().duration(150).attr('d', (dd: any) => arc(dd));
        tip.style('opacity', '0');
      })
      .transition().duration(550).delay((_d: any, i: number) => i * 70).style('opacity', 1);

    // Center text
    svg.append('text').attr('text-anchor', 'middle').attr('dy', '-0.35em')
      .style('fill', 'var(--text-muted)').style('font-size', '11px').style('letter-spacing', '1px')
      .text('TOTAL');
    svg.append('text').attr('text-anchor', 'middle').attr('dy', '1.1em')
      .style('fill', 'var(--text-primary)').style('font-size', '22px').style('font-weight', '700')
      .text(`$${(this.totalRevenue() / 1000).toFixed(1)}k`);

    // Legend
    const lg = svg.append('g').attr('transform', `translate(${-W / 2 + 10}, ${R + 14})`);
    data.forEach((d: CategorySales, i: number) => {
      const col = i % 2, row = Math.floor(i / 2);
      const lx = col * (W / 2 - 8), ly = row * 20;
      lg.append('circle').attr('cx', lx + 5).attr('cy', ly + 5).attr('r', 5)
        .style('fill', palette[i % palette.length]);
      lg.append('text').attr('x', lx + 14).attr('y', ly + 9)
        .style('fill', 'var(--text-muted)').style('font-size', '11px')
        .text(d.category.length > 18 ? d.category.slice(0, 17) + '…' : d.category);
    });
  }

  renderBar(data: CategorySales[]): void {
    if (!this.barContainer) return;
    const d3: any = getD3();
    if (!d3) return;
    const el = this.barContainer.nativeElement;
    d3.select(el).selectAll('*').remove();

    const margin = { top: 16, right: 16, bottom: 56, left: 50 };
    const W = ((el.offsetWidth as number) || 480) - margin.left - margin.right;
    const H = 280 - margin.top - margin.bottom;

    const svgRoot = d3.select(el).append('svg')
      .attr('width', W + margin.left + margin.right)
      .attr('height', H + margin.top + margin.bottom);
    const defs = svgRoot.append('defs');
    const svg = svgRoot.append('g').attr('transform', `translate(${margin.left},${margin.top})`);

    // Gradient for bars
    const bg = defs.append('linearGradient').attr('id', 'barG').attr('x1', '0%').attr('y1', '0%').attr('x2', '0%').attr('y2', '100%');
    bg.append('stop').attr('offset', '0%').attr('stop-color', '#a855f7');
    bg.append('stop').attr('offset', '100%').attr('stop-color', '#00e5ff').attr('stop-opacity', '0.75');

    const bgh = defs.append('linearGradient').attr('id', 'barGH').attr('x1', '0%').attr('y1', '0%').attr('x2', '0%').attr('y2', '100%');
    bgh.append('stop').attr('offset', '0%').attr('stop-color', '#c084fc');
    bgh.append('stop').attr('offset', '100%').attr('stop-color', '#38bdf8').attr('stop-opacity', '0.9');

    const x = d3.scaleBand().range([0, W]).padding(0.38)
      .domain(data.map((d: CategorySales) => d.category));
    const maxVal: number = data.reduce((m: number, d: CategorySales) => Math.max(m, d.invoiceCount), 0);
    const y = d3.scaleLinear().range([H, 0]).domain([0, (maxVal || 1) * 1.2]);

    // Grid
    const gg = svg.append('g');
    d3.axisLeft(y).tickSize(-W).tickFormat(() => '')(gg);
    gg.selectAll('line').style('stroke', 'rgba(255,255,255,0.05)').style('stroke-dasharray', '4,4');
    gg.select('.domain').remove();
    gg.selectAll('text').remove();

    // Tooltip (reuse if exists)
    if (d3.selectAll('.dash-tooltip').empty()) {
      d3.select('body').append('div').attr('class', 'dash-tooltip')
        .style('position', 'fixed').style('pointer-events', 'none')
        .style('background', 'var(--bg-elevated)').style('backdrop-filter', 'blur(16px)')
        .style('color', 'var(--text-primary)')
        .style('border', '1px solid rgba(168,85,247,0.35)')
        .style('border-radius', '12px').style('padding', '10px 14px')
        .style('font-size', '13px').style('line-height', '1.6')
        .style('opacity', '0').style('transition', 'opacity 0.15s').style('z-index', '9999')
        .style('box-shadow', '0 8px 32px rgba(0,0,0,0.35)');
    }
    const tip = d3.select('.dash-tooltip');

    // Bars
    svg.selectAll('.bar').data(data).enter().append('rect')
      .attr('class', 'bar')
      .attr('x', (d: CategorySales) => x(d.category) as number)
      .attr('width', x.bandwidth())
      .attr('y', H).attr('height', 0).attr('rx', 6)
      .style('fill', 'url(#barG)').style('cursor', 'pointer')
      .on('mouseover', (event: any, d: any) => {
        d3.select(event.currentTarget).style('fill', 'url(#barGH)');
        tip.style('opacity', '1').html(
          `<div style="font-weight:600;color:var(--glow-purple)">${d.category}</div>` +
          `<div>${d.invoiceCount} invoices</div>` +
          `<div style="color:var(--text-muted)">$${(d.totalRevenue as number).toLocaleString()}</div>`
        );
      })
      .on('mousemove', (event: any) => {
        tip.style('left', `${event.clientX + 16}px`).style('top', `${event.clientY - 44}px`);
      })
      .on('mouseout', (event: any) => {
        d3.select(event.currentTarget).style('fill', 'url(#barG)');
        tip.style('opacity', '0');
      })
      .transition().duration(650).ease(d3.easeCubicOut).delay((_d: any, i: number) => i * 55)
      .attr('y', (d: CategorySales) => y(d.invoiceCount) as number)
      .attr('height', (d: CategorySales) => H - (y(d.invoiceCount) as number));

    // Value labels
    svg.selectAll('.blabel').data(data).enter().append('text')
      .attr('class', 'blabel')
      .attr('x', (d: CategorySales) => (x(d.category) as number) + x.bandwidth() / 2)
      .attr('y', (d: CategorySales) => (y(d.invoiceCount) as number) - 7)
      .attr('text-anchor', 'middle')
      .style('fill', 'var(--text-muted)').style('font-size', '12px').style('opacity', 0)
      .text((d: CategorySales) => String(d.invoiceCount))
      .transition().delay(700).duration(250).style('opacity', 1);

    // X axis
    const xA = svg.append('g').attr('transform', `translate(0,${H})`).call(d3.axisBottom(x).tickSize(0));
    xA.select('.domain').style('stroke', 'rgba(255,255,255,0.1)');
    xA.selectAll('text').style('fill', 'var(--text-muted)').style('font-size', '11px').attr('dy', '1.4em')
      .each(function (this: any) {
        const s = d3.select(this), t: string = s.text();
        if (t.length > 14) s.text(t.slice(0, 13) + '…');
      });

    // Y axis
    const yA = svg.append('g').call(d3.axisLeft(y).ticks(4));
    yA.select('.domain').remove();
    yA.selectAll('text').style('fill', 'var(--text-muted)').style('font-size', '11px');
    yA.selectAll('.tick line').style('stroke', 'rgba(255,255,255,0.07)');
  }
}
