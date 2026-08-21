# EXP-11 Catalog Pricing Rollback

## Rollback objective

Rollback for catalog pricing must preserve evidence and avoid destructive production changes.

## Soft delete rule

Use soft delete or archive status for rollback when supported. Do not hard-delete production catalog rows.

## Do not hard-delete

Operators must not hard-delete categories, products, variants, barcodes, price records, modifier groups, or modifiers during normal operations.

## Safe rollback options

- mark product inactive or archived
- expire a product price with a future-safe replacement
- soft delete a controlled validation entity through admin endpoint
- create a corrective price record instead of mutating historical sales

## Audit requirement

Every rollback action must create audit evidence and sync replay evidence.
