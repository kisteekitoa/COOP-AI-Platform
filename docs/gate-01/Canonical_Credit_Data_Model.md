# Gate 01 — Canonical Credit Data Model

Define the canonical business entities for COOP-AI Version 1.

This document is the foundation for database design, APIs, business rules, validation rules, and application development.

## 1. Scope

This Gate covers only Version 1 modules:

- Member
- Savings
- Financing
- Accounting
- User Management
- Reporting

## 2. Canonical Business Entities

| Entity | Description | Primary Owner |
|---|---|---|
| Member | Represents a cooperative member or customer. | Member Services |
| SavingsAccount | Represents a member's savings account. | Savings Department |
| FinancingApplication | Captures a member's financing request and supporting information. | Financing Department |
| FinancingContract | Represents the approved financing agreement. | Financing Department |
| Installment | Represents a scheduled repayment installment under a financing contract. | Financing Department |
| Transaction | Represents an operational financial transaction such as deposit, withdrawal, payment, or disbursement. | Finance Department |
| JournalEntry | Represents an accounting posting generated from a transaction. | Accounting Department |
| Account | Represents a ledger or financial account used for accounting purposes. | Accounting Department |
| User | Represents a system user with assigned roles and permissions. | Administration |
| Branch | Represents a cooperative branch or operational location. | Operations |
| Guarantor | Represents a member acting as guarantor for financing. | Financing Department |
| FinancingProduct | Represents a financing product or financing scheme offered by the cooperative. | Financing Department |
| Approval | Represents an approval record for financing workflow. | Financing Department |
| Payment | Represents an actual payment received from a member. | Finance Department |

## 3. Entity Relationships

The relationships between the canonical entities are described in business language as follows:

- One Member can own many SavingsAccounts.
- One Member can submit many FinancingApplications.
- One FinancingApplication may result in one FinancingContract.
- One FinancingContract generates many Installments.
- One Transaction may create one or many JournalEntries.
- One Account may receive many JournalEntries.
- One User belongs to one Branch or may operate across multiple branches depending on organizational design.
- One Branch may host many Members and Users.
- One FinancingProduct can be used by many FinancingContracts.
- One FinancingContract can have many Payments.
- One Installment may be settled by one or many Payments.
- One Member may act as Guarantor for many FinancingContracts.
- One FinancingApplication can have many Approval records.
- One Branch owns many SavingsAccounts.
- One Branch owns many FinancingContracts.

## 4. Core Business Keys

| Entity | Business Key | Example | Description |
|---|---|---|---|
| Member | Member Number | M-10001 | Unique business identifier for a member. |
| SavingsAccount | Savings Account Number | SA-90001 | Unique identifier for a savings account. |
| FinancingApplication | Application Reference | FA-20001 | Unique reference for a financing request. |
| FinancingContract | Contract Number | FC-30001 | Unique identifier for an approved financing contract. |
| Installment | Installment Number | INST-40001 | Unique identifier for a repayment installment. |
| Transaction | Transaction Reference | TXN-50001 | Unique reference for a business transaction. |
| JournalEntry | Journal Entry Number | JE-60001 | Unique identifier for an accounting entry. |
| Account | Account Code | ACC-70001 | Unique business code for a financial account. |
| User | User ID | USER-80001 | Unique identifier for a system user. |
| Branch | Branch Code | BR-90001 | Unique identifier for a branch. |

## 5. Lifecycle

### Member
A member begins as a prospective person or customer and moves through onboarding, verification, account activation, service usage, and eventual status changes such as inactive or withdrawn.

### Financing Application
A financing application progresses through the following lifecycle: Draft → Submitted → Under Review → Verified → Approved → Rejected → Cancelled.

### Financing Contract
Once approved, a financing application becomes a financing contract that progresses through the lifecycle: Approved → Disbursed → Active → Completed → Closed.

### Savings Account
A savings account is opened and progresses through the lifecycle: Draft → Active → Dormant → Closed.

## 6. Business Ownership

Business ownership should be assigned by domain to support accountability and governance:

- Member data is owned by Member Services.
- Savings data is owned by the Savings Department.
- Financing data is owned by the Financing Department.
- Accounting data is owned by the Accounting Department.
- User and access data is owned by Administration.
- Branch and operational data is owned by Operations.

## 7. Design Principles

The canonical model should follow these principles:

- Single Source of Truth
- No Duplicate Member
- Immutable Financial Transactions
- Auditability
- Sharia Compliance
- Business Keys are immutable.
- Business identity must be separated from database identity.
- Referential integrity must be maintained across all canonical entities.
- Every financial transaction must be fully auditable.

## Canonical Model Principles

The canonical model should be defined using business language first and should remain technology independent. It should act as a single source of truth for the cooperative domain, remain reusable across modules, remain stable over time, and support future integrations without requiring major redefinition.

Version 1.0