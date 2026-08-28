# Post-LGA Capacity Remediation Plan

## Step 1 - readiness query optimization

Reduce `/health/ready` database work from 12 SQL commands per request to one catalog query. Preserve the complete required-table check.

## Step 2 - build and regression tests

Run restore, build, test and secret scan before deployment. The code must compile with zero errors and all existing suites must remain green.

## Step 3 - deploy to Railway

Deploy the updated PosServer image through the existing Railway Dockerfile pipeline. Do not change Public GA flags.

## Step 4 - strict capacity gate

Measure both `/health/live` and `/health/ready` with concurrency 3 and 6 requests. Both endpoints must return all successful responses and p95 <= 1200 ms.

## Step 5 - PostgreSQL pressure

Validate waiting connections <= 12, no long-running queries, RLS intact, sync queues stable, and commercial operations still healthy.

## Step 6 - infrastructure escalation only if needed

If the capacity gate still fails after the readiness query optimization, scale Railway resources and review PostgreSQL connection pool pressure. Re-run the same validator without changing thresholds.

A capacity gate PASS authorizes a Public GA readiness review only. It does not activate Public GA.
