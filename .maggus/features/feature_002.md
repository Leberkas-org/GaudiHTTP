<!-- maggus-id: 29f91fd7-8bcd-49b9-8b7f-88f7b4371432 -->
<!-- maggus-id: 20260326-130819-feature-027 -->

# Feature 027: RFC Knowledge Base Reorganisation (Phases 1–2)

## Introduction

Clean up and reorganise the TurboHttp RFC Knowledge Base (`notes/RFC/`) to establish a unified, well-structured, semantically validated foundation for RFC compliance tracking. This feature implements **Phases 1–2** of a 5-phase roadmap: establishing a new RFC Index template standard and auditing/trimming oversized or sparse RFC documentation.

The RFC Knowledge Base currently contains 404 markdown files across 10 RFCs (HTTP/1.0, HTTP/1.1, HTTP/2, HPACK, HTTP/3, QUIC, QPACK, HTTP Semantics, Cookies, Caching) with inconsistent structure, missing cross-references, and quality issues (RFC9000 oversized at 78 sections, RFC9110/9111 sparse). This feature resolves those structural problems and prepares the vault for future code-linking and compliance scoring.

### Architecture Context

- **Vision alignment:** Fulfils the core goal of producing a "production-ready HTTP client library" by establishing clear RFC compliance documentation that developers can navigate and reference
- **Components involved:** Knowledge vault (`notes/RFC/`) organises RFC sections; Obsidian + MCP tools provide vault management; Roslyn Navigator will support future code-linking (Phase 3)
- **New patterns:** Introduces RFC Index template standard; establishes semantic validation workflow for WikiLinks and cross-references
- **Deferred:** Code-linking (Phase 3), compliance scoring (Phase 4), cross-references (Phase 5) — planned for future features (028+)

## Goals

- ✅ Establish a unified RFC Index template (`RFC*.md`) with consistent structure, metadata, and quality standards
- ✅ Apply the template to all 10 RFCs, ensuring RFC-level compliance overview
- ✅ Audit RFC9000 (QUIC) and trim from 78 → 30–40 core sections (HTTP/3-relevant only)
- ✅ Expand RFC9110 (HTTP Semantics) from ~20 → ~60 sections (restore completeness)
- ✅ Expand RFC9111 (Caching) from ~15 → ~40 sections (restore completeness)
- ✅ Verify RFC9114 (HTTP/3) section completeness and flag gaps
- ✅ Validate all internal WikiLinks and cross-references for semantic correctness
- ✅ Document handoff tasks for Phases 3–5 (code-linking, compliance scoring, cross-RFC linking)

## Tasks

### TASK-027-001: Create RFC Index Template Standard

**Description:** As a knowledge manager, I want a unified RFC Index template (`RFC*.md`) so that every RFC has consistent metadata, structure, and quick reference information.

**Token Estimate:** ~20k tokens

**Predecessors:** none

**Successors:** TASK-027-002, TASK-027-003, TASK-027-004, TASK-027-005

**Parallel:** no — establishes template for all subsequent tasks

**Acceptance Criteria:**

- [ ] Template created at `notes/Templates/RFC-Index.md` with sections:
  - Quick Reference (Compliance Score, Implementation Status, Test Files, Key Gaps)
  - Core Concepts (bullet list with section links)
  - Implementation Notes (Encoder/Decoder files, Tests, Status)
  - Sections (list of section links with status badges)
  - Dependencies (Depends on, Used by, other RFCs)
- [ ] Frontmatter standardized: `title`, `rfc_number`, `description`, `tags` (all RFC files)
- [ ] Example applied to RFC1945 (HTTP/1.0) as reference implementation
- [ ] Template documented in `notes/VAULT_STYLE_GUIDE.md` (RFC Index section)
- [ ] All 10 RFC index files updated to new template structure
- [ ] No broken WikiLinks introduced; all template examples verified

### TASK-027-002: Audit & Trim RFC9000 (QUIC)

**Description:** As a knowledge keeper, I want RFC9000 sections trimmed from 78 → 30–40 core concepts so that the QUIC documentation focuses on HTTP/3-relevant topics and reduces cognitive load.

**Token Estimate:** ~35k tokens

**Predecessors:** TASK-027-001

**Successors:** TASK-027-006

**Parallel:** yes — can run alongside TASK-027-003, TASK-027-004, TASK-027-005

