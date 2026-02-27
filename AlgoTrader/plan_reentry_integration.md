# Implementation Plan - Advanced Re-entry Logic Integration (COMPLETED)

This plan outlines the steps to integrate the `AdvancedReEntryManager` into the `StrategyEngine` and `StrategyInstance` to support automated re-entries for individual legs and combined strategies.

## 1. Infrastructure & Dependency Injection
- [x] **DI Registration**: Register `AdvancedReEntryManager` as a singleton in `AlgoTrader.UI/Program.cs`.
- [x] **StrategyEngine Update**: 
    - Accept `AdvancedReEntryManager` in the constructor.
    - Update `RegisterStrategyAsync` to pass the manager instance to `StrategyInstance`.

## 2. StrategyInstance Enhancement
- [x] **Constructor Update**: Modify `StrategyInstance` to accept and store `AdvancedReEntryManager`.
- [x] **Combined Re-entry**:
    - In `OnTick`, after a strategy exit is confirmed, call `_reEntryManager.ShouldCombinedReEnter(_config)`.
    - If re-entry is directed, use `_reEntryManager.ProcessCombinedReEntry(_config)` to get the setup for the next run.
    - Handle state transition back to `WAITING_ENTRY` or trigger immediate re-entry.

## 3. Leg-Level Re-entry
- [x] **Leg Exit Detection**:
    - Subcribe to `ExecutionEngine.ExecutionUpdates` within `StrategyInstance`.
    - Identify fills that corresponding to leg exits (SL/Target).
- [x] **Leg Re-entry Logic**:
    - When a leg exit is detected, construct a `LegPosition` representing the exit.
    - Call `_reEntryManager.ShouldLegReEnter`.
    - If triggered, use `_reEntryManager.ProcessLegReEntry` to get the new leg configuration.
    - Dispatch the re-entry order via `ExecutionEngine`.

## 4. UI Integration
- [x] **Re-entry Panel**: Added "Advanced Re-entry" navigation and panel to `StrategyBuilderForm`.
- [x] **Leg Grid Columns**: Added Leg-level re-entry type, max entries, and time window columns.
- [x] **Persistence**: Updated `SaveStrategyAsync` to read and save re-entry configurations.

## 5. Verification & Testing
- [x] **Logging**: Re-entry events are logged with counts and details.
- [x] **Max Limits**: Limits are checked via `AdvancedReEntryManager` state.
- [x] **Reversal**: Direction flips correctly for "Reverse" scenarios.
