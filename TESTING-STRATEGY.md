# ApexPost Testing Strategy (Week 5)

The aim is not the largest number of tests. It is to put each risk at the cheapest level that can actually prove it, and to know what each level does **not** prove.

Scope: application and domain services, validation rules, repositories (integration level), Web API controllers and endpoints, the Part D typed API client, and the Couchbase Lite outbox and repository. Out of scope: any MAUI/UI/browser testing.

---

## 1. Risk map

Likelihood and impact are rated High / Medium / Low. "Covered by" points to the level that carries the main proof.

| # | Risk | Why it is likely | Cost if it breaks | L | I | Covered by |
|---|---|---|---|---|---|---|
| R1 | **Shipment status transitions** skip a step or go backwards (e.g. Delivered → InTransit) | Transition table is edited by hand; casing differences between clients | Customers and partners see a false tracking history; delivered parcels look lost | M | H | Unit theory over every pair (`ShipmentStatusRulesTests`), BDD outline (`ShipmentStatusLifecycle.feature`), API 409 test |
| R2 | **Duplicate tracking numbers** accepted | Two admins / retries create the same number | Two parcels share one tracking history; scans land on the wrong parcel | M | H | API integration (`PostShipment_DuplicateTrackingNumber_Returns409ProblemDetails`), BDD `ShipmentCreation.feature` |
| R3 | **Scan de-duplication** fails, so a retried upload stores a scan twice | Mobile networks drop responses; the client retries by design | Duplicate history entries, wrong "latest scan", audit disputes | H | H | SQL Server repository test (`ShipmentMetadataStoreTests`), API test, BDD `ScanSync.feature` |
| R4 | **JWT role rules / courier ownership**: a Courier deletes shipments or uploads scans for another courier | Attributes are easy to remove or mistype during refactoring | Data tampering, unauthorised deletion | M | H | Unit (attribute metadata, controller with mocked service), API integration 401/403 tests |
| R5 | **Client retry behaviour** retries a 4xx, retries a non-idempotent POST, or never stops | Resilience settings are configuration-driven and easy to get wrong | Duplicate side effects, battery drain, server overload during outages | M | H | Unit with fake/mocked `HttpMessageHandler` through the real Polly pipeline (`ResiliencePipelineTests`, `ApexApiClientHttpTests`) |
| R6 | **Offline outbox** marks a scan Synced that the server never stored, or loses scans after a partial failure | Batch logic, partial results and exceptions interleave | Scans silently lost: no proof of pickup or delivery | M | H | Unit with Moq (`OutboxServiceTests`), BDD `ScanSync.feature` end to end |
| R7 | **Validation boundaries** (tracking number format, 0.1–70 kg, phone, future scan time) drift | Rules are repeated in DTOs and services | Bad data in the system of record, or valid parcels refused at the counter | M | M | Unit boundary theories (`ShipmentRequestValidationTests`, `ScanUploadRulesTests`), BDD outline for weight |
| R8 | **Heavy-parcel surcharge** (new rule this week) applied at the wrong threshold | New code; 30 kg exactly is an edge case | Under/over-charging customers | M | M | TDD unit tests (`ShipmentPricingTests`) |
| R9 | Couchbase Lite **SQL++ queries** return wrong data (field names, parameter binding, ordering) | Query text is only checked at run time | Pending scans not found, so never uploaded | M | H | Integration against a real embedded database (`CouchbaseScanRepositoryTests`) |
| R10 | Errors leak internals, or the partner key fails open | Exception handler / key middleware edits | Information disclosure; partner endpoint open to anyone | L | H | Unit (`GlobalExceptionHandlerTests`, `PartnerApiKeyMiddlewareTests`), API test for a wrong key |

---

## 2. Test pyramid for ApexPost

Approximate number of executed test cases (each theory row counts as one case):

