import { Logs } from '../../src/app/features/logs-component/logs-component'
import { provideHttpClient } from '@angular/common/http'
import { ApiService } from '../../src/app/core/services/api.service'
import { Auth } from '../../src/app/core/services/auth'
import { of } from 'rxjs'

describe('Logs Component', () => {

  const mockLogsResponse = {
    logs: [
      {
        id: 1,
        fileName: 'invoice.pdf',
        fileSize: 1024,
        securityStatus: 'Healthy',
        securityFailReason: null,
        fileId: 'file123',
        changeType: 'Created',
        detectedAt: '2025-01-01T00:00:00Z',
        mimeType: 'application/pdf',
        processed: true,
        processedAt: '2025-01-01T01:00:00Z',
        modifiedBy: 'user@test.com',
        googleDriveModifiedTime: '2025-01-01T00:00:00Z',
        securityCheckedAt: '2025-01-01T00:05:00Z',
        uploadedByVendorId: 'vendor1'
      }
    ],
    total: 1,
    totalPages: 1
  }

  const apiStub = {
    getLogs: () => of(mockLogsResponse)
  }

  const authStub = {
    isAdmin: () => false
  }

  it('renders logs page', () => {

    cy.mount(Logs, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('File Change Logs')
    cy.contains('Track file activities')

  })


  it('loads and displays logs', () => {

    cy.mount(Logs, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('invoice.pdf')
    cy.contains('Created')

  })


  it('shows empty state when no logs exist', () => {

    const emptyApiStub = {
      getLogs: () => of({
        logs: [],
        total: 0,
        totalPages: 1
      })
    }

    cy.mount(Logs, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: emptyApiStub },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('No logs found')

  })


  it('pagination next page works', () => {

    const paginatedApi = {
      getLogs: () => of({
        logs: mockLogsResponse.logs,
        total: 100,
        totalPages: 5
      })
    }

    cy.mount(Logs, {
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
      getLogs: () => of({
        logs: mockLogsResponse.logs,
        total: 100,
        totalPages: 5
      })
    }

    cy.mount(Logs, {
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

    const adminAuth = {
      isAdmin: () => true
    }

    cy.intercept('GET', '**/admin/users', {
      statusCode: 200,
      body: [
        {
          id: '1',
          email: 'vendor@test.com',
          companyName: 'Vendor Co',
          role: 1
        }
      ]
    }).as('vendors')

    cy.mount(Logs, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: adminAuth }
      ]
    })

    cy.wait('@vendors')

    cy.contains('Vendor')

  })


  it('changes vendor filter', () => {

    const adminAuth = {
      isAdmin: () => true
    }

    cy.intercept('GET', '**/admin/users', {
      statusCode: 200,
      body: [
        {
          id: '1',
          email: 'vendor@test.com',
          companyName: 'Vendor Co',
          role: 1
        }
      ]
    }).as('vendors')

    cy.mount(Logs, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: adminAuth }
      ]
    })

    cy.wait('@vendors')

    cy.get('.vendor-select').select('Vendor Co')

  })

})