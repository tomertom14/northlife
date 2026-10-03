import {
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';

export interface LineSeries {
  key: string;
  label: string;
  /** A validated categorical slot; marks only, never text. */
  color: string;
  values: number[];
}

const HEIGHT = 248;
const TOP = 16;
const AXIS_BAND = 28;
const Y_LABELS = 44;
const END_LABELS = 64;
const numberFormat = new Intl.NumberFormat('he-IL');

/** Largest "nice" value (1, 2 or 5 × 10^k) at or above the data maximum, for clean ticks. */
export function niceCeiling(value: number): number {
  if (value <= 0) return 4;
  const magnitude = 10 ** Math.floor(Math.log10(value));
  const nice = [1, 2, 2.5, 5, 10].find((step) => step * magnitude >= value) ?? 10;
  return nice * magnitude;
}

/**
 * Daily line chart. Time runs right to left, following the Hebrew reading direction, so the most
 * recent day sits at the left edge with its value labelled. A crosshair snaps to the nearest day on
 * hover and on keyboard focus (arrow keys), and one tooltip lists every series. A table view of the
 * same numbers lives next to the chart, so the tooltip never gates a value.
 */
@Component({
  selector: 'app-line-chart',
  template: `
    <div class="legend" aria-hidden="true">
      @for (line of series(); track line.key) {
        <span class="key"><i [style.background]="line.color"></i>{{ line.label }}</span>
      }
    </div>
    <div
      class="plot"
      tabindex="0"
      role="group"
      [attr.aria-label]="ariaLabel()"
      (keydown)="onKey($event)"
      (focus)="onFocus()"
      (blur)="active.set(null)">
      <svg [attr.viewBox]="'0 0 ' + width() + ' ' + height" [attr.height]="height" role="img" [attr.aria-label]="ariaLabel()">
        @for (tick of ticks(); track tick.value) {
          <line class="grid" [attr.x1]="plotLeft()" [attr.x2]="plotRight()" [attr.y1]="tick.y" [attr.y2]="tick.y" />
          <text class="tick" [attr.x]="width() - 4" [attr.y]="tick.y + 4" text-anchor="end">{{ tick.label }}</text>
        }
        @for (label of xLabels(); track label.index) {
          <text class="tick" [attr.x]="label.x" [attr.y]="height - 8" text-anchor="middle">{{ label.text }}</text>
        }
        @for (line of paths(); track line.key) {
          <path class="line" [attr.d]="line.d" [attr.stroke]="line.color" />
        }
        @if (active() !== null) {
          <line class="crosshair" [attr.x1]="x(active()!)" [attr.x2]="x(active()!)" [attr.y1]="top" [attr.y2]="plotBottom" />
        }
        @for (end of ends(); track end.key) {
          <circle class="dot" [attr.cx]="end.x" [attr.cy]="end.y" r="4" [attr.fill]="end.color" />
          @if (end.showLabel) {
            <text class="end-label" [attr.x]="end.x - 10" [attr.y]="end.y + 4" text-anchor="end">{{ end.text }}</text>
          }
        }
        @if (active() !== null) {
          @for (line of series(); track line.key) {
            <circle class="dot" [attr.cx]="x(active()!)" [attr.cy]="y(line.values[active()!] ?? 0)" r="4" [attr.fill]="line.color" />
          }
        }
        <rect
          class="hit"
          [attr.x]="plotLeft() - 8"
          [attr.y]="top"
          [attr.width]="plotRight() - plotLeft() + 16"
          [attr.height]="plotBottom - top"
          (pointermove)="onPointer($event)"
          (pointerleave)="active.set(null)" />
      </svg>
      @if (tooltip(); as tip) {
        <div class="tooltip" [style.left.px]="tip.left" role="status">
          <span class="tip-date">{{ tip.label }}</span>
          @for (row of tip.rows; track row.key) {
            <span class="tip-row"><i [style.background]="row.color"></i><strong>{{ row.value }}</strong> {{ row.label }}</span>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; position: relative; }
    .legend { display: flex; flex-wrap: wrap; gap: .35rem 1.25rem; margin-bottom: .5rem; }
    .key { align-items: center; color: var(--muted); display: inline-flex; font-size: .8125rem; gap: .45rem; }
    .key i, .tip-row i { border-radius: 2px; display: inline-block; height: 2px; width: 14px; }
    .plot { border-radius: var(--radius-sm); outline-offset: 4px; position: relative; }
    /* Every SVG label is a number or a date, so anchors use left-to-right geometry. */
    svg { direction: ltr; display: block; overflow: visible; width: 100%; }
    .grid { stroke: #e6eae8; stroke-width: 1; }
    .tick { fill: var(--muted); font-size: 11px; font-variant-numeric: tabular-nums; }
    .line { fill: none; stroke-linecap: round; stroke-linejoin: round; stroke-width: 2; }
    .crosshair { stroke: #9aa39f; stroke-width: 1; }
    .dot { stroke: var(--surface); stroke-width: 2; }
    .end-label { fill: var(--ink); font-size: 12px; font-weight: 600; font-variant-numeric: tabular-nums; }
    .hit { cursor: crosshair; fill: transparent; }
    .tooltip {
      background: var(--surface); border: 1px solid var(--line-strong); border-radius: var(--radius-sm);
      box-shadow: var(--shadow-pop); display: grid; gap: .2rem; min-width: 9.5rem; padding: .5rem .65rem;
      pointer-events: none; position: absolute; top: 0; transform: translateX(-50%); z-index: 2;
    }
    .tip-date { color: var(--muted); font-size: .75rem; }
    .tip-row { align-items: center; color: var(--muted); display: flex; font-size: .8125rem; gap: .4rem; white-space: nowrap; }
    .tip-row strong { color: var(--ink); font-size: .9375rem; font-variant-numeric: tabular-nums; }
  `],
})
export class LineChart {
  /** One label per point, shown in the tooltip. */
  readonly labels = input.required<string[]>();
  /** Shorter labels for the axis ticks; defaults to the tooltip labels. */
  readonly tickLabels = input<string[] | null>(null);
  readonly series = input.required<LineSeries[]>();
  readonly ariaLabel = input('');

