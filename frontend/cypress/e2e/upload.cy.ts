describe('Upload Page', () => {

  beforeEach(() => {

    cy.visit('/login')

    // correct credentials
    cy.get('input[name=email]').type('adithya@gmail.com')
    cy.get('input[name=password]').type('Qwerty@123')

    // click the login form button
    cy.get('button[type="submit"]').click()

    // wait for redirect after login
    cy.location('pathname', { timeout: 10000 })
      .should('include', '/dashboard')

  })

  it('should open upload page', () => {

    cy.visit('/upload')

    // confirm correct page
    cy.location('pathname').should('eq', '/upload')

    cy.contains('Upload Documents').should('be.visible')

    cy.contains('Browse Files').should('be.visible')

  })

})