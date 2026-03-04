import { TestBed } from '@angular/core/testing';
import { Theme } from './theme';
import { DOCUMENT } from '@angular/common';
import { describe, it, expect, beforeEach } from 'vitest';

describe('Theme', () => {
  let service: Theme;
  let documentMock: Document;

  beforeEach(() => {
    documentMock = document;

    TestBed.configureTestingModule({
      providers: [
        Theme,
        { provide: DOCUMENT, useValue: documentMock },
      ],
    });

    service = TestBed.inject(Theme);
  });

  it('should create', () => {
    expect(service).toBeTruthy();
  });

  it('should initialize with dark theme', () => {
    expect(service.isDark()).toBe(true);
    expect(document.body.getAttribute('data-theme')).toBe('dark');
  });

  it('should toggle theme', () => {
    const initial = service.isDark();

    service.toggle();

    expect(service.isDark()).toBe(!initial);
  });

  it('should set dark theme', () => {
    service.setDark(true);

    expect(service.isDark()).toBe(true);
    expect(document.body.getAttribute('data-theme')).toBe('dark');
  });

  it('should set light theme', () => {
    service.setDark(false);

    expect(service.isDark()).toBe(false);
    expect(document.body.getAttribute('data-theme')).toBe('light');
  });
});
