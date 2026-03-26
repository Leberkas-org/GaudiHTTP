<!-- maggus-id: 1ded39e0-2ca7-4703-b120-efd4930f5629 -->

# Feature 003: RFC Markdown Restructuring for MCP Search

## Introduction

Convert the 10 RFC specification files (`rfc-specs/RFCNNNN/RFCNNNN.md`) from monolithic documents with embedded full-text code blocks into **per-section markdown files optimized for MCP-based full-text search**. Each RFC will be split into individual section files (`.../sections/NN_SectionTitle.md`), with rich frontmatter including `description`, `tags`, and `rfc_section` metadata to maximize BM25 search precision.

**Goal:** When a user (or Claude via MCP) searches for "chunked transfer encoding", the result points directly to RFC 9112 §7.1 (a focused 5–10 KB file) rather than the entire 2500-line RFC 9112 document.

### Architecture Context

- **Vision alignment:** Supports knowledge management and rapid RFC lookups for notes protocol implementation and compliance verification
- **Components involved:** MCP `search_notes` tool (Obsidian), markdown file structure in `rfc-specs/`
- **New patterns:** Section-file indexing, adaptive granularity splitting (large RFCs only)
- **No breaking changes:** Existing `rfc-specs/INDEX.md` and Obsidian vault remain functional; splitting is additive

---

## Goals

1. **Optimize BM25 search precision** — Section-level files mean MCP search returns the exact RFC section (not a 10,000-line document with a single match deep inside)
2. **Support adaptive splitting** — Large RFCs (>2000 lines) split adaptively; small RFCs kept whole for simplicity
3. **Preserve RFC authenticity** — Full original text preserved verbatim inside each section; no editorial changes
4. **Surface normative requirements** — MUST/SHOULD/MAY sentences blockquoted for prominence in BM25 scoring
5. **Enable rapid navigation** — Section files discoverable via section number, title, and requirement keywords
6. **Maintain git-friendly structure** — Staged and committed (no uncommitted .txt files left behind)

---

## Tasks

### TASK-003-001: Move RFC folders from rfc-specs/ to notes/RFC/
**Description:** As a developer, I want to move the 10 RFC folders from `rfc-specs/RFCNNNN/` to `notes/RFC/RFCNNNN/` so that after splitting, the section files are immediately visible in Obsidian (which uses `notes/RFC/` as vault root).

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** TASK-003-002
**Parallel:** yes — can run alongside TASK-003-004
**Model:** haiku

**Acceptance Criteria:**
- [ ] Verify current state: `rfc-specs/` contains `RFC1945/RFC1945.md`, `RFC6265/RFC6265.md`, ..., `RFC9204/RFC9204.md` (10 files)
- [ ] Create target directories in `TurboHttp/RFC/`: `RFC1945/`, `RFC6265/`, ..., `RFC9204/`
- [ ] Copy each `rfc-specs/RFCNNNN/RFCNNNN.md` to `TurboHttp/RFC/RFCNNNN/` (create subdirectories as needed)
- [ ] Move README.md and INDEX.md from `rfc-specs/` to `TurboHttp/RFC/` (for context at vault root)
- [ ] Remove old `rfc-specs/RFCNNNN/` directories (no longer needed; source of truth moves to TurboHttp/RFC/)
- [ ] Update git tracking: `git rm -r rfc-specs/RFCNNNN/` for each folder
- [ ] Verify git status shows moved files (should show as renames or deletions + additions, depending on git config)
- [ ] All changes staged (not committed yet)

### TASK-003-002: Design RFC splitting algorithm and parse section boundaries
**Description:** As a developer, I want to determine the optimal section boundary detection algorithm so that large RFCs are split at semantically meaningful boundaries (adaptive granularity).

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-003-001
**Successors:** TASK-003-003
**Parallel:** yes — can run alongside TASK-003-003
**Model:** opus

**Acceptance Criteria:**
- [ ] RFC plain-text section heading pattern identified: lines matching `^[[:space:]]{0,4}[0-9]+(\.[0-9]+){0,3}\.[[:space:]]{2,}[A-Z]` (e.g., "1.  Title", "   1.2.3.  Subtitle")
- [ ] Algorithm for adaptive granularity designed:
  - Extract top-level section size (lines between section start and next section)
  - If top-level section > 300 lines: split further at H2 subsections (e.g., "1.1.", "1.2."); otherwise keep whole
  - If subsection > 300 lines: further split at H3 (rarely necessary for RFCs)