| Level | Projects | Cases (approx.) | Share | What lives here |
|---|---|---|---|---|
| **Unit** | `ApexPost.UnitTests`, `ApexPost.Courier.Tests` | ~215 | ~75 % | Domain rules and value objects, pricing (TDD), status transitions, scan rules, DTO validation boundaries, application services with Moq, controllers with a mocked `IShipmentService`, exception handler, partner key, outbox with Moq, typed client with a mocked `HttpMessageHandler`, resilience pipeline |
| **Integration** | `ApexPost.IntegrationTests`, `ApexPost.Api.IntegrationTests` | ~50 | ~18 % | EF Core repositories, unit of work, stored procedures and rowversion against SQL Server; Couchbase Lite repository against a real embedded database; API endpoints end to end with `WebApplicationFactory` |
| **BDD (acceptance)** | `ApexPost.Specs` | ~20 scenarios | ~7 % | Business-language scenarios for shipment creation, offline scan sync and the status lifecycle |

```
          /\        BDD: 20 scenarios — the business flows, readable by the product owner
         /  \
        /----\      Integration: ~50 — real SQL Server, real Couchbase Lite, real HTTP pipeline
       /      \
      /--------\    Unit: ~215 — milliseconds each, no I/O, run on every build
```

The shape is deliberate: rules with many edge cases (transitions, boundaries, retry decisions) are checked exhaustively at unit level, where a case costs milliseconds. Integration tests are kept to the things only a real database or a real pipeline can prove (constraints, transactions, SQL++, routing, auth, serialisation). BDD covers a few end-to-end business stories, not every edge case.

### Level of each BDD feature

| Feature | Level | Why |
|---|---|---|
| `ShipmentCreation.feature` | **API** (in-memory host, test SQL Server database, test authentication) | The business outcomes (201, duplicate refused, weight refused, courier forbidden) come from validation, authorization, the unique key and the exception handler working together. Only the full pipeline shows that. |
| `ScanSync.feature` | **API, client to server** (real `OutboxService` + `ApiShipmentUploader` + `ApexApiClient` calling the in-memory API) | The behaviour that matters (exactly-once storage, duplicates skipped, bad scans flagged Failed) only exists when the courier client and the server's de-duplication meet. The device store is an in-memory fake because Couchbase Lite is proved separately. |
| `ShipmentStatusLifecycle.feature` | **Domain rule** (`ShipmentStatusRules`) | The lifecycle is a pure rule. Running it through HTTP would add minutes and prove nothing extra; one API test already proves the rule is wired to a 409. |

---

## 3. Framework choice: xUnit (one framework across the solution)

| Criterion | Why xUnit fits |
|---|---|
| **Consistency with existing tests** | All Week 2–4 tests (about 120 methods) are already xUnit. Switching would cost a rewrite and add nothing. |
| **Test isolation** | xUnit creates a new instance of the test class for every test, so per-test state lives in fields and cannot leak between tests. NUnit reuses one instance per fixture by default, which makes accidental shared state easier. |
| **Setup / teardown style** | Constructor = setup, `IDisposable`/`IAsyncLifetime` = teardown, `IClassFixture<T>` for expensive shared resources (the API host and its database). This is explicit and async-friendly. |
| **Parameterised tests** | `[Theory]` with `[InlineData]` for simple values and `TheoryData<T>` / `[MemberData]` for decimals and other values attributes cannot hold. |
| **Reqnroll support** | `Reqnroll.xUnit` generates xUnit tests from the feature files, so BDD runs in the same runner, Test Explorer and `dotnet test`. |
| **Team familiarity / tooling** | Works out of the box with Visual Studio Test Explorer, `dotnet test`, coverlet and `WebApplicationFactory` samples. |

NUnit is not used anywhere.

---

## 4. Existing tests review (Weeks 2–4)

Every test class that existed before this week, with the decision taken. No test was deleted without a replacement.

