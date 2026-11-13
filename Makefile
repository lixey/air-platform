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

gate: format-check check-paths contracts-check check-ts build test ## Run unified verification gate (format, paths, contracts, ts, build, and all tests incl. Air.Arch.Tests)

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
	dotnet test tests/Air.Arch.Tests/Air.Arch.Tests.csproj --nologo --verbosity quiet

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
