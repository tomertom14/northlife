import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EventImage } from './event-image';

@Component({
  imports: [EventImage],
  template: `<img [appEventImage]="url()" category="Outdoors" alt="" />`,
})
class Host {
  readonly url = signal<string | null>('/api/images/123');
}

describe('EventImage', () => {
  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const img: HTMLImageElement = fixture.nativeElement.querySelector('img');
    return { fixture, img };
  }

  it('shows the uploaded image first', () => {
    const { img } = render();
    expect(img.getAttribute('src')).toBe('/api/images/123');
  });

  it('falls back to the category illustration when the image cannot load', () => {
    const { fixture, img } = render();
    img.dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(img.getAttribute('src')).toBe('/images/events/outdoors.svg');
  });

  it('uses the illustration when the event has no image', () => {
    const { fixture, img } = render();
    fixture.componentInstance.url.set(null);
    fixture.detectChanges();
    expect(img.getAttribute('src')).toBe('/images/events/outdoors.svg');
  });
});