**Acceptance Criteria:**

- [ ] RFC9000 sections audited: identify core (HTTP/3 essential), secondary (useful context), and theoretical (skip)
- [ ] Sections reduced to 30–40 files covering:
  - Packet structure & types
  - Variable-length integers (core)
  - Connection IDs & handshake concepts
  - Stream frame basics (cross-referenced to RFC9114)
  - Flow control principles (light treatment)
- [ ] Removed sections documented in `notes/RFC/RFC9000/TRIM_RATIONALE.md` with reasoning
- [ ] Remaining sections reviewed for completeness (no empty or stub files)
- [ ] All removed section links cleaned from RFC9000.md index
- [ ] RFC9113 and RFC9114 links to RFC9000 verified (no broken refs after trim)

### TASK-027-003: Expand RFC9110 (HTTP Semantics) to ~60 Sections

**Description:** As a protocol implementer, I want RFC9110 expanded from ~20 to ~60 sections so that HTTP semantics (redirects, retries, content negotiation, method semantics, status codes) have complete coverage matching the RFC structure.

**Token Estimate:** ~40k tokens

**Predecessors:** TASK-027-001

**Successors:** TASK-027-006

**Parallel:** yes — can run alongside TASK-027-002, TASK-027-004, TASK-027-005

**Acceptance Criteria:**

- [ ] RFC9110 sections mapped against official RFC 9110 (sections 1–19 + appendices)
- [ ] Gap analysis completed: identify missing sections in `notes/RFC/RFC9110/EXPANSION_GAPS.md`
- [ ] New section files created for missing areas (~40 new files):
  - Request/response structure (§5)
  - Method semantics (§9)
  - Status codes (§15)
  - Content negotiation (§12)
  - Request routing & conditional logic (§11, §13)
- [ ] Section files follow naming: `NN_description.md` (two-digit prefix)
- [ ] RFC9110.md index updated with all sections; status badges applied
- [ ] All new sections reviewed for placeholder-only content vs. meaningful stubs
- [ ] Cross-references to RFC9111 (caching) and RFC6265 (cookies) verified

### TASK-027-004: Expand RFC9111 (Caching) to ~40 Sections

**Description:** As a caching implementer, I want RFC9111 expanded from ~15 to ~40 sections so that caching semantics (freshness, validation, storage, cache directives) have complete RFC coverage.

**Token Estimate:** ~30k tokens

**Predecessors:** TASK-027-001

**Successors:** TASK-027-006

**Parallel:** yes — can run alongside TASK-027-002, TASK-027-003, TASK-027-005

**Acceptance Criteria:**

- [ ] RFC9111 sections mapped against official RFC 9111 (sections 1–6 + appendices)
- [ ] Gap analysis completed: identify missing sections in `notes/RFC/RFC9111/EXPANSION_GAPS.md`
- [ ] New section files created for missing areas (~25 new files):
  - Freshness (§4.2 with subsections)
  - Validation & revalidation (§4.3)
  - Cache-Control directives (§5.2, §5.3)
  - Storage semantics (§3)
- [ ] Section files follow naming: `NN_description.md` (two-digit prefix)
- [ ] RFC9111.md index updated with all sections; status badges applied
- [ ] Cross-references to RFC9110 (semantics) and RFC6265 (cookies) verified

### TASK-027-005: Verify RFC9114 (HTTP/3) Section Completeness

**Description:** As an HTTP/3 developer, I want RFC9114 sections audited to confirm all sections have content (not empty stubs) and are properly linked so that HTTP/3 documentation is complete and navigable.

**Token Estimate:** ~15k tokens

**Predecessors:** TASK-027-001

**Successors:** TASK-027-006

**Parallel:** yes — can run alongside TASK-027-002, TASK-027-003, TASK-027-004

**Acceptance Criteria:**

- [ ] RFC9114 has 86 section files; audit performed to verify:
  - No empty or placeholder-only files (must have meaningful content)
  - Sections properly ordered (NN_description.md with sequential numbering)
  - All sections linked in RFC9114.md index
- [ ] Gap analysis completed in `notes/RFC/RFC9114/AUDIT_REPORT.md`:
  - Which sections are thorough vs. sparse
  - Which sections reference RFC9000 (QUIC), RFC7541 (HPACK), RFC9204 (QPACK)
  - Encoder/decoder implementation gaps documented
