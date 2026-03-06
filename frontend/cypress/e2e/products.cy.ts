describe('Products Page', () => {

  beforeEach(() => {

    cy.visit('/login')

    cy.get('input[name=email]').type('admin@test.com')
    cy.get('input[name=password]').type('password')

    cy.contains('Login').click()

  })

  it('should display products table', () => {

    cy.visit('/products')

    cy.contains('Products')

    cy.get('table').should('exist')

  })

})