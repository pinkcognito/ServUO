using Xunit;

// Issue #38 fix: xUnit parallelizes test CLASSES by default (each class is
// its own collection unless grouped). The Tier2 harnesses drive real
// ServUO engine code (Mobile/BaseCreature/World) that relies on shared,
// non-thread-safe static state - e.g. AggressiveActionEventArgs.Create
// (Server/EventSink.cs) pools instances in a plain, unsynchronized
// Queue<T>. Two Tier2 test classes hitting DoHarmful concurrently can
// corrupt that queue, surfacing as sporadic NullReferenceExceptions or
// silently cross-wired Aggressor/Aggressed pairs between unrelated tests -
// exactly the flakiness that started appearing once this PR added more
// Tier2 combat-triggering tests (CompanionPresenceIntegrationTests)
// alongside the existing CompanionCombatIntegrationTests. The engine was
// never built for concurrent access (it's a single game-thread loop in
// production), so the fix belongs here, not in engine code: run the whole
// suite serially. 89 tests still complete in well under a second.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
