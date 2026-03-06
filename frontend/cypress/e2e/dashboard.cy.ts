describe('Dashboard Page', () => {

  beforeEach(() => {

    cy.visit('/login')

    cy.get('input[name=email]').type('admin@invoice.com')
    cy.get('input[name=password]').type('Admin123!')

    cy.get('button[type="submit"]').click()

  })

  it('should display dashboard analytics', () => {

    cy.url().should('include', '/dashboard')

    cy.contains('Analytics Dashboard')
    cy.contains('Total Revenue')
    cy.contains('Total Invoices')

  })

})