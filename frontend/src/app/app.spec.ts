import { TestBed } from '@angular/core/testing';
import { App } from './app';
import { provideRouter } from '@angular/router';
import { describe, it, expect, beforeEach } from 'vitest';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([]) // provides Router + ActivatedRoute for RouterOutlet & RouterLink
      ]
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;

    expect(app).toBeTruthy();
  });

  it('should have the correct title signal', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;

    expect(app['title']()).toBe('AvFrontEnd');
  });
});

