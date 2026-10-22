# Versioned Context Capsule interchange prototype

`ContextCapsuleFormat` provides **pure serialization**, not durable storage. Version 1 stores Capsule names and normalized path references as JSON; no file content is serialized.

- Maximum 100 Capsules, at most 1,000 consumed entries per Capsule, 120 characters per name, and 1,000,000 JSON characters.
- Unknown versions, malformed JSON, invalid references and oversized documents fail explicitly. Existing in-memory Capsules are not modified on deserialization failure.
- Offline/missing paths are retained as references. No import operation moves or edits files.
- JSON may contain sensitive local paths: do not upload it automatically.
- Durability (atomic local writes, backup/repair, multiple windows, corruption recovery, schema migration) and user-visible import/export remain **unimplemented**.

Tests use synthetic inputs and run with `./scripts/test.ps1` on Windows with .NET 10. Do not call this a persisted Capsule feature or a completed migration system.
