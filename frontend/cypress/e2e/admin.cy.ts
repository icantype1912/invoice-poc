describe('Admin Page', () => {

  beforeEach(() => {

    cy.visit('/login')

    cy.get('input[name=email]').type('admin@invoice.com')
    cy.get('input[name=password]').type('Admin123!')

    // click the form login button, not navbar
    cy.get('button[type="submit"]').click()

    cy.location('pathname', { timeout: 10000 })
      .should('include', '/dashboard')

  })

  it('should display admin panel', () => {

    cy.visit('/admin')

    cy.contains('Pending Approvals').should('be.visible')
    cy.contains('All Users').should('be.visible')

  })

})