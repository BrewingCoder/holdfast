# GraphQL Implementation Alternatives

## Context

HoldFast currently uses `gqlgen` (v0.17.88) for Go-based GraphQL code generation. During billing removal work (March 2026), we discovered that gqlgen's generated code caused SIGBUS/page fault crashes on both the development machine (Windows) and CI runner (Linux). The tool also fails silently when packages have compile errors, and generates 3-4MB single files that are difficult to review.

This document captures replacement options for future evaluation if gqlgen continues to cause stability issues.

## Current Tool: gqlgen

**Version:** v0.17.88 (latest as of March 2026)
**GitHub:** ~10K stars, 200+ open issues, maintained by 99designs
**Approach:** Schema-first, code generation (separate `generate` step)

### Known Issues
- Generated files are 3-4MB single files — enormous AST parsing overhead
- Memory-intensive codegen caused SIGBUS crashes on two separate machines
- Silent failures (exit 1, no output) when Go packages have compile errors
- Breaking changes between minor versions (e.g., Complexity method signature)
- No v1.0 after years of v0.17.x releases
- Contributor activity is sporadic

## Alternatives

### Option 1: graph-gophers/graphql-go (Stay in Go, No Codegen)
- **Approach:** Schema-first, runtime reflection (no code generation)
- **Migration effort:** Rewrite resolvers to implement interfaces
- **Pros:** Zero codegen step, same schema files, no generated files to review
- **Cons:** Slightly slower runtime, less compile-time type safety
- **Best for:** Quick fix if gqlgen becomes untenable

### Option 2: graphql-go/graphql (Stay in Go, Code-First)
- **Approach:** Code-first (define schema in Go code)
- **Migration effort:** Rewrite schema + resolvers
- **Pros:** Most starred Go GraphQL lib, no codegen, no .graphqls files
- **Cons:** Verbose schema definition, no schema-first workflow
- **Best for:** Projects that prefer code-first

### Option 3: Hot Chocolate / .NET (Full Rewrite)
- **Approach:** Code-first with source generators (compile-time)
- **Migration effort:** Full backend rewrite in C#
- **Pros:** Best-in-class GraphQL server, Scott's primary expertise, EF Core for Postgres, enterprise/federal credibility, source generators are part of `dotnet build` (no separate step)
- **Cons:** Full rewrite
- **Best for:** Long-term if Go is abandoned

### Option 4: TypeScript + Apollo Server / GraphQL Yoga
- **Approach:** Code-first or schema-first, graphql-codegen for types
- **Migration effort:** Full backend rewrite
- **Pros:** Full-stack JS consistency with frontend, massive ecosystem, rock-solid codegen
- **Cons:** Performance gap for heavy workloads vs Go
- **Best for:** Full-stack consistency

### Option 5: Rust + async-graphql
- **Approach:** Code-first with derive macros (compile-time)
- **Migration effort:** Full backend rewrite + learning Rust
- **Pros:** Fastest runtime, memory safe, codegen is part of `cargo build`
- **Cons:** Steep learning curve, smaller ecosystem
- **Best for:** Performance-critical long-term play

### Option 6: Keep Go, Replace GraphQL with ConnectRPC/gRPC
- **Approach:** Protobuf schema + code generation (proven stable for 15+ years)
- **Migration effort:** Replace GraphQL layer, add thin gateway for frontend
- **Pros:** Rock-solid codegen (protoc/buf), better for service-to-service
- **Cons:** Lose GraphQL's flexible querying
- **Best for:** If GraphQL itself is the problem, not just the Go implementation

### Option 7: Hybrid — GraphQL Gateway + Go Services
- **Approach:** TypeScript/Rust GraphQL gateway in front of Go REST/gRPC services
- **Migration effort:** Incremental — add gateway, convert resolvers to service calls
- **Pros:** No big rewrite, incremental migration path
- **Cons:** Added deployment complexity
- **Best for:** Gradual migration away from gqlgen

## Active Crash: Go Compiler vs 88K-Line Generated File

**Status as of 2026-03-20:** The crashes are NOT limited to gqlgen codegen. The Go compiler itself crashes (SIGBUS/page fault) when compiling the 88,298-line / 2.8MB `generated.go` file. This happens on both Windows (dev machine) and Linux (CI runner VM), and has taken down the Proxmox hypervisor host twice by freezing the VM mid-compilation.

**Root cause:** `private-graph/graph/generated/generated.go` is a single 88K-line file. The Go compiler's memory usage spikes during type-checking and compilation of files this large. gqlgen has no option to split output across multiple files.

### Immediate Remediation Steps

**Step 1: Upgrade Go version (go.mod 1.23.8 → 1.25.x)**
- Newer Go compiler has improvements for large file compilation and memory handling
- Low risk — Go maintains backward compatibility
- Change `go 1.23.8` in go.mod, run `go mod tidy`, rebuild

**Step 2: Reduce to single CI runner**
- Two simultaneous Go compilations on the same VM doubles memory pressure
- Stop `va-holdfast-2` systemd service until stability is confirmed
- Re-enable once single-runner builds are proven stable

**Step 3: Clean all build caches on runner**
- `go clean -cache -testcache` on the runner VM
- Previous SIGBUS crashes may have corrupted cached compilation artifacts
- Corrupted cache causes `bufio: buffer full` errors and cascading failures

**Step 4: Post-generation file splitting (if Steps 1-3 fail)**
- Write a script to mechanically split `generated.go` into multiple files by function
- Each file stays in the same `generated` package
- Reduces per-file compilation memory from ~2.8MB to ~500KB chunks
- Risk: must preserve package-level vars and init ordering

### If All Remediation Fails

Escalate to full replacement per the alternatives above. Priority:
1. `graph-gophers/graphql-go` — no generated files, lowest migration cost
2. Hot Chocolate (.NET) — full rewrite but most stable GraphQL ecosystem
3. Hybrid gateway — incremental migration

## Recommendation Priority

1. **Execute remediation steps 1-3** (Go upgrade, single runner, cache clean)
2. **If crashes continue:** Step 4 (file splitting) then evaluate `graph-gophers/graphql-go`
3. **If Go GraphQL ecosystem is fundamentally broken:** Evaluate Hot Chocolate (.NET) or hybrid gateway approach
4. **Long-term rewrite consideration:** Hot Chocolate (.NET) given maintainer expertise

## Decision Log

| Date | Decision | Reason |
|------|----------|--------|
| 2026-03-19 | Upgraded gqlgen v0.17.70 → v0.17.88 | Silent crashes during codegen, SIGBUS on CI |
| 2026-03-20 | Added Makefile cache verification | Corrupted build cache caused gqlgen failures |
| 2026-03-20 | Documenting alternatives | SIGBUS on CI runner confirmed old generated code as root cause |
| 2026-03-20 | Bumped va-holdfast VM 8GB → 16GB | Dual runner compilations exhausting memory |
| 2026-03-20 | Second Proxmox host crash | Go compiler crashing on 88K-line generated.go even with new gqlgen + 16GB |
| 2026-03-20 | Beginning remediation steps 1-3 | Upgrade Go, single runner, clean caches |
