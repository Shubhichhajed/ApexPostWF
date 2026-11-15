# Copilot Test Generation Log

GitHub Copilot (Chat, in Visual Studio 2026) was used to **draft** two pieces of test code. Nothing was committed as generated: every draft was reviewed against the rules in TESTING-STRATEGY.md (AAA, naming, real expected values, mock setups that match the production code, edge cases) and then corrected. Classes that started from a Copilot draft carry the comment `// Copilot-assisted: ...`.

| # | Target | Final file(s) |
|---|---|---|
| 1 | Unit tests for `BranchAdministrationService` (Moq) | `tests/ApexPost.UnitTests/Application/BranchAdministrationServiceTests.cs` |
| 2 | Gherkin feature + step definitions for the shipment status lifecycle | `tests/ApexPost.Specs/Features/ShipmentStatusLifecycle.feature`, `tests/ApexPost.Specs/StepDefinitions/ShipmentStatusSteps.cs` |

---

## Prompt 1: service unit tests

**Prompt (Copilot Chat, with `BranchAdministrationService.cs`, `IBranchRepository` and `IUnitOfWork` attached as context):**

```text
Write xUnit tests for BranchAdministrationService using Moq.
Mock IBranchRepository and IUnitOfWork. Cover CreateAsync and UpdateAsync:
new branch, duplicate branch code, unknown id on update, and a normal update.
Use the Arrange/Act/Assert pattern and Method_Scenario_ExpectedResult names.
Verify SaveChangesAsync is called exactly once on success and never on failure.
```

**What Copilot produced:**

<!-- Paste the generated test class (or the relevant excerpt) here. -->

**Review decisions:**

| Draft item | Decision | Why |
|---|---|---|
| | Kept / Changed / Rejected | |
| | | |
| | | |

Points checked during review (and the shape of the final version):

- **Weak assertions:** the final tests check *what* was staged with `It.Is<Branch>(b => b.Code == "IND09" && b.Name == "Indore West" ...)`, not just that `AddAsync` was called with `It.IsAny`.
- **Wrong expected values:** the input is deliberately padded and lower-case (`" ind09 "`), and the expected code is `"IND09"`. A draft that passes `"IND09"` in and expects `"IND09"` out would not notice if normalisation were removed.
- **Mock setup that does not match the code:** `GetByCodeAsync` is set up with the *normalised* code, and the mocks are `MockBehavior.Strict`, so a call the service is not supposed to make fails the test instead of silently returning `null`.
- **Missing edge cases:** the duplicate-code test verifies `AddAsync` *and* `SaveChangesAsync` were never called; the update test checks the branch code is **not** changed by an edit (it is the business key) and that the counter keeps its ID.
- **Copying the implementation:** no expected value is computed with the same `Trim().ToUpperInvariant()` the service uses; expected values are literals.

---

## Prompt 2: Gherkin feature and step definitions

**Prompt (Copilot Chat, with `ShipmentRules.cs` attached):**

```text
Write a Reqnroll feature file in business language for the shipment status lifecycle:
Created -> PickedUp -> InTransit -> OutForDelivery -> Delivered or Failed.
Include a Background and a Scenario Outline with an Examples table of allowed and refused moves,
and one scenario showing that a Delivered shipment cannot go back to InTransit.
Then write the C# step definitions using ShipmentStatusRules.Apply. Share state between steps
with a context class injected through the constructor, not static fields.
```

**What Copilot produced:**

<!-- Paste the generated feature and step definitions here. -->

**Review decisions:**

| Draft item | Decision | Why |
|---|---|---|
| | Kept / Changed / Rejected | |
| | | |

Points checked during review (and the shape of the final version):

