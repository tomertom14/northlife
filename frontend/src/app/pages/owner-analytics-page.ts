import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FunnelChart, FunnelStage } from '../manage/funnel-chart';
import { LineChart, LineSeries } from '../manage/line-chart';
import { StatusBadge } from '../manage/status-badge';
import { AnalyticsRange, OwnerAnalytics, OwnerAnalyticsApi } from '../owner/owner-analytics-api';
import { formatDateKey, formatShortDate } from '../shared/jerusalem-time';

const compact = new Intl.NumberFormat('he-IL', { notation: 'compact', maximumFractionDigits: 1 });
const whole = new Intl.NumberFormat('he-IL');
const percent = new Intl.NumberFormat('he-IL', { style: 'percent', maximumFractionDigits: 1 });

/** Validated categorical slots 1 and 2 (dataviz reference palette, light surface). */
const VIEWS_COLOR = '#2a78d6';
const VISITORS_COLOR = '#eb6834';
/** Single-hue ordinal ramp for the funnel stages (steps 600, 450, 300). */
const FUNNEL_COLORS = ['#184f95', '#2a78d6', '#6da7ec'];

@Component({
  selector: 'app-owner-analytics-page',
  imports: [RouterLink, LineChart, FunnelChart, StatusBadge],
  templateUrl: './owner-analytics-page.html',
  styleUrl: './owner-analytics-page.scss',
})
export class OwnerAnalyticsPage implements OnInit {
  private readonly api = inject(OwnerAnalyticsApi);

  readonly ranges: { days: AnalyticsRange; label: string }[] = [
    { days: 7, label: '7 ימים' },
    { days: 30, label: '30 יום' },
    { days: 90, label: '90 יום' },
  ];
  readonly range = signal<AnalyticsRange>(30);
  readonly data = signal<OwnerAnalytics | null>(null);
  readonly loading = signal(true);
  readonly failed = signal(false);
  readonly date = formatShortDate;
  readonly dayLabel = formatDateKey;
  readonly whole = (value: number) => whole.format(value);
  readonly percent = (value: number | null) => (value === null ? '—' : percent.format(value));

  readonly tiles = computed(() => {
    const totals = this.data()?.totals;
    if (!totals) return [];
    return [
      { label: 'צפיות בדפי האירועים', value: compact.format(totals.detailViews), note: `${whole.format(totals.impressions)} הופעות בפיד` },
      { label: 'מבקרים ייחודיים', value: compact.format(totals.uniqueVisitors), note: 'הערכה, בדיוק של כאחוז' },
      { label: 'ניווטים לאירוע', value: compact.format(totals.navigations), note: 'Waze או Google Maps' },
      { label: 'שיעור הקלקה', value: this.percent(totals.clickThroughRate), note: 'צפיות מתוך הופעות' },
    ];
  });

  readonly tickLabels = computed(() => (this.data()?.daily ?? []).map((day) => formatDateKey(day.day)));
  /** The last day is today and still filling up; say so rather than let the drop look like a slump. */
  readonly labels = computed(() => {
    const data = this.data();
    return (data?.daily ?? []).map((day) => (day.day === data?.to ? `${formatDateKey(day.day)} (היום, עד עכשיו)` : formatDateKey(day.day)));
  });

  readonly series = computed<LineSeries[]>(() => {
    const daily = this.data()?.daily ?? [];
    return [
      { key: 'views', label: 'צפיות בדפי האירועים', color: VIEWS_COLOR, values: daily.map((day) => day.detailViews) },
      { key: 'visitors', label: 'מבקרים ייחודיים', color: VISITORS_COLOR, values: daily.map((day) => day.uniqueVisitors) },
    ];
  });

  readonly funnel = computed<FunnelStage[]>(() => {
    const totals = this.data()?.totals;
    if (!totals) return [];
    return [
      { key: 'impressions', label: 'הופעות בפיד ובבחירות העורכים', value: totals.impressions, color: FUNNEL_COLORS[0] },
      { key: 'views', label: 'כניסות לדף האירוע', value: totals.detailViews, color: FUNNEL_COLORS[1] },
      { key: 'navigations', label: 'ניווטים לאירוע', value: totals.navigations, color: FUNNEL_COLORS[2] },
    ];
  });

  readonly events = computed(() =>
    [...(this.data()?.events ?? [])].sort((a, b) => b.detailViews - a.detailViews || b.impressions - a.impressions),
  );
  readonly hasData = computed(() => {
    const totals = this.data()?.totals;
    return !!totals && totals.impressions + totals.detailViews + totals.navigations + totals.shares > 0;
  });
  readonly chartLabel = computed(() => {
    const data = this.data();
    if (!data) return '';
    return `צפיות ומבקרים ייחודיים לפי יום, מ־${formatDateKey(data.from)} עד ${formatDateKey(data.to)}. חצים ימינה ושמאלה עוברים בין הימים.`;
  });

  ngOnInit(): void {
    this.load();
  }

  select(days: AnalyticsRange): void {
    if (days === this.range()) return;
    this.range.set(days);
    this.load();
  }

  load(): void {
    // Refetch keeps the previous render on screen, dimmed, instead of flashing a skeleton.
    this.loading.set(true);
    this.failed.set(false);
    this.api.get(this.range()).subscribe({
      next: (data) => {
        this.data.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }
}
