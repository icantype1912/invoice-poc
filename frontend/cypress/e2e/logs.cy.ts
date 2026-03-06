describe('Logs Page', () => {

  beforeEach(() => {

    cy.visit('/login')

    // correct credentials
    cy.get('input[name=email]').type('admin@invoice.com')
    cy.get('input[name=password]').type('Admin123!')

    // click the form login button
    cy.get('button[type="submit"]').click()

    // wait for redirect after login
    cy.location('pathname', { timeout: 10000 })
      .should('include', '/dashboard')

  })

  it('should display logs table', () => {

    cy.visit('/logs')

    // confirm correct page
    cy.location('pathname').should('eq', '/logs')

    cy.contains('File Change Logs').should('be.visible')

    cy.get('table').should('be.visible')

  })

})