.PHONY: all help build test clean contracts export-contracts contracts-check format format-check check-paths check-arch check-ts gate audit prepare reset db-prepare db-reset agent-up agent-down agent-restart logs-err mcp-check

VOLUME ?= default
WEB := demo/web
PRETTIER_PATHS := . ../../air-ui/src ../../air-ui/tests ../../docs
AGENT_API_PORT ?= 5191
AGENT_WEB_PORT ?= 4400
AUDIT_CLI ?= auto
AUDIT_MODEL ?= claude-opus-5-5-high
AUDIT_CLAUDE_MODEL ?= claude-fable-5-2
AUDIT_TIMEOUT ?= 300
AUDIT_PROMPT ?= Perform a strict architectural and code review of the current git diff against AGENTS.md (including Section 7 ADR invariants). Do NOT run background tasks or subagents. Report objective bugs/regressions vs controversial design trade-offs concisely.
ABS_PATH_PATTERN := ([/]Volumes[/]|[/]Users[/]|[/]home[/]|[A-Za-z]:\\Users\\)
DAEMON_RUN := python3 -c 'import os, sys, subprocess; r, w = os.pipe(); f = open(sys.argv[1], "wb"); subprocess.Popen(sys.argv[2:], stdin=r, stdout=f, stderr=f, pass_fds=(w,), start_new_session=True)'

all: build

