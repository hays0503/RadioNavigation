// graphify OpenCode plugin (v2 API)
//
// opencode v2 (server runtime, >= 2.0) requires plugin modules to default-export
// a definition with an `id` and an `effect`/`setup` function. The old v1 shape
// (named export returning a hooks object, e.g. "tool.execute.before") is
// rejected at load with:
//   Plugin must export a default definition with an id and an effect or setup function.
//
// This plugin prepends a ONE-TIME reminder to the first shell command of the
// session, so the agent reaches for the graphify knowledge graph before
// grepping raw files. The v2 equivalent of the old bash `tool.execute.before`
// hook is `ctx.shell.hook("create.before", ...)`, whose event carries a
// mutable `command` string.
//
// IMPORTANT: keep the reminder string free of backticks and $(...) constructs.
// The hook prepends `echo "<reminder>" ; <cmd>`; backticks inside the
// double-quoted echo trigger shell command substitution, which both corrupts
// tool output and silently executes the very graphify command we are only
// suggesting. Plain words render fine in opencode's TUI.

import { existsSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

// This file always lives at <project>/.opencode/plugins/graphify.js,
// so "../.." resolves to the project root regardless of the server's cwd.
const projectRoot = fileURLToPath(new URL("../..", import.meta.url));
const graphFile = join(projectRoot, "graphify-out", "graph.json");

export default {
  id: "graphify",
  setup: async (ctx) => {
    let reminded = false;

    // `shell` is present in opencode >= 2.0.11; optional chaining keeps the
    // plugin from crashing on older servers that omit the domain.
    ctx.shell?.hook("create.before", (event) => {
      if (reminded || !event) return;
      if (!existsSync(graphFile)) return;

      // ';' not '&&' — Windows PowerShell 5.1 rejects '&&' as a statement
      // separator, breaking the first bash command of the session (#1646).
      event.command =
        'echo "[graphify] knowledge graph at graphify-out/. For focused questions, run graphify query with your question (scoped subgraph, usually much smaller than GRAPH_REPORT.md) instead of grepping raw files. Read GRAPH_REPORT.md only for broad architecture context." ; ' +
        event.command;
      reminded = true;
    });
  },
};