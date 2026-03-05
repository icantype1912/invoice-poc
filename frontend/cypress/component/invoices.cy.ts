import { Invoices } from '../../src/app/features/invoices/invoices'
import { provideHttpClient } from '@angular/common/http'
import { ApiService } from '../../src/app/core/services/api.service'
import { Auth } from '../../src/app/core/services/auth'
import { of } from 'rxjs'

describe('Invoices Component', () => {

  const mockInvoices = {
    invoices: [
      {
        id: 'inv1',
        invoiceNumber: 'INV-001',
        invoiceDate: '2025-01-01',
        totalAmount: 100,
        currency: 'USD',
        shippingCost: 5,
        discount: { amount: 10, percentage: 10 },
        lineItems: [
          {
            id: 'line1',
            productName: 'Chair',
            quantity: 2,
            unitRate: 50,
            amount: 100,
            category: 'Furniture'
          }
        ]
      }
    ],
    total: 1,
    totalPages: 1
  }

  const mockInvalid = {
    data: [
      {
        id: 'invalid1',
        fileName: 'bad-invoice.pdf',
        type: 'ExtractionFailure',
        reason: 'OCR failed',
        createdAt: '2025-01-01T00:00:00Z',
        jobId: 'job123'
      }
    ],
    totalCount: 1,
    totalPages: 1
  }

  const apiStub = {
    getInvoices: () => of(mockInvoices),
    getInvalidInvoices: () => of(mockInvalid),
    requeueInvalidInvoice: () => of({})
  }

  const authStub = {
    isAdmin: false
  }

  it('renders invoices page', () => {

    cy.mount(Invoices, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('Invoices')
    cy.contains('Valid Invoices')

  })

  it('loads valid invoices', () => {

    cy.mount(Invoices, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('INV-001')

  })

  it('expands invoice to show line items', () => {

    cy.mount(Invoices, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('INV-001').click()

    cy.contains('Line Items')
    cy.contains('Chair')

  })

  it('pagination next page works', () => {

    const paginatedApi = {
      getInvoices: () => of({
        invoices: mockInvoices.invoices,
        total: 200,
        totalPages: 10
      }),
      getInvalidInvoices: () => of(mockInvalid)
    }

    cy.mount(Invoices, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: paginatedApi },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('Next').click()

    cy.contains('Page 2')

  })

  it('opens invalid invoices section', () => {

    cy.mount(Invoices, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('Invalid Invoices').click()

    cy.contains('bad-invoice.pdf')

  })

  it('requeues invalid invoice', () => {

    const adminAuth = { isAdmin: true }

    cy.mount(Invoices, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: adminAuth }
      ]
    })

    cy.contains('Invalid Invoices').click()

    cy.contains('Requeue')

  })

  it('shows vendor filter for admin', () => {

    const adminAuth = { isAdmin: true }

    cy.intercept('GET', '**/admin/users', {
      statusCode: 200,
      body: [
        { id: 'vendor1', email: 'vendor@test.com', role: 1 }
      ]
    }).as('vendors')

    cy.mount(Invoices, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiStub },
        { provide: Auth, useValue: adminAuth }
      ]
    })

    cy.wait('@vendors')

    cy.contains('Vendor')

  })

  it('shows empty state when no invoices exist', () => {

    const emptyApi = {
      getInvoices: () => of({
        invoices: [],
        total: 0,
        totalPages: 1
      }),
      getInvalidInvoices: () => of({
        data: [],
        totalCount: 0,
        totalPages: 1
      })
    }

    cy.mount(Invoices, {
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: emptyApi },
        { provide: Auth, useValue: authStub }
      ]
    })

    cy.contains('No valid invoices found')

  })

})