help: ## Show available targets
	@grep -E '^[a-zA-Z0-9_-]+:.*?## ' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-18s\033[0m %s\n", $$1, $$2}'

build: ## Build the entire solution
	dotnet build AirPlatform.slnx

test: ## Run all layered xUnit tests (tests/*) and all air-ui/demo Vitest specs
	dotnet test AirPlatform.slnx --nologo
	@cd $(WEB) && TZ=America/New_York NX_DAEMON=false ./node_modules/.bin/nx test demo

format: ## Format all C# files via dotnet format (.editorconfig) and frontend/docs via Prettier
	@dotnet format AirPlatform.slnx --verbosity minimal && cd $(WEB) && npx prettier --write --log-level warn --config .prettierrc $(PRETTIER_PATHS)

format-check: ## Verify formatting without modifying files
	@dotnet format AirPlatform.slnx --verify-no-changes --verbosity minimal && cd $(WEB) && npx prettier --check --config .prettierrc $(PRETTIER_PATHS)

check-paths: ## Verify zero absolute filesystem paths in tracked files
	@! git grep -nI -E '$(ABS_PATH_PATTERN)'

check-ts: ## Verify TypeScript and Angular template compilation for air-ui and demo
	@cd $(WEB) && npx ngc -p apps/demo/tsconfig.app.json --noEmit

contracts-check: ## Verify generated TypeScript contracts and UI texts match C# definitions without mutating the working tree
	@tmp=$$(mktemp -d); trap 'rm -rf "$$tmp"' EXIT; \
	dotnet run --project src/Air.UiContracts -- "$$tmp" >/dev/null && \
	(cd $(WEB) && npx prettier --write --log-level warn --config .prettierrc --ignore-path /dev/null "$$tmp" >/dev/null) && \
	diff -ru air-ui/src/core/generated "$$tmp"

gate: format-check check-paths check-arch contracts-check check-ts build test ## Run unified verification gate (format, paths, arch, contracts, ts, build, test)

audit: ## Run controlled code audit on Claude Fable 5.2 (High) via claude CLI with automatic fallback to agy CLI
	@had_skills=0; [ -d skills/system ] && had_skills=1; \
	trap 'if [ "$$had_skills" = "0" ]; then rm -rf skills/system && rmdir skills 2>/dev/null || true; fi' EXIT INT TERM; \
	if [ "$(AUDIT_CLI)" = "claude" ]; then \
		perl -e 'alarm $(AUDIT_TIMEOUT); exec @ARGV' claude --model $(AUDIT_CLAUDE_MODEL) --effort high --strict-mcp-config --dangerously-skip-permissions -p "$(AUDIT_PROMPT)"; \
	elif [ "$(AUDIT_CLI)" = "agy" ]; then \
		perl -e 'alarm $(AUDIT_TIMEOUT); exec @ARGV' agy --model $(AUDIT_MODEL) --dangerously-skip-permissions -p "$(AUDIT_PROMPT)"; \
	else \
		perl -e 'alarm $(AUDIT_TIMEOUT); exec @ARGV' claude --model $(AUDIT_CLAUDE_MODEL) --effort high --strict-mcp-config --dangerously-skip-permissions -p "$(AUDIT_PROMPT)" || \
		perl -e 'alarm $(AUDIT_TIMEOUT); exec @ARGV' agy --model $(AUDIT_MODEL) --dangerously-skip-permissions -p "$(AUDIT_PROMPT)"; \
	fi

check-arch: ## Verify Platform.* namespaces, physical folder matching, Contracts/, Internals/, 1 type per file, *Extensions.cs, semantic tokens, TimeProvider, line caps, and .resx parity
	@python3 -c 'import glob, os, re, sys; \
	resolve_ns = lambda n: "Platform." + n[4:] if n.startswith("Air.") else (n.split(".")[0] if n.startswith("Demo") and (n.endswith(".Api") or n.endswith(".Migrations")) and n != "Demo.Api" else n); \
	csprojs = {os.path.dirname(os.path.abspath(p)): (re.search(r"<RootNamespace>([^<]+)</RootNamespace>", open(p, encoding="utf-8").read()).group(1).strip() if re.search(r"<RootNamespace>([^<]+)</RootNamespace>", open(p, encoding="utf-8").read()) else resolve_ns(os.path.splitext(os.path.basename(p))[0])) for p in glob.glob("**/*.csproj", recursive=True)}; \
	exp_ns = lambda cs, d: csprojs[d] if os.path.relpath(os.path.dirname(os.path.abspath(cs)), d) == "." else csprojs[d] + "." + os.path.relpath(os.path.dirname(os.path.abspath(cs)), d).replace(os.sep, "."); \
	cs_cap = lambda cs: 750 if cs.startswith("src/Air.Api/") else (950 if cs.startswith("src/Air.App/") else 450); \
	pal_re = re.compile(r"\b(?:dark:|(?:text|bg|border|ring|from|to|via)-(?:slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)-\d+)"); \
	errs = []; \
	[errs.extend([f"{cs}: forbidden Air namespace"] if re.search(r"^\s*namespace\s+Air\b", txt, re.M) else []) or \
	 errs.extend([f"{cs}: namespace {m.group(1)} != expected {exp_ns(cs, max([d for d in csprojs if os.path.abspath(cs).startswith(d + os.sep)], key=len))}" for m in [re.search(r"^\s*namespace\s+([A-Za-z0-9_.]+)\s*;", txt, re.M)] if m and [d for d in csprojs if os.path.abspath(cs).startswith(d + os.sep)] and m.group(1) != exp_ns(cs, max([d for d in csprojs if os.path.abspath(cs).startswith(d + os.sep)], key=len))]) or \
	 errs.extend([f"{cs}: multiple top-level types" for t in [set(re.findall(r"^(?:public|internal|file)\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+)*(?:class|record|struct|interface|enum)\s+([A-Za-z0-9_]+)", txt, re.M))] if "/Migrations/" not in cs and len(t) > 1]) or \
	 errs.extend([f"{cs}: extension method in non-Extensions file"] if re.search(r"static\s+[^\(\n]+\(\s*this\s+", txt) and not cs.endswith("Extensions.cs") else []) or \
	 errs.extend([f"{cs}: internal type must be in Internals/"] if cs.startswith("src/") and re.search(r"^internal\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+)*(?:class|record|struct|interface|enum)\s+", txt, re.M) and "/Internals/" not in cs else []) or \
	 errs.extend([f"{cs}: public type must not be in Internals/"] if (cs.startswith("src/") or cs.startswith("demo/src/")) and "/Internals/" in cs and re.search(r"^public\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+)*(?:class|record|struct|interface|enum)\s+", txt, re.M) else []) or \
	 errs.extend([f"{cs}: contract type must be in Contracts/"] if cs.startswith("src/") and re.search(r"^public\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+)*(?:class|record|struct|interface|enum)\s+[A-Za-z0-9_]+(?:Dto|Request)\b", txt, re.M) and "/Contracts/" not in cs else []) or \
	 errs.extend([f"{cs}: forbidden domain hardcoding {m.group(0)}" for m in [re.search(r"\"(?:mccCode|DocStatus|counterpartyId|toAccountId)\"|--dangerously-skip-permissions|Password123", txt, re.I)] if (cs.startswith("src/") or "Password123" in txt) and m]) or \
	 errs.extend([f"{cs}: [AllowAnonymous] is forbidden on GridService/FormService; use [WebhookEndpoint] with explicit auth"] if cs.endswith(("GridService.cs", "FormService.cs")) and re.search(r"\[\s*AllowAnonymous\b", txt) else []) or \
	 errs.extend([f"{cs}: use GetParent()/GetHooks() instead of new Parent()/new Hooks()"] if cs.startswith("src/") and os.path.basename(cs) not in ("FormService.cs", "GridService.cs") and re.search(r"new\s+(?:Parent|Hooks)\s*\(\s*\)", txt) else []) or \
	 errs.extend([f"{cs}: use TimeProvider / Time.Today / Time.UtcNow instead of raw DateTime/DateTimeOffset clock reads"] if (cs.startswith(("src/Air.App/", "src/Air.Mcp/", "src/Air.Agent/", "src/Air.UiContracts/", "demo/src/")) and "/Migrations/" not in cs and "/Demo.DevMigrations/" not in cs) and re.search(r"\bDateTime(?:Offset)?\.(?:UtcNow|Now|Today)\b", txt) else []) or \
	 errs.extend([f"{cs}: forbidden Air.* string literal in platform source" ] if cs.startswith("src/") and re.search(r"\"Air\.", txt) else []) or \
	 errs.extend([f"{cs}: exceeds {cs_cap(cs)} lines ({txt.count(chr(10)) + 1})"] if (cs.startswith("src/") or cs.startswith("demo/src/")) and "/Migrations/" not in cs and txt.count(chr(10)) + 1 > cs_cap(cs) else []) \
	 for cs in sorted(glob.glob("**/*.cs", recursive=True)) if "/obj/" not in cs and "/bin/" not in cs and not cs.endswith(".Designer.cs") and os.path.basename(cs) not in ("GlobalUsings.cs", "Program.cs") for txt in [open(cs, encoding="utf-8").read()]]; \
	[errs.extend([f"{ts}: exceeds 450 lines ({ttxt.count(chr(10)) + 1})"] if ts.startswith("air-ui/src/") and "/generated/" not in ts and ttxt.count(chr(10)) + 1 > 450 else []) or \
	 errs.extend([f"{ts}: use newUuid() instead of raw crypto.randomUUID"] if os.path.basename(ts) != "uuid.ts" and "crypto.randomUUID" in ttxt else []) or \
	 errs.extend([f"{ts}: forbidden bypassSecurityTrust* usage (use native DOM sanitization or structured UI blocks)"] if "bypassSecurityTrust" in ttxt else []) or \
	 errs.extend([f"{ts}: forbidden non-semantic Tailwind palette or dark: class ({m.group(0)})" for m in [pal_re.search(ttxt)] if os.path.basename(ts) != "ui-color.ts" and "/generated/" not in ts and m]) \
	 for ts in sorted(glob.glob("air-ui/src/**/*.ts", recursive=True) + glob.glob("demo/web/apps/**/*.ts", recursive=True)) if not ts.endswith(".spec.ts") and not ts.endswith("contracts.ts") for ttxt in [open(ts, encoding="utf-8").read()]]; \
	[errs.extend([f"{gu}: forbidden .Internals in GlobalUsings.cs (use local using in .cs files)"] if re.search(r"^global\s+using\s+.*\.Internals\b", open(gu, encoding="utf-8").read(), re.M) else []) for gu in sorted(glob.glob("**/GlobalUsings.cs", recursive=True)) if "/obj/" not in gu and "/bin/" not in gu]; \
	[errs.extend([f"{rx}: missing in .uk.resx: {sorted(b - u)}" for b, u in [(set(re.findall(r"<data\s+name=\"([^\"]+)\"", open(rx, encoding="utf-8").read())), set(re.findall(r"<data\s+name=\"([^\"]+)\"", open(rx[:-5] + ".uk.resx", encoding="utf-8").read())))] if b - u]) or \
	 errs.extend([f"{rx}: missing in neutral .resx: {sorted(u - b)}" for b, u in [(set(re.findall(r"<data\s+name=\"([^\"]+)\"", open(rx, encoding="utf-8").read())), set(re.findall(r"<data\s+name=\"([^\"]+)\"", open(rx[:-5] + ".uk.resx", encoding="utf-8").read())))] if u - b]) or \
	 errs.extend([f"{rx}: Cyrillic text in neutral .resx (must be English; put Ukrainian in .uk.resx)"] if re.search(r"<data\s+name=\"(?!Ukrainian\")[^\"]+\"[^>]*>\s*<value>[^<]*[\u0400-\u04FF]", open(rx, encoding="utf-8").read()) else []) \
	 for rx in sorted(glob.glob("**/*.resx", recursive=True)) if not rx.endswith(".uk.resx") and os.path.exists(rx[:-5] + ".uk.resx")]; \
	ui_keys = set(re.findall(r"^\s*([A-Za-z0-9_]+)\s*:", open("air-ui/src/ui-components.ts", encoding="utf-8").read(), re.M)); \
	[errs.extend([f"{cs}: ComponentModel kind {(km.group(1) if km else m.group(1))} missing in air-ui/src/ui-components.ts" for m in [re.search(r"public\s+(?:sealed\s+)?class\s+([A-Za-z0-9_]+)Model\s*:\s*ComponentModel\b", txt)] if m for km in [re.search(r"override\s+string\s+Kind\s*=>\s*\"([^\"]+)\"", txt)] if (km.group(1) if km else m.group(1)) not in ui_keys]) for cs in sorted(glob.glob("src/Air.App/**/*.cs", recursive=True)) if "/obj/" not in cs and "/bin/" not in cs for txt in [open(cs, encoding="utf-8").read()]]; \
	ext_app_re = re.compile(r"\b(?:(?!Demo|Platform|Air|Base|Test|Mock|App|Org|Ecom|Module|Configure|Web|Ui|Dev|Create|Add|Get|Set|Map|Use|Host|Client|InMemory|Sample)[A-Z][a-z0-9]+(?:App|Org|Ecom|Fin|DbContext|Migrations|Transfer)|509[2-9]|519[2-9]|430[1-9]|440[1-9])\b"); \
	[errs.extend([f"{f}: forbidden external application reference ({m.group(0)})"] if m else []) for f in sorted(set(glob.glob("src/**/*.*", recursive=True) + glob.glob("air-ui/src/**/*.*", recursive=True) + glob.glob("docs/**/*.*", recursive=True) + glob.glob("tests/**/*.*", recursive=True) + ["AGENTS.md", "Makefile"])) if os.path.isfile(f) and "/obj/" not in f and "/bin/" not in f for m in [ext_app_re.search(open(f, encoding="utf-8", errors="ignore").read())]]; \
	[errs.extend([f"{md}: stale RegisterDomainKnowledge reference in docs (use PlatformModule.Docs)"] if ("RegisterDomain" + "Knowledge") in open(md, encoding="utf-8").read() else []) for md in sorted(glob.glob("docs/**/*.md", recursive=True))]; \
	print("\n".join(errs)) if errs else None; sys.exit(1 if errs else 0)'

export-contracts: ## Export TypeScript contracts and localized UI texts
	dotnet run --project src/Air.UiContracts -- air-ui/src/core/generated
	@cd $(WEB) && npx prettier --write --log-level warn --config .prettierrc ../../air-ui/src/core/generated

contracts: export-contracts

db-prepare: ## Prepare and seed the demo database
	dotnet run --project demo/src/Demo.DevMigrations -- prepare --volume $(VOLUME)

prepare: db-prepare

db-reset: ## Drop, recreate, and seed the demo database
	dotnet run --project demo/src/Demo.DevMigrations -- reset --volume $(VOLUME)

reset: db-reset

agent-up: ## Start Demo backend (5191) and frontend (4400) on dedicated agent ports
	@$(DAEMON_RUN) /tmp/air-platform-agent-api.log dotnet run --project demo/src/Demo.Api --urls http://localhost:$(AGENT_API_PORT)
	@cd $(WEB) && DEMO_API_URL=http://localhost:$(AGENT_API_PORT) $(DAEMON_RUN) /tmp/air-platform-agent-web.log ./node_modules/.bin/nx serve demo --configuration=agent --port $(AGENT_WEB_PORT)
	@for i in $$(seq 1 120); do \
		if lsof -iTCP:$(AGENT_API_PORT) -sTCP:LISTEN -P -n >/dev/null 2>&1 && lsof -iTCP:$(AGENT_WEB_PORT) -sTCP:LISTEN -P -n >/dev/null 2>&1; then \
			echo "Agent Demo servers ready: Backend http://localhost:$(AGENT_API_PORT) | Frontend http://localhost:$(AGENT_WEB_PORT)"; \
			exit 0; \
		fi; \
		sleep 0.5; \
	done; \
	echo "ERROR: Agent Demo servers failed to bind on $(AGENT_API_PORT)/$(AGENT_WEB_PORT)"; \
	tail -n 25 /tmp/air-platform-agent-api.log /tmp/air-platform-agent-web.log 2>/dev/null || true; \
	exit 1

agent-down: ## Stop Demo backend (5191) and frontend (4400) on dedicated agent ports
	@lsof -ti :$(AGENT_API_PORT) -i :$(AGENT_WEB_PORT) | xargs kill -9 2>/dev/null || true
	@echo "Agent Demo servers stopped ($(AGENT_API_PORT), $(AGENT_WEB_PORT))."

agent-restart: ## Restart Demo backend (5191) and frontend (4400) on dedicated agent ports
	@$(MAKE) --no-print-directory agent-down
	@$(MAKE) --no-print-directory agent-up

logs-err: ## Show recent errors/exceptions from agent backend log
	@grep -E -C 3 "fail:|Exception" /tmp/air-platform-agent-api.log 2>/dev/null | tail -n 60 || echo "No recent errors in /tmp/air-platform-agent-api.log"

mcp-check: ## Check JetBrains Rider (64482) and WebStorm (64542) MCP endpoints
	@lsof -iTCP:64482 -sTCP:LISTEN -P -n >/dev/null 2>&1 && echo "Rider MCP (64482):    [READY]" || echo "Rider MCP (64482):    [OFFLINE]"
	@lsof -iTCP:64542 -sTCP:LISTEN -P -n >/dev/null 2>&1 && echo "WebStorm MCP (64542): [READY]" || echo "WebStorm MCP (64542): [OFFLINE]"

clean: ## Remove bin and obj directories
	find . -type d \( -name "bin" -o -name "obj" \) -exec rm -rf {} +