- [ ] Edge cases documented:
  - Appendices (labeled "Appendix A", "Appendix B") — treated as top-level sections
  - TOC section — extracted separately to index file
  - ABNF blocks — must not be split mid-definition (detected via indented lines matching `   [A-Za-z][A-Za-z0-9-]* = ...`)
- [ ] Section numbering scheme established: `NN_title.md` (e.g., `07_1_chunked_transfer_coding.md` for §7.1)
- [ ] Algorithm tested on one sample RFC (RFC 9112 or RFC 1945) to verify correctness

### TASK-003-003: Build RFC text extraction and ABNF/requirement detection utilities
**Description:** As a developer, I want reusable utilities for parsing RFC text so that the splitting script doesn't contain redundant pattern-matching code.

**Token Estimate:** ~45k tokens
**Predecessors:** TASK-003-002
**Successors:** TASK-003-005
**Parallel:** no — depends on finalized section algorithm from TASK-003-001
**Model:** opus

**Acceptance Criteria:**
- [ ] `extract_raw_rfc_text()` utility reads RFC markdown file, strips BOM (`\uFEFF`), extracts text from inside the `` ``` `` code block, removes page footers (`[Page N]`) and form-feeds
- [ ] `detect_section_headings()` utility returns list of (line_number, section_number, section_title, indent_level) tuples for all section boundaries in the RFC text
- [ ] `detect_abnf_blocks()` utility identifies ABNF grammar regions (consecutive indented lines matching `^   [A-Za-z][A-Za-z0-9-]* = ` or continuation), returns (start_line, end_line, abnf_text)
- [ ] `highlight_requirements()` utility scans prose lines (non-ABNF, non-header) and returns lines that contain ` MUST `, ` MUST NOT `, ` SHALL `, ` SHOULD `, ` SHOULD NOT `, ` MAY ` as whole words
- [ ] Utilities handle edge cases:
  - Multiple spaces/tabs in indentation
  - Lines with "MAY" that are not requirements (e.g., "This MAY be used as a reference")
  - ABNF lines that look like prose (rare)
- [ ] Unit tests written for each utility (test with RFC 9112 excerpts)
- [ ] All utilities exported in a testable module (e.g., `rfc_parse.sh` or equivalent)

### TASK-003-004: Curate RFC metadata (descriptions and tags)
**Description:** As a documentarian, I want to define rich frontmatter metadata for each RFC so that search results surface the most relevant information.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-003-005
**Parallel:** yes — can run alongside TASK-003-001

**Acceptance Criteria:**
- [ ] For each of the 10 RFCs, create curated metadata:
  - **title:** RFC name and number (e.g., "RFC 9112 — HTTP/1.1")
  - **rfc_number:** numeric field (e.g., `9112`)
  - **source_url:** official RFC Editor link
  - **description:** 2–3 sentences highlighting key topics, MUST requirements, compliance score (e.g., "HTTP/1.1 message syntax, chunked transfer encoding, connection management. MUST requirements: Host header, Transfer-Encoding, Content-Length consistency, keep-alive semantics. 91.5/100 notes compliance.")
  - **tags:** 5–8 tags including RFC number, protocol name, and key topics (e.g., `[RFC9112, HTTP/1.1, message-framing, chunked-encoding, connection-management, transfer-coding]`)
  - **aliases:** alternative names (e.g., `["HTTP/1.1 Message Syntax", "RFC 9112"]`)
- [ ] Metadata reviewed against existing vault notes (`notes/RFC/`) to ensure consistency with documented compliance scores and gaps
- [ ] All 10 RFCs have metadata prepared in a structured table or `.json` file for easy injection during splitting

### TASK-003-005: Implement RFC splitting script
**Description:** As a developer, I want a bash script that parses each RFC, applies the splitting algorithm, and outputs per-section markdown files with proper frontmatter in the now-moved `notes/RFC/RFCNNNN/sections/` directories.

**Token Estimate:** ~75k tokens
**Predecessors:** TASK-003-002, TASK-003-003, TASK-003-004
**Successors:** TASK-003-006
**Parallel:** no — depends on utilities and metadata from prior tasks
**Model:** opus

**Acceptance Criteria:**
- [ ] Script `/tmp/rfc_split_adaptive.sh` accepts RFC filename as argument: `bash rfc_split_adaptive.sh RFC9112.md`
- [ ] For each RFC:
  1. Extract raw text and metadata
  2. Detect all section boundaries using `detect_section_headings()`
  3. For each top-level section:
     - Measure line count from section start to next section
     - If ≤ 300 lines: create single section file
     - If > 300 lines: further split at H2 subsections (e.g., "1.1.", "1.2."); if subsection still > 300 lines, further split at H3 (rare)
  4. For each output section file:
     - Generate frontmatter with RFC metadata + section-specific fields (`rfc_section: "7.1"`)
     - Include tags: parent RFC tags + section-specific tags (e.g., "chunked-encoding", "transfer-coding")
     - Format section content with:
       - H2/H3 heading matching section number and title
       - Prose paragraphs fully searchable
       - ABNF blocks wrapped in `` ```abnf ``
       - Requirement sentences blockquoted: `> **MUST**: sentence text` (one blockquote per MUST/SHOULD/MAY sentence)
     - Write to `./notes/RFC/RFCNNNN/sections/NN_SectionTitle.md` (files now in Obsidian vault root)
  5. Log progress: "Processing RFC 9112: [####----] 40% (10/25 sections)"
