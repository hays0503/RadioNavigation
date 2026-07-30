# code-audit

**description:** Comprehensive automated audit of a C# project — detects anti-patterns via static source analysis without relying on external tools (SonarQube, Roslyn analyzers). 

## When to Use

- Before refactoring: "Audit this code for common issues"
- After large changes: check for regressions in quality metrics  
- During onboarding: get health report of unfamiliar codebase
- As pre-commit gate: optionally run before pushing to catch obvious smell before reviewbot

**Do not use when:** CI already runs static analysis (Sonar, CodeQL) — skip this step and rely on the existing pipeline.

## Input Requirements

- **$ARGUMENTS**: path to project root or source directory
  - Example: `Audit C:\path\to\project`
  - If omitted, uses current working directory
- Project must be standard C# (.csproj based with `.cs` files in known layout)

## Output Format

Always produce structured report matching the format used for the RadioNavigation audit:

```markdown
# [Project Name] Code Audit — Summary

> Date | Root Path | Total .cs Files Scanned | Critical / Major / Minor Counts

## Finding Summary Table

| ID | Category | Severity | Description | File | Lines | Action Needed |
|---|----------|---------|-------------|------|-------|---------------|
1 3...  

## Detailed Findings by Priority

### 🔴 Critical (Must fix before any refactoring)
- **ID**: [name] — Description ... Code excerpt from file:lines. Why this is dangerous. Fix recommendations with code samples. Fies to modify.

---

### 🟡 Minor / Style Improvements  
- Same structure as above...

## Recommendations / Sprint Roadmap
- Priority order with reasoning about dependencies between fixes
```

## Automated Detection Categories (implement each as focused search)

Run these **in parallel** for speed:

### 1. Debug.Log in hot paths → performance leak
- Search pattern: `\.Debug\.(Log|Warning|Error|LogException)` within files containing `[OnAudioFilterRead]|ProcessAudio|Update\(|FixedUpate()` patterns within 30 lines  
- If found → High priority (audio/DSP path is always critical-path in Unity)

### 2. God Objects — classes too many responsibilities
- Count methods and lines per class: `class \w+ : MonoBehaviour|^public (static )?class \w+` with lines > ~150 LOC
- Flag as "Potential God Object" if method count > 8 AND namespace suggests mixed responsibility domains

### 3. Magic Numbers — hardcoded numeric literals without named constants  
- Search: `(\d+\.\d+f|\d+)` NOT inside comments and not within `const\s+\w+\s+=\s+(\d+\.|f)` patterns
- Group by pattern: numbers appearing in 3+ files suggest centralization opportunity

### 4. Null Safety Anti-pattern  
Search: `.IsNull()`, `.IsNotNull()` extension method calls → flag as C# nullable context migration candidate
Count total occurrences across project 

### 5. Reflection Fragility  
Search for `Type.GetType(`, `GetMethod(`, `GetProperty()`, `GetField()` with high number of reflection lookups in single class/method → fragility against update risk when target APIs change silently

### 6. Event Subscription Leaks
- Search pattern: `static event ` without corresponding `-=` unsubscribe calls anywhere in project  
- Unity lifecycle events (Awake/OnDestroy patterns) that don't follow proper subscribe/unsubscribe discipline  

### 7. Test Coverage Gap
- Check for any `[Test]`, `.Test` folder, or test framework references (.csproj PackageReference including `xunit`, `nunit`, `minitest`)

## Process

1. **Discover** source files with glob for the project root → collect .cs file list
2. **Parallel scan** each detection category using grep / read as appropriate  
3. **Aggregate findings** into priority tiers matching existing structure (Critical/Major/Minor) 
4. **Generate** structured markdown report in refactoring-plan/SUMMARY.md format

## Tool Calling Strategy

- Use parallel agents where possible — audio analysis, god object scan, magic number search should run concurrently  
- Prefer grep over read when scanning for patterns across all files
- Use read with limit < 200 lines only when detailed inspection of a hot method is needed  

## Constraints

- Always work read-only — modify nothing except optionally creating new output report under project/.mimocode/ or current working directory 
- Do NOT touch git history, build scripts, or deployment configurations
- If the project uses different language (not C#), adapt category names but keep structure

