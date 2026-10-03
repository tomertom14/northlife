import { Component, ElementRef, afterNextRender, inject, input, output, signal, viewChild } from '@angular/core';
import { PublicEventsApi } from '../public/public-events-api';

interface GoogleIdentityServices {
  accounts: {
    id: {
      initialize(options: { client_id: string; callback: (response: { credential: string }) => void; ux_mode?: string }): void;
      renderButton(element: HTMLElement, options: Record<string, unknown>): void;
    };
  };
}

let scriptLoad: Promise<GoogleIdentityServices> | null = null;

function loadGoogleIdentity(): Promise<GoogleIdentityServices> {
  scriptLoad ??= new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = 'https://accounts.google.com/gsi/client';
    script.async = true;
    script.onload = () => {
      const google = (window as unknown as { google?: GoogleIdentityServices }).google;
      if (google?.accounts?.id) resolve(google);
      else reject(new Error('Google Identity Services did not load.'));
    };
    script.onerror = () => {
      scriptLoad = null;
      reject(new Error('Google Identity Services could not be reached.'));
    };
    document.head.append(script);
  });
  return scriptLoad;
}

/**
 * Google's own "Sign in with Google" button. Renders only when the server exposes an OAuth
 * client ID, and emits the ID token for the API to verify.
 */
@Component({
  selector: 'app-google-button',
  template: `
    @if (available()) {
      <div class="divider" aria-hidden="true"><span>או</span></div>
    }
    <div #slot class="slot"></div>
  `,
  styles: [`
    :host { display: block; }
    .slot { display: flex; justify-content: center; }
    .divider { align-items: center; color: var(--muted); display: flex; font-size: .875rem; gap: .75rem; margin-bottom: 1rem; }
    .divider::before, .divider::after { background: var(--line); content: ''; flex: 1; height: 1px; }
  `],
})
export class GoogleButton {
  private readonly api = inject(PublicEventsApi);
  private readonly slot = viewChild.required<ElementRef<HTMLElement>>('slot');
  readonly text = input<'signin_with' | 'signup_with' | 'continue_with'>('continue_with');
  readonly credential = output<string>();
  readonly available = signal(false);

  constructor() {
    afterNextRender(() => {
      this.api.getPublicConfiguration().subscribe({
        next: (config) => {
          if (!config.googleClientId) return;
          loadGoogleIdentity()
            .then((google) => {
              google.accounts.id.initialize({
                client_id: config.googleClientId!,
                callback: (response) => this.credential.emit(response.credential),
              });
              this.available.set(true);
              const host = this.slot().nativeElement;
              google.accounts.id.renderButton(host, {
                theme: 'outline',
                size: 'large',
                shape: 'rectangular',
                text: this.text(),
                locale: 'he',
                width: Math.min(host.clientWidth || 320, 400),
              });
            })
            .catch(() => this.available.set(false));
        },
      });
    });
  }
}