| Test class (before) | Decision | Reason |
|---|---|---|
| `UnitTests/Domain/BusinessRuleTests` | **Modified** | Several tests asserted two or three different rules each (e.g. inactive staff, closed branch and wrong branch in one test), so a failure did not say which rule broke. Split into one rule per test, AAA, `Method_Scenario_ExpectedResult` names; added the boundaries that were missing (money order exactly at the 1,000 threshold, removing exactly the stock on hand). |
| `UnitTests/Domain/ValueObjectAndTransactionTests` | **Modified** | Behaviour was good; renamed to the convention, merged "blank" and "too long" branch-code cases into one theory, made AAA explicit, turned the three tax-rate asserts into a theory. |
| `UnitTests/Application/IdentityVerificationUnitOfWorkTests` | **Modified** | Hand-written `SpyUnitOfWork` replaced by `Mock<IUnitOfWork>` with `Verify(..., Times.Once/Never)`; the in-memory repositories stay (state-based, see section 5) and are now shared in `InMemoryRepository<T>`. |
| `UnitTests/Application/MoneyOrderServiceUnitOfWorkTests` | **Modified** | Same change as above; also asserts nothing is stored when the identity rule fails. |
| `UnitTests/ApiRequestValidationMatrixTests` | **Replaced** by `Api/ShipmentRequestValidationTests` | It generated over 1,000 rows, and a nested loop made one data source produce about 3,600 cases, mostly duplicates. Two tests had been commented out because their theory data used `Guid.NewGuid()` and `DateTime.UtcNow`, so the data changed between discovery and execution. The replacement keeps only boundary and representative cases (about 60) and makes time relative to the run. |
| `UnitTests/ApiAuthorizationMetadataTests` | **Modified** | Moved to `Api/`, renamed, `nameof` instead of strings, the separate admin-only theory folded into the role theory. |
| `UnitTests/GlobalExceptionHandlerTests` | **Modified** | Renamed, setup extracted, now also asserts the `traceId` extension. |
| `UnitTests/OutboxServiceTests` | **Modified** (moved to `Courier/`) | Hand-written fakes replaced by Moq so the tests can verify *which* scans were uploaded and *which* status writes happened (`It.Is`, `Times.Exactly`). Behaviours unchanged. |
| `UnitTests/PartnerApiKeyMiddlewareTests` | **Kept** (renamed, moved to `Api/`) | Already focused and fast. |
| `UnitTests/PartnerApiKeyValidatorTests` | **Kept** (renamed, moved to `Api/`) | Also asserts `IsConfigured` for the fail-closed case. |
| `Courier.Tests/ApiClientAndUploaderTests` | **Kept** (renamed) | Covers JSON source generation, ProblemDetails mapping and uploader outcomes with a fake handler. |
| `Courier.Tests/AuthHeaderHandlerTests` | **Kept** (renamed) | Covers token attach, 401 logout, 403 keep-token. |
| `Courier.Tests/ResiliencePipelineTests` | **Kept** (renamed) | Real Polly pipeline with a fake handler: retry only on 408/5xx, never on 4xx, circuit opens. |
| `Courier.Tests/TestDoubles` | **Kept** | Stub handler and in-memory token store used above. |
| `IntegrationTests/Security/SecurityPipelineTests` | **Kept** | Week 3 MVC pipeline end to end (headers, API key 401/403, 429, login redirect, clerk 403 on void, correlation ID). Names already state scenario and result. |
| `IntegrationTests/Persistence/SqlServerPersistenceTests` | **Kept** | The proof for EF Core mapping, rowversion conflicts, rollback of failed sales, stored procedures; this cannot be moved to a lower level. |
| `IntegrationTests/Authentication/ApexPostWebFactory` | **Kept** | Creates and drops a unique SQL Server database per factory. It sets the connection string through an environment variable; that is process-wide state, which is why that assembly disables parallel execution. |

**New this week:** `Domain/ShipmentPricingTests` (TDD), `Api/ShipmentStatusRulesTests`, `Api/ScanUploadRulesTests`, `Api/ShipmentsV2ControllerTests`, `Application/BranchAdministrationServiceTests`, `Courier.Tests/ApexApiClientHttpTests`, `IntegrationTests/Courier/CouchbaseScanRepositoryTests`, the `ApexPost.Api.IntegrationTests` project and the `ApexPost.Specs` project.

---

## 5. Data access strategy

