import { Upload } from '../../src/app/features/upload/upload'
import { provideHttpClient } from '@angular/common/http'

describe('Upload Component', () => {

  it('renders upload UI', () => {

    cy.mount(Upload, {
      providers: [provideHttpClient()]
    })

    cy.contains('Upload Documents')
    cy.contains('Drag & drop your documents here')
    cy.contains('Browse Files')

  })


  it('adds file when selected', () => {

    cy.mount(Upload, {
      providers: [provideHttpClient()]
    })

    cy.get('input[type=file]')
      .selectFile({
        contents: Cypress.Buffer.from('test'),
        fileName: 'invoice.pdf',
        mimeType: 'application/pdf'
      }, { force: true })

    cy.contains('invoice.pdf')

  })


  it('shows success after upload', () => {

    cy.intercept('POST', '**/VendorInvoices/upload', {
      statusCode: 200,
      body: { success: true }
    }).as('upload')

    cy.mount(Upload, {
      providers: [provideHttpClient()]
    })

    cy.get('input[type=file]').selectFile({
      contents: Cypress.Buffer.from('file'),
      fileName: 'invoice.pdf',
      mimeType: 'application/pdf'
    }, { force: true })

    cy.wait('@upload')

    cy.contains('✓ Uploaded')

  })


  it('shows rejection reason', () => {

    cy.intercept('POST', '**/VendorInvoices/upload', {
      statusCode: 422,
      body: {
        securityReason: 'Virus detected'
      }
    }).as('upload')

    cy.mount(Upload, {
      providers: [provideHttpClient()]
    })

    cy.get('input[type=file]').selectFile({
      contents: Cypress.Buffer.from('badfile'),
      fileName: 'virus.pdf',
      mimeType: 'application/pdf'
    }, { force: true })

    cy.wait('@upload')

    cy.contains('✗ Rejected')
    cy.contains('Virus detected')

  })


  it('handles drag and drop', () => {

    cy.mount(Upload, {
      providers: [provideHttpClient()]
    })

    const file = new File(['test'], 'drag.pdf', { type: 'application/pdf' })

    cy.get('.upload-card').trigger('drop', {
      dataTransfer: {
        files: [file]
      }
    })

    cy.contains('drag.pdf')

  })


  it('shows uploading state', () => {

    cy.intercept('POST', '**/VendorInvoices/upload', {
      delay: 1000,
      statusCode: 200,
      body: { success: true }
    }).as('upload')

    cy.mount(Upload, {
      providers: [provideHttpClient()]
    })

    cy.get('input[type=file]').selectFile({
      contents: Cypress.Buffer.from('test'),
      fileName: 'progress.pdf',
      mimeType: 'application/pdf'
    }, { force: true })

    cy.contains('uploading')

  })

})