- [ ] Script handles errors gracefully:
  - Missing BOM stripped without error
  - Malformed section headings logged with line number
  - ABNF detection failures logged but don't halt processing
- [ ] Script tested on all 10 RFCs (dry-run first, then actual file generation)
- [ ] Output directory structure created automatically (`./notes/RFC/RFCNNNN/sections/` if not present)
- [ ] No files deleted; script is idempotent (re-running overwrites previous output)

### TASK-003-006: Generate RFC index files (navigation hubs without section lists)
**Description:** As a developer, I want lightweight index files at `./notes/RFC/RFCNNNN/RFCNNNN.md` that serve as navigation hubs for section discovery via search, not as comprehensive TOCs.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-003-005
**Successors:** TASK-003-007
**Parallel:** no — depends on section file output from TASK-003-004
**Model:** —

**Acceptance Criteria:**
- [ ] For each RFC, create an index file `./notes/RFC/RFCNNNN/RFCNNNN.md` with:
  ```markdown
  ---
  title: "RFC NNNN — Title"
  rfc_number: NNNN
  source_url: "https://www.rfc-editor.org/rfc/rfcNNNN"
  description: "[curated 2–3 sentence description]"
  tags: [RFC####, protocol-name, topic-1, topic-2, ...]
  aliases: ["Alternative Name", "RFC NNNN"]
  ---

  # RFC NNNN — Title

  **Source:** [RFC Editor](https://www.rfc-editor.org/rfc/rfcNNNN) | **Implementation:** `./notes/Protocol/RFC####/` | **Compliance:** NN/100

  ## Overview

  [Brief overview of RFC scope — 3–5 sentences]

  ## Section Files (in Obsidian)

  This RFC has been split into section files for precise MCP search results. All sections are visible in Obsidian under `./notes/RFC/RFCNNNN/sections/`.

  Use `search_notes()` with keywords like:
  - "chunked transfer encoding" → §7.1
  - "MUST Host header" → §3.1
  - Section numbers (e.g., "§5.2") → exact section
  ```
- [ ] Index files are lightweight (no full section list, no 100-line TOC) — they are **navigation hubs**, not reference documents
- [ ] Each index file includes compliance score and link to notes implementation
- [ ] All 10 RFC index files generated

### TASK-003-007: Update notes/RFC/INDEX.md to document split structure
**Description:** As a documentarian, I want to update the master `./notes/RFC/INDEX.md` to explain the new per-section structure and how to search effectively.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-003-006
**Successors:** TASK-003-008
**Parallel:** no — small task, sequential

**Acceptance Criteria:**
- [ ] `./notes/RFC/INDEX.md` updated with:
  - Section explaining the adaptive split structure (per-section files for large RFCs, whole files for small RFCs)
  - Examples of MCP search queries and which RFC section they target
  - Note that all RFC sections are now visible in Obsidian (located in `./notes/RFC/RFCNNNN/sections/`)
  - Link to the split structure (e.g., "See `RFC9112/sections/` for HTTP/1.1 split files")
- [ ] Old "How to Search This RFC" section removed or replaced
- [ ] Index still shows per-RFC metadata table (compliance scores, status, implementation paths)
- [ ] File remains under 150 lines (lightweight reference)

### TASK-003-008: Verify MCP search precision with test queries
**Description:** As a tester, I want to run MCP search queries against the split RFC files in `./notes/RFC/` to verify that search results are precise (section-level, not document-level) and that sections are visible in Obsidian.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-003-007
**Successors:** TASK-003-009
**Parallel:** no — depends on completed split files
**Model:** haiku

**Acceptance Criteria:**
- [ ] Open Obsidian vault (configured to use `./notes/RFC/` as root) and verify all RFC section files are visible
- [ ] Run `search_notes()` queries:
  - Query: `"chunked transfer encoding"` → Result: RFC 9112 §7.1 file, excerpt shows chunked transfer coding definition
  - Query: `"MUST Host header"` → Result: RFC 9112 §3.1 file, excerpt shows blockquoted MUST requirement
  - Query: `"dynamic table HPACK"` → Result: RFC 7541 §2.3 file, excerpt shows dynamic table explanation
  - Query: `"max-age freshness"` → Result: RFC 9111 §4.2 file, excerpt shows freshness calculation
  - Query: `"SETTINGS_MAX_CONCURRENT"` → Result: RFC 9113 §6.5.2 file, excerpt shows SETTINGS frame definition
- [ ] Verify that each search returns the correct section file (not the full RFC document)
- [ ] For queries spanning multiple sections, verify that top 3 results are all relevant sections (not random matches from bloated documents)
- [ ] Document any false negatives (queries that don't return expected sections) and update search guidance in `rfc-specs/INDEX.md` if needed

### TASK-003-009: Clean up, stage changes, and commit
**Description:** As a developer, I want to clean up temporary files and commit the RFC restructuring to git.

**Token Estimate:** ~10k tokens
**Predecessors:** TASK-003-008
**Successors:** none
**Parallel:** no — final stage
**Model:** haiku

**Acceptance Criteria:**
- [ ] Remove any temporary files created during splitting (e.g., `/tmp/rfc_split_adaptive.sh`, intermediate logs)
- [ ] Verify no `.txt` files remain in `rfc-specs/RFCNNNN/` directories (only `.md` files)
- [ ] `git status` shows only new `sections/*.md` files and updated `RFCNNNN.md` index files; no untracked `.txt` or temporary files
- [ ] `git add rfc-specs/` to stage all changes
- [ ] Commit with message:
  ```
  Restructure RFC specifications into per-section markdown files for MCP search precision

  - Split each RFC into adaptive section files (top-level + H2 subsections if > 300 lines)
  - Add rich frontmatter: description, tags, rfc_section metadata
  - Blockquote all MUST/SHOULD/MAY requirements for search prominence
  - Update rfc-specs/INDEX.md with split structure explanation
  - Verified MCP search precision: section-level results, not document-level

  Impact: BM25 search for "chunked transfer encoding" now returns RFC 9112 §7.1 (focused file)
  instead of entire 2500-line RFC.

  Co-Authored-By: Claude Haiku 4.5 <noreply@anthropic.com>
  ```
- [ ] `git log -1` shows the commit and matches the message above
- [ ] `git status` shows clean working tree (no staged or unstaged changes)

---

## Functional Requirements

- **FR-1:** Each RFC > 2000 lines must be split into multiple section files; RFCs ≤ 2000 lines remain whole (adaptive granularity).
- **FR-2:** Section files must preserve the exact original RFC text verbatim — no editorial changes, no paraphrasing.
- **FR-3:** Every section file must have frontmatter with `title`, `rfc_number`, `rfc_section` (e.g., "7.1"), `source_url`, `description`, `tags`, and `aliases`.
- **FR-4:** ABNF grammar blocks must be wrapped in `` ```abnf `` fenced code blocks with language identifier.
- **FR-5:** Requirement sentences (lines containing ` MUST `, ` SHOULD `, ` MAY ` as whole words in prose) must be blockquoted: `> **MUST**: sentence text`.
- **FR-6:** Section filenames must follow pattern `NN_SectionTitle.md` (e.g., `07_1_chunked_transfer_coding.md` for §7.1).
- **FR-7:** MCP `search_notes()` queries must return section-level files (not full RFC documents) as top results.
- **FR-8:** Index files (`rfc-specs/RFCNNNN/RFCNNNN.md`) must be lightweight navigation hubs (no full section lists) and not exceed 150 lines.
- **FR-9:** All changes must be staged and committed to git with appropriate commit message.
- **FR-10:** No temporary or untracked files (`.txt`, logs) remain after completion.

---

## Non-Goals

- **No content modifications:** This is a structural reorganization only; RFC text is never edited or paraphrased.
- **No Obsidian vault changes:** The `notes/` Obsidian vault is not modified; RFC section files are separate from vault notes.
- **No integration with vault notes:** Section files do not link to or reference vault compliance notes (keep separation of concerns).
- **No UI/UX enhancements:** This is backend-only (markdown restructuring); no Obsidian themes, CSS, or custom rendering.
- **No version-specific optimizations:** All RFCs use the same splitting algorithm; no RFC-specific exceptions (except for adaptive 300-line threshold).

---

## Design Considerations

**Search Optimization (MCP):**
- Rich frontmatter ensures that `search_notes(..., searchFrontmatter: true)` finds RFCs by metadata (RFC number, description keywords)
- Section-level files mean BM25 scoring doesn't dilute results across 10,000 irrelevant lines
- Tag-based discovery: `search_notes("#RFC9112")` finds all HTTP/1.1 sections
- Blockquoted requirements boost BM25 term frequency for MUST/SHOULD/MAY keywords

**Markdown Format:**
- No custom syntax; uses standard markdown + code fences
- Section headings (H2/H3) provide implicit structure for tools
- Blockquotes are standard markdown; highly visible in any renderer

**File Naming:**
- Leading zero-padded numbers (`07_1_...`) ensure alphabetical sort matches RFC section order
- Underscores separate section number from title (e.g., `07_1_chunked_transfer_coding.md`)

**Backward Compatibility:**
- Existing `rfc-specs/INDEX.md` and RFC index files remain valid
- Old monolithic RFC files are replaced; no parallel versions
- Obsidian vault (if configured) can point to `rfc-specs/` without changes

---

## Technical Considerations

**Parsing Challenges:**
- RFC plain-text format varies slightly between documents (spacing, indentation). Regex patterns must be tested against all 10 RFCs.
- ABNF blocks are indented; detecting boundaries requires lookahead/lookbehind logic to avoid false positives.
- Some RFCs have non-ASCII characters (e.g., em-dashes, non-breaking spaces) — must preserve UTF-8 encoding.

**Large File Processing:**
- RFC 9110 (~10,800 lines) and RFC 9000 (~8,500 lines) require efficient line-by-line processing; avoid loading entire file into memory multiple times.
- Bash script may need optimization (consider `awk` or compiled binary for very large files).

**UTF-8 BOM Handling:**
- RFC 9112 and RFC 9111 have a UTF-8 BOM (`\uFEFF`) at the start of the code block. Must be stripped before processing, or it will appear in output files.

**Edge Cases:**
- Appendices (RFC format: "Appendix A", "Appendix B") — treated as top-level sections.
- Normative vs. Informative references — both must be preserved as part of their parent section.
- Security Considerations sections — large in some RFCs; split adaptively if > 300 lines.

---

## Success Metrics

1. **Search Precision:** `search_notes()` queries return section-level files for top results (not full RFC documents).
2. **File Granularity:** Large RFCs split into 15–30 section files; small RFCs kept whole.
3. **Metadata Completeness:** 100% of section files have frontmatter with `rfc_number`, `rfc_section`, `tags`.
4. **Requirement Coverage:** ≥ 95% of MUST/SHOULD/MAY sentences blockquoted correctly.
5. **Git Clean:** Final commit includes all split files and updated indices; no temporary files.

---

## Open Questions

None — user responses clarified all ambiguities (1A, 2D, 3A, 4A, 5C). Ready to implement.

