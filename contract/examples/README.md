# Contract example corpus

Canonical **wire-form** JSON for the shared DTOs in [`../openapi.yaml`](../openapi.yaml).

These files are the single golden corpus for the Tier-1 **contract-parity** tests
(see [`docs/REGRESSION-TESTING.md`](../../docs/REGRESSION-TESTING.md)). The same
files are asserted on both sides of the Rust/.NET seam:

- **.NET** — `service/Api.Tests.Unit/ContractParityTests.cs` deserializes each file
  into the matching `service/Api/Contracts.cs` DTO and re-serializes with
  System.Text.Json Web defaults; the result must structurally equal the file.
- **Rust** — `crates/api-types` round-trips the same files through its mirrored
  structs.

If a DTO diverges from the contract on either side (a renamed field, a changed
enum spelling like `GCCHigh`, a wrong casing), the round-trip stops matching and
the test fails. Keep these in camelCase wire form; add a file when you add a DTO.