- [ ] Cross-references to HTTP/2 (RFC9113), QUIC (RFC9000), QPACK (RFC9204) verified
- [ ] Status badges applied (✅ complete, 🔶 sparse, 🟡 stub)

### TASK-027-006: Link Validation & Semantic Correctness Check

**Description:** As a quality reviewer, I want all WikiLinks and cross-references validated for integrity (no broken links) and semantic correctness (links point to relevant sections) so that the RFC vault is navigation-safe and trustworthy.

**Token Estimate:** ~25k tokens

**Predecessors:** TASK-027-002, TASK-027-003, TASK-027-004, TASK-027-005

**Successors:** TASK-027-007

**Parallel:** no — depends on all expansion/trim tasks

**Acceptance Criteria:**

- [ ] Link validation script/process created:
  - Scans all `notes/RFC/**/*.md` files
  - Extracts WikiLinks: `[[path|DisplayName]]` format
  - Verifies target files exist
  - Reports broken links with file + line number
- [ ] Manual semantic review performed on:
  - RFC-to-RFC cross-references (RFC9113 → RFC7541 links contextually correct?)
  - Section-to-section links within RFC (correct reference to RFC 9113 §6 when discussing frames?)
  - Links to `notes/Architecture/` ADRs (if referenced)
- [ ] All broken links fixed; semantic issues documented in `LINK_VALIDATION_REPORT.md`
- [ ] Tag consistency validated (all RFC files have `rfc_number`, `tags` frontmatter)
- [ ] Frontmatter linting passes (no missing required fields)
- [ ] Build/test succeeds with no vault warnings

### TASK-027-007: Document Phases 3–5 Handoff & Create Future Feature Placeholders

**Description:** As a project planner, I want Phases 3–5 documented in a roadmap and placeholder feature files created so that future work (code-linking, compliance scoring, cross-references) is clearly scoped and scheduled.

**Token Estimate:** ~10k tokens

**Predecessors:** TASK-027-006

**Successors:** none

**Parallel:** no — final documentation task

**Acceptance Criteria:**

- [ ] Roadmap document created: `notes/RFC/PHASES_3-5_ROADMAP.md` describing:
  - Phase 3: Code-Linking (Encoder/Decoder → RFC section mapping) — deferred, planned for feature_028
  - Phase 4: Compliance Scoring (per-section scores + test coverage tracking) — feature_029
  - Phase 5: Cross-References (RFC dependencies, Architecture ADR backlinks) — feature_030
  - Estimated effort per phase (4–6 hours each)
- [ ] Notes added to `00-Index.md` linking to roadmap
- [ ] Placeholder feature files prepared (`feature_028_stub.md`, etc.) with guidance for future implementers
- [ ] Summary document created: `PHASES_1-2_COMPLETION_SUMMARY.md` with stats:
  - Files created/modified
  - Sections added/removed
  - Link validation results
  - Open questions for Phase 3

## Task Dependency Graph