| Approach | Used for | What it proves | What it does **not** prove |
|---|---|---|---|
| **Mocked repository / unit of work (Moq)** | Application services (`BranchAdministrationService`, money order, identity verification), outbox (`IScanRepository`, `IShipmentUploader`) | The service's decisions: what it stages, that it commits exactly once on success and never on failure, that it stops on a failed batch | That the query is correct, that constraints hold, that the mapping round-trips, that the transaction actually rolls back |
| **State-based in-memory fake** | Repositories in application tests where the result (what got stored) matters more than the calls; the courier device store in BDD | That the right entities end up stored | Same limits as a mock: no SQL, no constraints, no concurrency |
| **SQLite in-memory** | **Not used** | – | The schema relies on SQL Server features: `rowversion`, `datetimeoffset`, `SYSUTCDATETIME()` defaults, CHECK constraints in T-SQL syntax, stored procedures and serializable transactions. SQLite would either fail to create the schema or silently behave differently (no rowversion generation), which is worse than not testing. |
| **SQL Server test database** (LocalDB by default, `APEXPOST_TEST_CONNECTION` to override) | `ApexPost.IntegrationTests`, `ApexPost.Api.IntegrationTests`, `@api` specs | Real migrations, constraints, unique keys, rowversion, transactions, stored procedures, the scan primary key used for de-duplication | Production-scale performance and locking under real concurrency; anything outside the database (network, real JWT issuer) |
| **Real embedded Couchbase Lite in a temp folder** | `CouchbaseScanRepositoryTests` | SQL++ text and parameters, value indexes, document mapping, change listener | Multi-process access (deliberately not supported), sync with a server |

Each SQL Server test host creates a database with a unique name (`ApexPost_ApiTests_{guid}`), applies the migrations, seeds the minimum reference data and drops the database when it is disposed. Tests never touch a development or production database.

### Why EF Core's `DbContext`/`DbSet` is never mocked

Mocking `DbSet<T>` turns LINQ-to-Entities into LINQ-to-Objects. That gives false confidence in four ways:

1. **Translation is skipped.** A query that EF Core cannot translate to SQL (a client-side method in `Where`, an unsupported `GroupBy`) passes against a list and throws at run time.
2. **Semantics differ.** In-memory string comparison is case-sensitive; SQL Server's default collation is not. Null handling, decimal precision and date types also differ.
3. **The database's guarantees disappear.** Unique keys (duplicate tracking numbers, R2), the scan primary key (R3), CHECK constraints, cascades, rowversion and serializable transactions are exactly what the risky features depend on, and a mock has none of them.
4. **Tests copy the implementation.** Setting up `DbSet` mocks means re-describing the query in the test, so the test only proves the code does what the code does.

So services that own queries (`ShipmentService`, `ShipmentMetadataStore`, EF repositories) are tested against SQL Server, and only the layers *above* the repository interfaces are mocked.

### Mocks and what they do not prove (viva notes)

- `Mock<IScanRepository>` in `OutboxServiceTests` proves the outbox writes `Synced`/`Failed` only for scans that got an outcome and stops after a failed batch. It does **not** prove the SQL++ query for pending scans is right (that is `CouchbaseScanRepositoryTests`).
- `Mock<IShipmentService>` in `ShipmentsV2ControllerTests` proves the controller returns 403 for another courier's scans *without calling the service*, and builds the right `CreatedAtRoute`. It does **not** prove the route template, model binding, or that authentication populates the `courierId` claim (that is `ShipmentEndpointTests`).
- `Mock<HttpMessageHandler>` in `ApexApiClientHttpTests` proves how the client reacts to 200, 404, 500, ProblemDetails, cancellation and a hung server. It does **not** prove the real API returns those shapes; `ScanSync.feature` runs the client against the real API to catch contract drift.

---

## 6. Conventions and determinism

- **Naming:** `Method_Scenario_ExpectedResult`, e.g. `Quote_WeightAbove30Kg_AddsHeavyParcelSurcharge`, `UploadScans_ForAnotherCourier_Returns403`.
- **AAA:** every test has Arrange / Act / Assert blocks (commented in the Week 5 tests). One behaviour per test; several asserts are allowed only when they describe the same outcome.
- **Exceptions and boundaries:** expected exceptions are asserted with `Assert.Throws`/`ThrowsAsync` on an `act` delegate; boundary cases sit on both sides of each limit (0.09/0.1, 70/70.01, 30/30.01 kg, 6/7 and 15/16 phone digits, +5 s/+6 s clock skew).
- **No `DateTime.Now` in rules under test:** the server reads time through `TimeProvider`; `ScanUploadRules.Validate` receives the current time as a parameter, so tests pass a fixed instant. Where a DTO reads the clock, test data is relative to the moment the test runs (never computed at discovery).
- **No order dependence or shared mutable state:** xUnit creates a new class instance per test; shared hosts are class fixtures, and each test uses its own tracking numbers; Reqnroll state is passed by context injection and cleaned up in `AfterScenario`.
- **No real network:** HTTP in unit tests goes through fake or mocked handlers; the API is hosted in memory by `WebApplicationFactory`.
- **Fictional data and no secrets:** keys used in tests (`partner-key-for-tests`, the test signing key) are test values set in code for the in-memory host only.

