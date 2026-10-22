# Development Workflow, Formatting & Git Standards

---

## 1. Port Allocation (Developer IDE vs AI Agent)

To prevent port collisions when both the developer (in Rider / WebStorm) and the AI agent run servers simultaneously, each workspace uses dedicated ports:

| Workspace                   | Target             | Developer IDE Ports | AI Agent Ports  |
| :-------------------------- | :----------------- | :------------------ | :-------------- |
| **`air-platform` (`demo`)** | Backend / Frontend | `5091` / `4300`     | `5191` / `4400` |
| **`my-port`**               | Backend / Frontend | `5095` / `4305`     | `5195` / `4405` |

- Start or restart agent servers via `make agent-restart` (and stop via `make agent-down`).

---

## 2. Code Formatting & `GlobalUsings.cs`

Every project (`*.csproj`) maintains a `GlobalUsings.cs` file with all platform, third-party, and module namespaces used across the project.
`<GenerateDocumentationFile>true</GenerateDocumentationFile>` is enabled in `Directory.Build.props` so `IDE0005` detects unused or duplicate `using` statements during CLI formatting.

Before every commit, run:

```bash
make format
```

This executes:

1. `dotnet format` matching `.editorconfig` rules (including removing unused/duplicate `using` directives and sorting `using`s).
2. `npx prettier --write` across TypeScript, HTML, CSS, and JSON files in the web workspace.

---

## 3. JetBrains MCP Integration

When Rider and WebStorm are open with MCP enabled:

- Verify readiness via `make mcp-check`.
- Prefer JetBrains MCP tools for file inspection, symbol search, refactoring, and live IDE diagnostics (`get_file_problems`).

---

## 4. Git Commit & Hygiene Rules

1. **Zero Absolute Paths**:
   - Never commit absolute machine or user home directory paths in any file.
   - Enforced automatically by `.githooks/pre-commit` and `make check-paths`.
2. **Commit Message Format**:
   - All commit messages must be in **English** and start with a lowercase type prefix:
     ```text
     <type>: <imperative summary in up to 80 chars>
     ```
   - Allowed `<type>` values:
     - `add`: new feature, capability, or file
     - `upd`: change or refinement to existing behavior or structure
     - `del`: removal of code, files, or features
     - `fix`: bug fix
     - `style`: formatting-only change
     - `chore`: build, dependencies, tooling, or documentation
   - Optional bullet lines (`- ...`) after a blank line when details add clarity.
   - Never add `Co-Authored-By`, `Signed-off-by`, or tool attribution lines.
   - Enforced automatically by `.githooks/commit-msg`.
