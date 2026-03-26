#!/usr/bin/env node
/**
 * RFC Section File Converter - TASK-026-009
 * Converts RFC section files to proper Markdown per VAULT_STYLE_GUIDE.md
 */

const fs = require('fs');
const path = require('path');

const RFCS = ['RFC1945', 'RFC6265', 'RFC7541', 'RFC9000', 'RFC9110', 'RFC9111', 'RFC9112', 'RFC9113', 'RFC9114', 'RFC9204'];
const BASE = 'notes/RFC';

// RFC 2119 keywords to wrap as callouts (longer first for greedy match)
const RFC_KEYWORDS = [
  'MUST NOT', 'SHALL NOT', 'SHOULD NOT',
  'MUST', 'SHALL', 'SHOULD', 'REQUIRED', 'MAY'
];
const KEYWORD_RE = new RegExp(`\\b(${RFC_KEYWORDS.join('|')})\\b`);

// Pattern for a plain-text heading: section number (N.N+) + short capitalized title
// Requires at least 2 levels (N.N) to avoid matching numbered list items (1. xxx)
const HEADING_RE = /^\s*(\d+(?:\.\d+)+)\.?\s{1,3}([A-Z][A-Za-z][\w\s,\-\/()&':]+)$/;

// Count depth of a section number (e.g., "5.1.1" → 3)
function sectionDepth(secNum) {
  return secNum.split('.').length;
}

// Extract section number from a heading line like "# 5.  Request"
function extractSectionNum(line) {
  const m = line.match(/^#+\s+(\d+(?:\.\d+)*)\.?\s/);
  return m ? m[1] : null;
}

function processFile(filePath, isPreamble) {
  const content = fs.readFileSync(filePath, 'utf8');
  const lines = content.split('\n');
  const changes = [];

  // Find frontmatter end
  let fmEnd = 0;
  if (lines[0].trim() === '---') {
    for (let i = 1; i < lines.length; i++) {
      if (lines[i].trim() === '---') { fmEnd = i + 1; break; }
    }
  }

  // Find H1 heading and its section number
  let h1Idx = -1;
  let h1Line = null;
  let h1SectionNum = null;
  let h1Depth = 0;
  for (let i = fmEnd; i < lines.length; i++) {
    if (lines[i].startsWith('# ') && !lines[i].startsWith('## ')) {
      h1Idx = i;
      h1Line = lines[i];
      h1SectionNum = extractSectionNum(lines[i]);
      h1Depth = h1SectionNum ? sectionDepth(h1SectionNum) : 1;
      break;
    }
  }

  // Pass 1: Remove duplicate title repeat lines (applies to all files)
  if (h1Idx >= 0) {
    for (let i = h1Idx + 1; i < Math.min(h1Idx + 6, lines.length); i++) {
      const stripped = lines[i].trim();
      if (!stripped) continue;
      if (stripped.startsWith('#')) break;
      // Check if this line repeats a section number pattern
      if (/^\d+(\.\d+)*\.?\s{1,3}\S/.test(stripped)) {
        changes.push({ line: i, type: 'dup_title', old: lines[i] });
        lines[i] = '';
      }
      break;
    }
  }

  // Pass 2: Convert plain-text subsection headings and wrap RFC keywords
  let inCode = false;
  // For preamble files, detect TOC region to skip heading conversion there
  let inToc = false;

  for (let i = fmEnd; i < lines.length; i++) {
    const trimmed = lines[i].trim();

    // Track code blocks
    if (trimmed.startsWith('```')) {
      inCode = !inCode;
      continue;
    }
    if (inCode) continue;

    // Track TOC section in preamble files
    if (isPreamble) {
      if (trimmed === 'Table of Contents') { inToc = true; continue; }
      // TOC ends when we hit a non-TOC pattern: a heading, or a long prose line,
      // or "Authors' Addresses", "Acknowledgments", etc.
      if (inToc) {
        if (trimmed === '' || /^\s*\d/.test(lines[i]) || /^Appendix/.test(trimmed)) continue;
        // If we see "Authors' Addresses" or similar end-of-TOC markers, exit TOC
        if (/^(Authors|Acknowledgments|Full Copyright)/.test(trimmed) ||
            (trimmed.length > 60 && !/^\d/.test(trimmed))) {
          inToc = false;
        } else {
          continue; // Still in TOC, skip
        }
      }
    }

    // Skip existing headings and blockquotes
    if (trimmed.startsWith('#') || trimmed.startsWith('>')) continue;

    // Check for plain-text subsection heading (skip in preamble files entirely)
    if (!isPreamble) {
      const headingMatch = trimmed.match(HEADING_RE);
      if (headingMatch && !/\.{3,}/.test(trimmed)) {
        const secNum = headingMatch[1];
        const title = headingMatch[2].trim();
        const secDepth = sectionDepth(secNum);

        // Calculate heading level relative to H1
        let level;
        if (h1Depth > 0) {
          level = secDepth - h1Depth + 1;
        } else {
          level = secDepth;
        }
        level = Math.max(2, Math.min(level, 6));

        const hashes = '#'.repeat(level);
        const newLine = `${hashes} ${secNum}  ${title}`;

        if (lines[i] !== newLine) {
          changes.push({ line: i, type: 'heading', old: lines[i], new: newLine });
          lines[i] = newLine;
        }
        continue;
      }
    }

    // Check for RFC 2119 keywords to wrap as callouts
    if (KEYWORD_RE.test(lines[i])) {
      // Don't process lines already formatted as callouts
      if (lines[i].trimStart().startsWith('>')) continue;
      // Don't process continuation lines of existing callouts
      if (i > 0 && lines[i-1].trimStart().startsWith('>')) continue;
      // Skip definition/boilerplate lines
      if (isDefinitionLine(lines, i)) continue;

      const match = lines[i].match(KEYWORD_RE);
      if (match) {
        const keyword = match[1];
        const lineContent = lines[i].trimStart();
        const newLine = `> **${keyword}**: ${lineContent}`;
        changes.push({ line: i, type: 'keyword', old: lines[i], new: newLine, keyword });
        lines[i] = newLine;
      }
    }
  }

  if (changes.length > 0) {
    fs.writeFileSync(filePath, lines.join('\n'), 'utf8');
  }

  return changes;
}

// Check if a line is in a "conventions" section defining RFC 2119 keywords
function isDefinitionLine(lines, idx) {
  const line = lines[idx];
  // Lines that quote/define the keywords themselves
  if (/[""\u201C\u201D][A-Z]+[""\u201C\u201D]/.test(line)) return true;
  // Lines listing keywords: "MUST", "SHOULD", ...
  if (/[""\u201C\u201D](?:MUST|SHOULD|MAY|SHALL|REQUIRED)[""\u201C\u201D],?\s/.test(line)) return true;
  // The RFC 2119 boilerplate line
  if (/RFC\s*2119/.test(line)) return true;
  if (/BCP\s*14/.test(line)) return true;
  // Lines inside conventions/terminology sections
  for (let i = idx; i >= Math.max(0, idx - 15); i--) {
    if (/^#+.*[Cc]onvention/.test(lines[i]) || /^#+.*[Tt]erminolog/.test(lines[i]) || /^#+.*[Nn]otation/.test(lines[i])) return true;
    if (lines[i].includes('Notational Conventions')) return true;
    if (/^#+/.test(lines[i])) break;
  }
  return false;
}

// Main execution
let totalFiles = 0;
let modifiedFiles = 0;
let totalChanges = { dup_title: 0, heading: 0, keyword: 0 };
const report = [];

for (const rfc of RFCS) {
  const dir = path.join(BASE, rfc, 'sections');
  const files = fs.readdirSync(dir).filter(f => f.endsWith('.md')).sort();

  for (const fn of files) {
    totalFiles++;
    const isPreamble = fn.startsWith('00_');
    const filePath = path.join(dir, fn);
    const changes = processFile(filePath, isPreamble);

    if (changes.length > 0) {
      modifiedFiles++;
      for (const c of changes) {
        totalChanges[c.type] = (totalChanges[c.type] || 0) + 1;
      }
      report.push(`${rfc}/${fn}: ${changes.length} changes (${changes.map(c => c.type).join(', ')})`);
    }
  }
}

console.log(`\n=== Conversion Report ===`);
console.log(`Total files scanned: ${totalFiles}`);
console.log(`Files modified: ${modifiedFiles}`);
console.log(`Changes:`);
console.log(`  Duplicate titles removed: ${totalChanges.dup_title}`);
console.log(`  Plain-text headings converted: ${totalChanges.heading}`);
console.log(`  RFC keyword callouts added: ${totalChanges.keyword}`);
console.log(`\nModified files:`);
report.forEach(r => console.log(`  ${r}`));