---

## 7. What we deliberately do not test

| Not tested | Why |
|---|---|
| MAUI/console UI screens, navigation, browser pages | Out of scope for this week; the screens only collect input and call services that are tested. |
| ASP.NET Core, EF Core, Polly and Couchbase internals | Framework code is tested by its owners; we test our configuration of it (pipeline, policies, queries). |
| JWT signature validation with real tokens | Framework feature configured in `Program.cs`; covered by the Week 4 `.http` checks. Automated tests use a test authentication scheme so that role rules can be tested without a signing key. |
| Generated code (NSwag client, EF migrations, Reqnroll code-behind, JSON source generator) | Generated from inputs that are themselves tested; regenerating is the fix, not a test. |
| Trivial DTO getters/setters and mappings with no logic | No behaviour to break; they are exercised by the integration tests anyway. |
| Performance and load | Needs a production-like environment; covered by the Week 3 index/plan work, not by unit tests. |
| Couchbase Lite multi-process access | Not supported by design (see OFFLINE-DESIGN.md). |

---

## 8. Running the tests

See README section 6. In short:

```powershell
dotnet test ApexPost.sln                         # everything (needs LocalDB or APEXPOST_TEST_CONNECTION)
dotnet test tests/ApexPost.UnitTests             # unit only, no database
dotnet test tests/ApexPost.Courier.Tests
dotnet test tests/ApexPost.Specs                 # BDD scenarios
dotnet test ApexPost.sln --collect:"XPlat Code Coverage"   # optional coverage
```

---

## 9. Reflection

**What the strategy caught.** Reviewing the existing tests before writing new ones found more than any new test did. Two Week 4 validation tests had been switched off because their theory data used `Guid.NewGuid()` and `DateTime.UtcNow`: the runner computed the data at discovery, so a "one minute in the future" value could be in the past by the time it ran. One data method had a nested loop that produced about 3,600 mostly duplicate cases, which inflated the test count and slowed the run without adding any coverage. The same review showed that the server's scan validation read `DateTime.UtcNow` directly, which made the 5-second clock-skew boundary impossible to test exactly. Moving it behind `TimeProvider` and a pure `ScanUploadRules.Validate(scan, now)` made the +5 s / +6 s boundary a plain unit test. Writing the risk map also showed that the status lifecycle and the pricing formula were each written twice inside `ShipmentService`. Both are now single, tested functions. The surcharge rule was built test-first, and the "exactly 30 kg is not heavy" case was decided when the first test was written, not discovered in production.

**What I would change.** The SQL Server-backed tests depend on LocalDB, which exists on Windows developer machines but not on a Linux CI agent. Next time I would run the same tests against a SQL Server container (Testcontainers) so `dotnet test` has no machine prerequisite at all. I would also split the Week 3 `ApexPostWebFactory` so it no longer sets an environment variable; that is the one piece of process-wide state left in the suite, and it is the reason that assembly cannot run in parallel. Finally, contract drift between the API and the generated client is only caught indirectly (by the ScanSync specs); a small test comparing the live OpenAPI document with `contracts/v2.json` would make it explicit.

**Most confidence per minute.** The parameterised unit theories over pure rules (status transitions, scan rules, weight and pricing boundaries) were the best value: each new edge case is one `InlineData` line and runs in microseconds. A close second was the small set of `WebApplicationFactory` tests. Five or six of them proved routing, validation, authorization, the exception handler and the SQL Server constraints working together, which no number of mocked unit tests can do. The BDD scenarios cost the most per test, but they are the only tests the business side can read and challenge, so they are kept few and focused on stories.
