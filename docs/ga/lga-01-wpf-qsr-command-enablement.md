# LGA-01 WPF QSR Command Enablement

## Issue

In the WPF QSR screen, after adding Americano 12oz and entering enough tendered cash, the buttons stayed disabled:

- Cash payment
- Fake receipt queue
- Visual sync update

## Root cause

The command `CanExecute` state was not refreshed after changes to quantity, tendered cash, total, and change.

## Fix

`SalesViewModel` now exposes QSR commands as `RelayCommand` and calls `RaiseCanExecuteChanged` through `RefreshCommandStates` after cart/payment state changes.

## Expected behavior

After adding a product and setting tendered cash greater than or equal to total, the cash payment command becomes enabled. Receipt and visual sync commands are also refreshed as the state changes.