- **Business language:** steps say "a shipment that is Delivered / it is moved to InTransit / the move is refused", not "call Apply with ..." or "an exception is thrown".
- **State sharing:** `StatusScenarioState` is injected through the constructor; no static fields.
- **Assertion on the real outcome:** the "refused" step asserts that a `ShipmentConflictException` was raised *and* that no new status was produced; the message scenario compares the exact text the API returns in its 409.
- **Coverage of the risky cases:** the Examples include skipping a step (Created → Delivered), going backwards (InTransit → PickedUp) and reopening a final status (Failed → OutForDelivery), not only the happy path.

---

## Proof that the generated tests can fail

Each check below was done by deliberately breaking the production code, running the tests, recording the result, and then restoring the code with `git checkout -- <file>`.

### Break 1: drop code normalisation in `BranchAdministrationService.CreateAsync`

```diff
- var code = command.Code.Trim().ToUpperInvariant();
+ var code = command.Code.Trim();
```

```powershell
dotnet test tests/ApexPost.UnitTests --filter "FullyQualifiedName~BranchAdministrationServiceTests"
```

Expected: `CreateAsync_NewCode_StagesNormalisedBranchAndCommitsOnce` **fails**. The strict `IBranchRepository` mock receives `GetByCodeAsync("ind09", ...)`, which has no setup, so Moq throws `MockException`. Without normalisation, a second branch with the same code in different case would be created.

Observed:

```text
Failed ApexPost.UnitTests.Application.BranchAdministrationServiceTests.CreateAsync_NewCode_StagesNormalisedBranchAndCommitsOnce [5 ms]
  Moq.MockException : IBranchRepository.GetByCodeAsync("ind09", CancellationToken) invocation failed with mock behavior Strict.
```


### Break 2: remove the duplicate check

```diff
- if (await branches.GetByCodeAsync(code, cancellationToken) is not null)
-     throw new DomainException("Branch code already exists.");
```

Expected: `CreateAsync_DuplicateCode_ThrowsDomainExceptionAndNeverCommits` **fails**. The service now goes on to call `AddAsync`, which the strict mock has no setup for, so a `MockException` is thrown instead of the expected `DomainException` ("Assert.Throws() Failure: Exception type was not an exact match").

Observed:

```text
Failed ApexPost.UnitTests.Application.BranchAdministrationServiceTests.CreateAsync_DuplicateCode_ThrowsDomainExceptionAndNeverCommits [8 ms]
  Assert.Throws() Failure: Exception type was not an exact match
```


### Break 3: allow a delivered shipment back onto the road

In `ShipmentStatusRules`, add `["Delivered"] = ["InTransit"]` to the transition table.

```powershell
dotnet test tests/ApexPost.Specs --filter "FullyQualifiedName~ShipmentStatusLifecycle"
```

Expected: the scenario **"A delivered shipment cannot go back on the road" fails** at `Then the move is refused with the message ...`, because `Assert.NotNull(state.Error)` finds no exception. `ShipmentStatusRulesTests.CanTransition_SkippedBackwardsOrFinal_ReturnsFalse("Delivered","InTransit")` fails as well.

Observed:

```text
Failed A delivered shipment cannot go back on the road [12 ms]
  Assert.NotNull() Failure: Value is null
Failed ApexPost.UnitTests.Api.ShipmentStatusRulesTests.CanTransition_SkippedBackwardsOrFinal_ReturnsFalse(current: "Delivered", next: "InTransit") [1 ms]
  Assert.False() Failure
Failed ApexPost.UnitTests.Api.ShipmentStatusRulesTests.Apply_DeliveredBackToInTransit_ThrowsShipmentConflictException [3 ms]
  Assert.Throws() Failure: No exception was thrown
```


All three changes were reverted and the suite was green again afterwards.

---

## Rejected Copilot test (for the viva)

<!-- Describe one generated test you rejected: what it asserted, why it was not meaningful
     (for example: it only verified SaveChangesAsync with Times.AtLeastOnce and would pass even if
     the branch was saved twice, or it recomputed the expected value using the same expression as the code). -->