  readonly height = HEIGHT;
  readonly top = TOP;
  readonly plotBottom = HEIGHT - AXIS_BAND;
  readonly width = signal(640);
  readonly active = signal<number | null>(null);

  readonly plotLeft = computed(() => END_LABELS);
  readonly plotRight = computed(() => Math.max(END_LABELS + 40, this.width() - Y_LABELS));
  private readonly count = computed(() => this.labels().length);
  private readonly maximum = computed(() =>
    niceCeiling(Math.max(0, ...this.series().flatMap((line) => line.values))),
  );

  readonly ticks = computed(() =>
    [0, 0.25, 0.5, 0.75, 1].map((share) => {
      const value = Math.round(this.maximum() * share * 100) / 100;
      return { value, y: this.y(value), label: numberFormat.format(value) };
    }),
  );

  readonly xLabels = computed(() => {
    const count = this.count();
    if (count === 0) return [];
    // Roughly one label per 90px, always including the first and the last day.
    const every = Math.max(1, Math.ceil(count / Math.max(2, Math.floor((this.plotRight() - this.plotLeft()) / 90))));
    const indexes = new Set<number>([0, count - 1]);
    for (let index = count - 1; index >= 0; index -= every) indexes.add(index);
    const sorted = [...indexes].sort((a, b) => a - b);
    // Drop labels that would crowd their neighbour.
    return sorted
      .filter((index, position) => position === 0 || index === count - 1 || this.x(sorted[position - 1]) - this.x(index) >= 60)
      .map((index) => ({ index, x: this.x(index), text: (this.tickLabels() ?? this.labels())[index] }));
  });

  readonly paths = computed(() =>
    this.series().map((line) => ({
      key: line.key,
      color: line.color,
      d: line.values.map((value, index) => `${index === 0 ? 'M' : 'L'}${this.x(index).toFixed(1)},${this.y(value).toFixed(1)}`).join(''),
    })),
  );

  /** End markers and values on the latest day; labels are dropped when they would collide. */
  readonly ends = computed(() => {
    const last = this.count() - 1;
    if (last < 0) return [];
    const ends = this.series().map((line) => ({
      key: line.key,
      color: line.color,
      x: this.x(last),
      y: this.y(line.values[last] ?? 0),
      text: numberFormat.format(line.values[last] ?? 0),
      showLabel: true,
    }));
    for (let a = 0; a < ends.length; a++) {
      for (let b = a + 1; b < ends.length; b++) {
        if (Math.abs(ends[a].y - ends[b].y) < 14) {
          ends[a].showLabel = false;
          ends[b].showLabel = false;
        }
      }
    }
    return ends;
  });

  readonly tooltip = computed(() => {
    const index = this.active();
    if (index === null) return null;
    const left = Math.min(Math.max(this.x(index), 80), this.width() - 80);
    return {
      left,
      label: this.labels()[index],
      rows: this.series().map((line) => ({
        key: line.key,
        color: line.color,
        label: line.label,
        value: numberFormat.format(line.values[index] ?? 0),
      })),
    };
  });

  constructor() {
    const host = inject(ElementRef<HTMLElement>).nativeElement;
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      this.width.set(Math.max(280, host.clientWidth));
      if (typeof ResizeObserver === 'undefined') return;
      const observer = new ResizeObserver(([entry]) => this.width.set(Math.max(280, Math.round(entry.contentRect.width))));
      observer.observe(host);
      destroyRef.onDestroy(() => observer.disconnect());
    });
  }

  /** Index 0 (oldest) at the right edge, the latest day at the left. */
  x(index: number): number {
    const count = this.count();
    if (count <= 1) return (this.plotLeft() + this.plotRight()) / 2;
    return this.plotRight() - (index * (this.plotRight() - this.plotLeft())) / (count - 1);
  }

  y(value: number): number {
    return this.plotBottom - (value / this.maximum()) * (this.plotBottom - TOP);
  }

  onPointer(event: PointerEvent): void {
    const svg = (event.target as SVGElement).ownerSVGElement;
    if (!svg) return;
    const box = svg.getBoundingClientRect();
    const px = ((event.clientX - box.left) / box.width) * this.width();
    const count = this.count();
    if (count === 0) return;
    const step = count > 1 ? (this.plotRight() - this.plotLeft()) / (count - 1) : 1;
    this.active.set(Math.min(count - 1, Math.max(0, Math.round((this.plotRight() - px) / step))));
  }

  onFocus(): void {
    if (this.active() === null && this.count() > 0) this.active.set(this.count() - 1);
  }

  onKey(event: KeyboardEvent): void {
    const count = this.count();
    if (count === 0) return;
    const current = this.active() ?? count - 1;
    // Right is earlier and left is later, matching the axis direction.
    const next =
      event.key === 'ArrowRight' ? current - 1
      : event.key === 'ArrowLeft' ? current + 1
      : event.key === 'Home' ? 0
      : event.key === 'End' ? count - 1
      : null;
    if (next === null) return;
    event.preventDefault();
    this.active.set(Math.min(count - 1, Math.max(0, next)));
  }
}
