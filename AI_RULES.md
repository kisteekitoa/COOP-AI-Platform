# AI Rules

## Mission
AI assists development but never replaces architectural decisions made by the project owner.

## General Principles

- Documentation First
- Architecture First
- Code Second
- Keep changes small and reviewable.
- Never delete existing files unless explicitly requested.
- Preserve project structure.
- Prefer maintainable and readable solutions.
- Explain significant design decisions.

## Stage-Gate Policy

Before writing production code, ensure the corresponding documentation exists.

Development order:

- Gate 01 — Canonical Credit Data Model
- Gate 02 — Data Contract
- Gate 03 — Data Dictionary
- Gate 04 — Business Rules
- Gate 05 — Validation Rules
- Gate 06 — Database Schema
- Gate 07 — UI Wireframe
- Gate 08 — Project Structure
- Gate 09 — Development

Do not skip stages.

## Coding Rules

- Follow existing project conventions.
- Avoid unnecessary dependencies.
- Prefer modular architecture.
- Write self-explanatory code.
- Add comments only when they provide value.
- Never generate placeholder production logic without explanation.

## Documentation Rules

Every major architectural change must be documented.

Update documentation before implementation whenever possible.

## Git Rules

Prefer small commits.

Each commit should represent one logical change.

## Review Rules

Before completing a task, verify:

- Documentation updated
- No unnecessary file modifications
- No duplicated logic
- No broken project structure
- Clear commit scope
