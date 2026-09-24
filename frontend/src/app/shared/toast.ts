import { Component, Injectable, inject, signal } from '@angular/core';

export interface ToastMessage {
  id: number;
  text: string;
  kind: 'success' | 'error';
}

/** One short confirmation at a time, announced politely and pinned to the viewport bottom. */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private sequence = 0;
  private timer?: ReturnType<typeof setTimeout>;
  readonly current = signal<ToastMessage | null>(null);

  success(text: string): void {
    this.show(text, 'success');
  }

  error(text: string): void {
    this.show(text, 'error');
  }

  dismiss(): void {
    clearTimeout(this.timer);
    this.current.set(null);
  }

  private show(text: string, kind: ToastMessage['kind']): void {
    clearTimeout(this.timer);
    this.current.set({ id: ++this.sequence, text, kind });
    this.timer = setTimeout(() => this.current.set(null), kind === 'error' ? 8000 : 5000);
  }
}

@Component({
  selector: 'app-toast',
  template: `
    <div class="toast-region" aria-live="polite" role="status">
      @if (toasts.current(); as toast) {
        <div class="toast" [class.error]="toast.kind === 'error'">
          <span>{{ toast.text }}</span>
          <button type="button" (click)="toasts.dismiss()" aria-label="סגירת ההודעה">
            <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M6 6l12 12M18 6L6 18" /></svg>
          </button>
        </div>
      }
    </div>
  `,
  styles: [`
    .toast-region { bottom: max(1rem, env(safe-area-inset-bottom)); display: flex; justify-content: center; left: 1rem; pointer-events: none; position: fixed; right: 1rem; z-index: 40; }
    .toast { align-items: center; background: var(--ink); border-radius: var(--radius-sm); box-shadow: var(--shadow-pop); color: var(--ground); display: flex; gap: .75rem; max-width: 32rem; padding: .7rem .7rem .7rem 1rem; pointer-events: auto; }
    .toast.error { background: var(--danger); }
    button { background: none; border: 0; color: inherit; cursor: pointer; display: grid; min-height: 2.25rem; min-width: 2.25rem; place-items: center; }
    svg { fill: none; height: 1.1rem; stroke: currentColor; stroke-linecap: round; stroke-width: 2; width: 1.1rem; }
  `],
})
export class Toast {
  readonly toasts = inject(ToastService);
}
