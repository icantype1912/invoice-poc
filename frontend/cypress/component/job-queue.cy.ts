import { JobQueue } from '../../src/app/features/job-queue/job-queue'
import { provideHttpClient } from '@angular/common/http'
import { ApiService } from '../../src/app/core/services/api.service'
import { Auth } from '../../src/app/core/services/auth'
import { of } from 'rxjs'

describe('JobQueue Component', () => {

  const mockJobs = {
    jobs: [
      {
        id: 'job12345678',
        jobType: 'PROCESS_INVOICE',
        status: 'COMPLETED',
        retryCount: 0,
        payloadJson: JSON.stringify({ uploader: 'vendor1' }),
        resultJson: JSON.stringify({ invoiceNumber: 'INV-001' }),
        createdAt: '2025-01-01T00:00:00Z',
        updatedAt: '2025-01-01T01:00:00Z'
      }
    ],
    total: 1
  }

  const apiStub = {
    getJobs: () => of(mockJobs),
    getJobById: () => of(mockJobs.jobs[0]),
    requeueJob: () => of({})
  }

  const authStub = {
    isAdmin: false
  }

  it('renders job queue page', () => {

    cy.mount(JobQueue, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('Job Queue')
    cy.contains('Background processing tasks')

  })

  it('loads jobs into table', () => {

    cy.mount(JobQueue, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('PROCESS_INVOICE')
    cy.contains('COMPLETED')

  })

  it('shows empty state when no jobs', () => {

    const emptyApi = {
      getJobs: () => of({ jobs: [], total: 0 })
    }

    cy.mount(JobQueue, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: emptyApi },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('No jobs found')

  })

  it('opens job details when row clicked', () => {

    cy.mount(JobQueue, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('PROCESS_INVOICE').click()

    cy.contains('Job Details')

  })

  it('opens raw JSON modal for completed jobs', () => {

    cy.mount(JobQueue, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('COMPLETED').click()

    cy.contains('Extracted Data')

  })

  it('pagination next page works', () => {

    const paginatedApi = {
      getJobs: () => of({
        jobs: mockJobs.jobs,
        total: 200
      })
    }

    cy.mount(JobQueue, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: paginatedApi },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('Next').click()

    cy.contains('Page 2')

  })

  it('pagination prev page works', () => {

    const paginatedApi = {
      getJobs: () => of({
        jobs: mockJobs.jobs,
        total: 200
      })
    }

    cy.mount(JobQueue, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: paginatedApi },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('Next').click()
    cy.contains('Prev').click()

    cy.contains('Page 1')

  })

  it('shows vendor selector for admin', () => {

    const adminAuth = { isAdmin: true }

    cy.intercept('GET', '**/admin/users', {
      statusCode: 200,
      body: [
        { id: 'vendor1', email: 'vendor@test.com', role: 1 }
      ]
    }).as('vendors')

    cy.mount(JobQueue, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: adminAuth }
      ]
    })

    cy.wait('@vendors')

    cy.contains('Vendor')

  })

  it('requeue button visible for failed jobs (admin)', () => {

    const failedApi = {
      getJobs: () => of({
        jobs: [{
          id: 'job1',
          jobType: 'PROCESS',
          status: 'FAILED',
          retryCount: 1,
          payloadJson: '{}',
          createdAt: '',
          updatedAt: ''
        }],
        total: 1
      }),
      requeueJob: () => of({})
    }

    const adminAuth = { isAdmin: true }

    cy.mount(JobQueue, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: failedApi },
        { provide: Auth, useValue: adminAuth }
      ]
    })

    cy.contains('Requeue')

  })

})