```
TASK-027-001 ──→ TASK-027-002 ──┐
                                 ├─→ TASK-027-006 ──→ TASK-027-007
TASK-027-001 ──→ TASK-027-003 ──┤
                                 ├─→ (link validation)
TASK-027-001 ──→ TASK-027-004 ──┤
                                 │
TASK-027-001 ──→ TASK-027-005 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-027-001 | ~20k | none | no | — |
| TASK-027-002 | ~35k | 001 | yes (with 003-005) | — |
| TASK-027-003 | ~40k | 001 | yes (with 002, 004-005) | — |
| TASK-027-004 | ~30k | 001 | yes (with 002-003, 005) | — |
| TASK-027-005 | ~15k | 001 | yes (with 002-004) | — |
| TASK-027-006 | ~25k | 002-005 | no | — |
| TASK-027-007 | ~10k | 006 | no | — |

**Total estimated tokens:** ~175k

**Execution model:**
- **Phase A (Sequential):** TASK-027-001 (template foundation)
- **Phase B (Parallel):** TASK-027-002, 003, 004, 005 (4 concurrent audit/expansion tasks)
- **Phase C (Sequential):** TASK-027-006, 007 (validation + handoff)

## Functional Requirements

- FR-1: RFC Index template must include Quick Reference, Core Concepts, Implementation Notes, Sections list, Dependencies
- FR-2: All 10 RFC index files (`RFC*.md`) must conform to the new template structure
- FR-3: RFC9000 must be trimmed to 30–40 sections; trim rationale documented
- FR-4: RFC9110 must expand to ~60 sections with gap analysis documented
- FR-5: RFC9111 must expand to ~40 sections with gap analysis documented
- FR-6: RFC9114 must be audited for completeness; status badges applied
- FR-7: All WikiLinks across `notes/RFC/` must be validated for existence and semantic correctness
- FR-8: Broken links must be reported with file + line number in validation report
- FR-9: Tag consistency (frontmatter) must be verified across all RFC files
- FR-10: Roadmap for Phases 3–5 must be documented; future features clearly scoped

## Non-Goals

- **Code-linking (Phase 3)** — Encoder/Decoder → RFC section mapping is deferred to feature_028
- **Compliance scoring (Phase 4)** — Per-section test coverage & compliance scores deferred to feature_029
- **Cross-RFC dependencies (Phase 5)** — RFC dependency linking (RFC7541 ← RFC9113) deferred to feature_030
- **Automated code-linking scripts** — Manual Roslyn Navigator approach reserved for Phase 3
- **Full RFC text embedding** — Section files reference RFC text; no wholesale copying
- **Implementation refactoring** — This is knowledge base cleanup, not code changes

## Design Considerations

- **Obsidian MCP Integration:** Use `read_note`, `write_note`, `patch_note`, `search_notes` tools for vault operations
- **WikiLink Format:** Consistent `[[path|DisplayName]]` for internal references
- **Status Badges:** Use ✅ (complete), 🔶 (sparse), 🟡 (stub), 🟢 (excellent) for quick visual scanning
- **Naming Conventions:** Section files use `NN_description.md` two-digit prefix for ordering (matches existing patterns in RFC1945, RFC9112)
- **Cross-Reference Validation:** Manual semantic review required — automated tools can't verify "is this actually relevant?"

## Technical Considerations

- **Obsidian Vault:** Notes stored in `notes/RFC/` subdirectories; vault uses MCP tools for CI/CD and automation
- **GitLink:** Vault structure must remain git-tracked; `.gitignore` already excludes `notes/Debugging/` and `.obsidian/`
- **Link Formats:** All WikiLinks must use relative paths from vault root (e.g., `[[RFC/RFC9112/sections/06_message_body|Message Body]]`)
- **Architecture Integration:** Future Phase 3 will require Roslyn Navigator (`find_symbol`, `find_references`, `get_symbol_detail`) to map code to RFC sections
- **ARCHITECTURE.md Update:** Phase 3 (code-linking) may require updating `CLAUDE.md` with code-to-RFC mapping patterns

## Success Metrics

- ✅ All 10 RFC index files conform to new template (100% coverage)
- ✅ RFC9000 trimmed to 30–40 sections (50% reduction, quality maintained)
- ✅ RFC9110 expanded to ~60 sections (3× expansion)
- ✅ RFC9111 expanded to ~40 sections (2.7× expansion)
- ✅ RFC9114 audit completed with status badges applied
- ✅ Link validation report shows 0 broken WikiLinks
- ✅ Semantic review completed with no critical linking errors
- ✅ Phases 3–5 roadmap documented and ready for feature_028+

## Open Questions

- **Trim threshold for RFC9000:** Are 30–40 sections the right target, or should it be 20–30? (Consider HTTP/3 implementer perspective)
- **RFC9114 completeness:** Are the 86 existing sections comprehensive, or should we expand further before validating?
- **Section-level metadata:** Should each section file include implementation status (encoder/decoder files) in frontmatter for future Phase 3 linking?
- **Validation tooling:** Should link validation be scripted (bash/PowerShell) or manual Obsidian search?

---

**Status:** ✅ **Ready for Review & Clarification**

This plan covers **Phases 1–2 only** (RFC Index Template + Audit & Trim). Phases 3–5 (Code-Linking, Compliance Scoring, Cross-References) are deferred to future features (028–030) and will be documented in the handoff (TASK-027-007).

**Next Steps:**
1. Review and provide feedback on scope, token estimates, or task dependencies
2. Clarify answers to open questions (if any)
3. Once approved, execute TASK-027-001 (template creation) to unlock parallel execution of tasks 002–005
