import { Admin } from '../../src/app/features/admin/admin'
import { provideHttpClient } from '@angular/common/http'
import { Auth } from '../../src/app/core/services/auth'

const mockPendingUsers = [
  { id: 'p1', email: 'pending@acme.com', companyName: 'ACME Corp', role: 1, status: 0 },
  { id: 'p2', email: 'pending2@beta.com', companyName: 'Beta Ltd', role: 1, status: 0 },
]

const mockUsers = [
  { id: 'u1', email: 'admin@example.com', companyName: 'HQ', role: 0, status: 1 },
  { id: 'u2', email: 'vendor@acme.com', companyName: 'ACME Corp', role: 1, status: 1 },
  { id: 'u3', email: 'locked@acme.com', companyName: 'ACME Corp', role: 1, status: 3 },
  { id: 'u4', email: 'rejected@acme.com', companyName: 'ACME Corp', role: 1, status: 2 },
]

const mockAuth = {
  getUserId: () => 'u1',
}

describe('Admin Component', () => {

  const mountAdmin = () => {
    cy.intercept('GET', '**/admin/users/pending', { statusCode: 200, body: mockPendingUsers }).as('getPending')
    cy.intercept('GET', '**/admin/users', { statusCode: 200, body: mockUsers }).as('getUsers')
    cy.mount(Admin, {
      providers: [
        provideHttpClient(),
        { provide: Auth, useValue: mockAuth }
      ]
    })
    cy.wait('@getPending')
    cy.wait('@getUsers')
  }

  // ── Rendering ─────────────────────────────────────────────────
  it('renders page sections', () => {
    mountAdmin()
    cy.contains('Pending Approvals')
    cy.contains('All Users')
    cy.contains('Refresh')
  })

  it('renders pending users', () => {
    mountAdmin()
    cy.contains('pending@acme.com')
    cy.contains('pending2@beta.com')
    cy.contains('ACME Corp')
    cy.contains('Beta Ltd')
  })

  it('renders all users', () => {
    mountAdmin()
    cy.contains('admin@example.com')
    cy.contains('vendor@acme.com')
    cy.contains('locked@acme.com')
  })

  it('shows empty state when no pending users', () => {
    cy.intercept('GET', '**/admin/users/pending', { statusCode: 200, body: [] }).as('getPending')
    cy.intercept('GET', '**/admin/users', { statusCode: 200, body: mockUsers }).as('getUsers')
    cy.mount(Admin, {
      providers: [
        provideHttpClient(),
        { provide: Auth, useValue: mockAuth }
      ]
    })
    cy.wait('@getPending')
    cy.contains('No pending users')
  })

  it('shows empty state when no users', () => {
    cy.intercept('GET', '**/admin/users/pending', { statusCode: 200, body: [] }).as('getPending')
    cy.intercept('GET', '**/admin/users', { statusCode: 200, body: [] }).as('getUsers')
    cy.mount(Admin, {
      providers: [
        provideHttpClient(),
        { provide: Auth, useValue: mockAuth }
      ]
    })
    cy.wait('@getUsers')
    cy.contains('No users found')
  })

  // ── Role & Status Labels ──────────────────────────────────────
  it('displays correct role labels', () => {
    mountAdmin()
    cy.contains('Admin')
    cy.contains('Vendor')
  })

  it('displays correct status labels', () => {
    mountAdmin()
    cy.contains('Pending')
    cy.contains('Approved')
    cy.contains('Rejected')
    cy.contains('Locked')
  })

  // ── Pending Actions ───────────────────────────────────────────
  it('approves a pending user and refreshes', () => {
    mountAdmin()
    cy.intercept('POST', '**/admin/users/p1/approve', { statusCode: 200, body: {} }).as('approve')
    cy.intercept('GET', '**/admin/users/pending', { statusCode: 200, body: [] }).as('refreshPending')
    cy.intercept('GET', '**/admin/users', { statusCode: 200, body: mockUsers }).as('refreshUsers')
    cy.contains('pending@acme.com').parents('.row').contains('Approve').click()
    cy.wait('@approve')
    cy.wait('@refreshPending')
  })

  it('rejects a pending user and refreshes', () => {
    mountAdmin()
    cy.intercept('POST', '**/admin/users/p1/reject', { statusCode: 200, body: {} }).as('reject')
    cy.intercept('GET', '**/admin/users/pending', { statusCode: 200, body: [] }).as('refreshPending')
    cy.intercept('GET', '**/admin/users', { statusCode: 200, body: mockUsers }).as('refreshUsers')
    cy.contains('pending@acme.com').parents('.row').contains('Reject').click()
    cy.wait('@reject')
    cy.wait('@refreshPending')
  })

  // ── All Users Actions ─────────────────────────────────────────
  it('promotes a vendor user', () => {
    mountAdmin()
    cy.intercept('POST', '**/admin/users/u2/promote', { statusCode: 200, body: {} }).as('promote')
    cy.intercept('GET', '**/admin/users/pending', { statusCode: 200, body: mockPendingUsers }).as('refreshPending')
    cy.intercept('GET', '**/admin/users', { statusCode: 200, body: mockUsers }).as('refreshUsers')
    cy.contains('vendor@acme.com').parents('.row').contains('Promote').click()
    cy.wait('@promote')
  })

  it('unlocks a locked user', () => {
    mountAdmin()
    cy.intercept('POST', '**/admin/users/u3/unlock', { statusCode: 200, body: {} }).as('unlock')
    cy.intercept('GET', '**/admin/users/pending', { statusCode: 200, body: mockPendingUsers }).as('refreshPending')
    cy.intercept('GET', '**/admin/users', { statusCode: 200, body: mockUsers }).as('refreshUsers')
    cy.contains('locked@acme.com').parents('.row').contains('Unlock').click()
    cy.wait('@unlock')
  })

  it('deletes a user', () => {
    mountAdmin()
    cy.intercept('DELETE', '**/admin/users/u2', { statusCode: 200, body: {} }).as('delete')
    cy.intercept('GET', '**/admin/users/pending', { statusCode: 200, body: mockPendingUsers }).as('refreshPending')
    cy.intercept('GET', '**/admin/users', { statusCode: 200, body: mockUsers }).as('refreshUsers')
    cy.contains('vendor@acme.com').parents('.row').contains('Delete').click()
    cy.wait('@delete')
  })

  // ── Current User Guards ───────────────────────────────────────
  it('does not show Promote or Delete for the current user', () => {
    mountAdmin()
    // u1 is the current user (getUserId returns 'u1')
    cy.contains('admin@example.com').parents('.row').within(() => {
      cy.contains('Promote').should('not.exist')
      cy.contains('Delete').should('not.exist')
    })
  })

  it('does not show Promote for admin role users', () => {
    mountAdmin()
    cy.contains('admin@example.com').parents('.row').within(() => {
      cy.contains('Promote').should('not.exist')
    })
  })

  it('only shows Unlock for locked users', () => {
    mountAdmin()
    cy.contains('locked@acme.com').parents('.row').within(() => {
      cy.contains('Unlock').should('exist')
    })
    cy.contains('vendor@acme.com').parents('.row').within(() => {
      cy.contains('Unlock').should('not.exist')
    })
  })

  // ── Refresh ───────────────────────────────────────────────────
  it('calls refresh on button click', () => {
    mountAdmin()
    cy.intercept('GET', '**/admin/users/pending', { statusCode: 200, body: mockPendingUsers }).as('refreshPending')
    cy.intercept('GET', '**/admin/users', { statusCode: 200, body: mockUsers }).as('refreshUsers')
    cy.contains('Refresh').click()
    cy.wait('@refreshPending')
    cy.wait('@refreshUsers')
  })
})