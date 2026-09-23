import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ImageUploadApi } from './image-upload-api';

describe('ImageUploadApi', () => {
  it('sends the selected file as multipart form data', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const api = TestBed.inject(ImageUploadApi);
    const http = TestBed.inject(HttpTestingController);
    const file = new File(['image'], 'name-is-not-trusted.png', { type: 'image/png' });

    api.upload(file).subscribe();
    const request = http.expectOne('/api/manage/images');

    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeInstanceOf(FormData);
    expect((request.request.body as FormData).get('file')).toBe(file);
    expect(request.request.headers.has('Content-Type')).toBe(false);
    request.flush({
      id: 'image-id',
      url: '/api/images/image-id',
      contentType: 'image/webp',
      sizeBytes: 100,
      width: 100,
      height: 100,
    });
    http.verify();
  });
});
