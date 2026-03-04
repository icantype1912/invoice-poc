import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Upload } from './upload';
import { HttpClient, HttpEventType } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { describe, it, expect, beforeEach, vi } from 'vitest';

class MockHttpClient {
  post = vi.fn();
}

describe('Upload', () => {
  let component: Upload;
  let fixture: ComponentFixture<Upload>;
  let http: MockHttpClient;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Upload],
      providers: [{ provide: HttpClient, useClass: MockHttpClient }],
    }).compileComponents();

    fixture = TestBed.createComponent(Upload);
    component = fixture.componentInstance;

    http = TestBed.inject(HttpClient) as unknown as MockHttpClient;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should handle drag over', () => {
    const event = {
      preventDefault: vi.fn()
    } as unknown as DragEvent;

    component.onDragOver(event);

    expect(component.isDragOver).toBe(true);
  });

  it('should handle drag leave', () => {
    const event = {
      preventDefault: vi.fn()
    } as unknown as DragEvent;

    component.onDragLeave(event);

    expect(component.isDragOver).toBe(false);
  });


  it('should add file on drop', () => {
    const file = new File(['test'], 'test.pdf');

    http.post.mockReturnValue(of({ type: HttpEventType.Response, body: { success: true } }));

    const event = {
      preventDefault: () => {},
      dataTransfer: { files: [file] },
    } as unknown as DragEvent;

    component.onDrop(event);

    expect(component.files.length).toBe(1);
  });

  it('should update progress on upload progress event', () => {
    const file = new File(['test'], 'file.txt');

    http.post.mockReturnValue(
      of({
        type: HttpEventType.UploadProgress,
        loaded: 50,
        total: 100,
      })
    );

    component['addFile'](file);

    const item = component.files[0];

    expect(item.progress()).toBe(50);
  });

  it('should mark upload as done on successful response', () => {
    const file = new File(['test'], 'file.txt');

    http.post.mockReturnValue(
      of({
        type: HttpEventType.Response,
        body: { success: true },
      })
    );

    component['addFile'](file);

    const item = component.files[0];

    expect(item.status()).toBe('done');
  });

  it('should reject file if backend reports security reason', () => {
    const file = new File(['test'], 'file.txt');

    http.post.mockReturnValue(
      of({
        type: HttpEventType.Response,
        body: { success: false, securityReason: 'Malicious content' },
      })
    );

    component['addFile'](file);

    const item = component.files[0];

    expect(item.status()).toBe('rejected');
    expect(item.rejectReason()).toBe('Malicious content');
  });

  it('should reject file on HTTP 422 error', () => {
    const file = new File(['test'], 'file.txt');

    http.post.mockReturnValue(
      throwError(() => ({
        status: 422,
        error: { securityReason: 'Virus detected' },
      }))
    );

    component['addFile'](file);

    const item = component.files[0];

    expect(item.status()).toBe('rejected');
    expect(item.rejectReason()).toBe('Virus detected');
  });

  it('should reject file on generic upload error', () => {
    const file = new File(['test'], 'file.txt');

    http.post.mockReturnValue(
      throwError(() => ({
        status: 500,
      }))
    );

    component['addFile'](file);

    const item = component.files[0];

    expect(item.status()).toBe('rejected');
    expect(item.rejectReason()).toBe('Upload failed');
  });
});