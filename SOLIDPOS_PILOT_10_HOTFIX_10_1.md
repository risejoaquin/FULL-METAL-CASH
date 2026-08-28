# SolidPOS PILOT-10 HOTFIX 10.1 — Expansion Decision Term Contract

## Status

PENDING USER VALIDATION

## Scope

Fixes a false negative in PILOT-10 document validation.

## Problem

The production expansion decision document used expansion-oriented wording, but the validator required the exact literal token `expand`.

## Change

The validator now accepts equivalent production expansion terms:

- expand
- expansion
- expansion type
- limited production expansion
- controlled expansion

The decision document was also made explicit with the sentence that the operator may expand only under the listed controls.

## Runtime impact

No backend changes. No database changes. No production mutations.

## Expected result

PILOT-10 should pass the document contract step and continue to production health, monitoring, SQL cross-check, and expansion decision matrix validation.
