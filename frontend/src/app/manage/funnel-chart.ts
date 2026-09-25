import { Component, computed, input, signal } from '@angular/core';

export interface FunnelStage {
  key: string;
  label: string;
  value: number;
  /** One step of a single-hue ordinal ramp, darkest for the widest stage. */
  color: string;
}

const numberFormat = new Intl.NumberFormat('he-IL');
const percentFormat = new Intl.NumberFormat('he-IL', { style: 'percent', maximumFractionDigits: 1 });

/**
 * Horizontal funnel. Bars grow from the right (the start edge in Hebrew), 20px thick with a rounded
 * data end, and each shows its value at the tip and its conversion from the previous stage, so the
 * hover readout adds detail but never hides a number.
 */
@Component({
  selector: 'app-funnel-chart',
  template: `
    <ol class="funnel">
      @for (stage of rows(); track stage.key; let first = $first) {
        <li>
          <div class="head">
            <span class="label">{{ stage.label }}</span>
            @if (!first) {
              <span class="rate">{{ stage.rateText }}</span>
            }
          </div>
          <div
            class="track"
            tabindex="0"
            [attr.aria-label]="stage.label + ': ' + stage.valueText + (first ? '' : ', ' + stage.rateText)"
            (pointerenter)="hovered.set(stage.key)"
            (pointerleave)="hovered.set(null)"
            (focus)="hovered.set(stage.key)"
            (blur)="hovered.set(null)">
            <span class="bar" [class.lift]="hovered() === stage.key" [style.inline-size.%]="stage.share" [style.background]="stage.color"></span>
            <span class="value">{{ stage.valueText }}</span>
            @if (hovered() === stage.key) {
              <span class="tooltip" role="status">
                <strong>{{ stage.valueText }}</strong> {{ stage.label }}
                @if (!first) {
                  <span class="sub">{{ stage.rateText }}</span>
                }
              </span>
            }
          </div>
        </li>
      }
    </ol>
  `,
  styles: [`
    .funnel { display: grid; gap: .9rem; list-style: none; margin: 0; padding: 0; }
    .head { align-items: baseline; display: flex; flex-wrap: wrap; gap: .25rem .75rem; margin-bottom: .3rem; }
    .label { font-size: .875rem; font-weight: 600; }
    .rate { color: var(--muted); font-size: .8125rem; }
    .track { align-items: center; border-radius: var(--radius-sm); display: flex; gap: .5rem; min-height: 28px; outline-offset: 3px; position: relative; }
    .bar {
      block-size: 20px; border-end-end-radius: 4px; border-start-end-radius: 4px; display: block;
      flex: none; max-inline-size: calc(100% - 4.5rem); min-inline-size: 2px; transition: filter 120ms ease;
    }
    .bar.lift { filter: brightness(1.12); }
    .value { font-size: .875rem; font-variant-numeric: tabular-nums; font-weight: 600; white-space: nowrap; }
    .tooltip {
      background: var(--surface); border: 1px solid var(--line-strong); border-radius: var(--radius-sm);
      box-shadow: var(--shadow-pop); display: grid; font-size: .8125rem; gap: .15rem; inset-block-end: calc(100% + 6px);
      inset-inline-start: 0; padding: .45rem .6rem; pointer-events: none; position: absolute; white-space: nowrap; z-index: 2;
    }
    .tooltip strong { font-size: .9375rem; }
    .sub { color: var(--muted); }
  `],
})
export class FunnelChart {
  readonly stages = input.required<FunnelStage[]>();
  readonly hovered = signal<string | null>(null);

  readonly rows = computed(() => {
    const stages = this.stages();
    const max = Math.max(1, ...stages.map((stage) => stage.value));
    return stages.map((stage, index) => {
      const previous = index > 0 ? stages[index - 1].value : 0;
      return {
        ...stage,
        share: (stage.value / max) * 100,
        valueText: numberFormat.format(stage.value),
        rateText: previous > 0 ? `${percentFormat.format(stage.value / previous)} מהשלב הקודם` : 'אין עדיין נתונים לשלב הקודם',
      };
    });
  });
}
