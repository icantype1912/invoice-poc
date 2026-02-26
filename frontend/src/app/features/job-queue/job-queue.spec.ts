import { ComponentFixture, TestBed } from '@angular/core/testing';

import { JobQueue } from './job-queue';

describe('JobQueue', () => {
  let component: JobQueue;
  let fixture: ComponentFixture<JobQueue>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [JobQueue]
    })
    .compileComponents();

    fixture = TestBed.createComponent(JobQueue);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
