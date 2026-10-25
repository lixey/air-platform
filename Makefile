.PHONY: all help build clean contracts export-contracts format format-check check-paths check-arch check-ts gate prepare reset db-prepare db-reset agent-up agent-down agent-restart logs-err mcp-check

VOLUME ?= default
WEB := demo/web
PRETTIER_PATHS := . ../../air-ui/src ../../docs
AGENT_API_PORT ?= 5191
AGENT_WEB_PORT ?= 4400
ABS_PATH_PATTERN := ([/]Volumes[/]|[/]Users[/]|[/]home[/]|[A-Za-z]:\\Users\\)
DAEMON_RUN := python3 -c 'import os, sys, subprocess; r, w = os.pipe(); f = open(sys.argv[1], "wb"); subprocess.Popen(sys.argv[2:], stdin=r, stdout=f, stderr=f, pass_fds=(w,), start_new_session=True)'

all: build

help: ## Show available targets
	@grep -E '^[a-zA-Z0-9_-]+:.*?## ' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-18s\033[0m %s\n", $$1, $$2}'

build: ## Build the entire solution
	dotnet build AirPlatform.slnx

format: ## Format all C# files via dotnet format (.editorconfig) and frontend/docs via Prettier
	@dotnet format AirPlatform.slnx --verbosity minimal && cd $(WEB) && npx prettier --write --log-level warn --config .prettierrc $(PRETTIER_PATHS)

format-check: ## Verify formatting without modifying files
	@dotnet format AirPlatform.slnx --verify-no-changes --verbosity minimal && cd $(WEB) && npx prettier --check --config .prettierrc $(PRETTIER_PATHS)

check-paths: check-arch ## Verify zero absolute filesystem paths and architectural rules in tracked files
	@! git grep -nI -E '$(ABS_PATH_PATTERN)'

check-ts: ## Verify TypeScript compilation for air-ui and demo
	@cd $(WEB) && npx tsc --noEmit -p apps/demo/tsconfig.app.json

gate: format-check check-paths check-arch check-ts build ## Run unified verification gate (format, paths, arch, ts, build)

check-arch: ## Verify Platform.* namespaces, physical folder matching, Contracts/, Internals/, 1 type per file, *Extensions.cs, and .resx parity
	@python3 -c 'import glob, os, re, sys; \
	resolve_ns = lambda n: "Platform." + n[4:] if n.startswith("Air.") else (n.split(".")[0] if n.startswith("Demo") and (n.endswith(".Api") or n.endswith(".Migrations")) and n != "Demo.Api" else n); \
	csprojs = {os.path.dirname(os.path.abspath(p)): (re.search(r"<RootNamespace>([^<]+)</RootNamespace>", open(p, encoding="utf-8").read()).group(1).strip() if re.search(r"<RootNamespace>([^<]+)</RootNamespace>", open(p, encoding="utf-8").read()) else resolve_ns(os.path.splitext(os.path.basename(p))[0])) for p in glob.glob("**/*.csproj", recursive=True)}; \
	exp_ns = lambda cs, d: csprojs[d] if os.path.relpath(os.path.dirname(os.path.abspath(cs)), d) == "." else csprojs[d] + "." + os.path.relpath(os.path.dirname(os.path.abspath(cs)), d).replace(os.sep, "."); \
	errs = []; \
	[errs.extend([f"{cs}: forbidden Air namespace"] if re.search(r"^\s*namespace\s+Air\b", txt, re.M) else []) or \
	 errs.extend([f"{cs}: namespace {m.group(1)} != expected {exp_ns(cs, max([d for d in csprojs if os.path.abspath(cs).startswith(d + os.sep)], key=len))}" for m in [re.search(r"^\s*namespace\s+([A-Za-z0-9_.]+)\s*;", txt, re.M)] if m and [d for d in csprojs if os.path.abspath(cs).startswith(d + os.sep)] and m.group(1) != exp_ns(cs, max([d for d in csprojs if os.path.abspath(cs).startswith(d + os.sep)], key=len))]) or \
	 errs.extend([f"{cs}: multiple top-level types" for t in [set(re.findall(r"^(?:public|internal|file)\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+)*(?:class|record|struct|interface|enum)\s+([A-Za-z0-9_]+)", txt, re.M))] if "/Migrations/" not in cs and len(t) > 1]) or \
	 errs.extend([f"{cs}: extension method in non-Extensions file"] if re.search(r"static\s+[^\(\n]+\(\s*this\s+", txt) and not cs.endswith("Extensions.cs") else []) or \
	 errs.extend([f"{cs}: internal type must be in Internals/"] if cs.startswith("src/") and re.search(r"^internal\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+)*(?:class|record|struct|interface|enum)\s+", txt, re.M) and "/Internals/" not in cs else []) or \
	 errs.extend([f"{cs}: contract type must be in Contracts/"] if cs.startswith("src/") and re.search(r"^public\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+)*(?:class|record|struct|interface|enum)\s+[A-Za-z0-9_]+(?:Dto|Request)\b", txt, re.M) and "/Contracts/" not in cs else []) or \
	 errs.extend([f"{cs}: forbidden domain hardcoding {m.group(0)}" for m in [re.search(r"\"(?:mccCode|DocStatus|counterpartyId|toAccountId)\"|privat24|--dangerously-skip-permissions", txt, re.I)] if cs.startswith("src/") and m]) or \
	 errs.extend([f"{cs}: [AllowAnonymous] is forbidden on GridService/FormService; use [WebhookEndpoint] with explicit auth"] if cs.endswith(("GridService.cs", "FormService.cs")) and re.search(r"\[\s*AllowAnonymous\b", txt) else []) or \
	 errs.extend([f"{cs}: use GetParent()/GetHooks() instead of new Parent()/new Hooks()"] if cs.startswith("src/") and os.path.basename(cs) not in ("FormService.cs", "GridService.cs") and re.search(r"new\s+(?:Parent|Hooks)\s*\(\s*\)", txt) else []) or \
	 errs.extend([f"{cs}: exceeds 1000 lines ({txt.count(chr(10)) + 1})"] if cs.startswith("src/") and txt.count(chr(10)) + 1 > 1000 else []) \
	 for cs in sorted(glob.glob("**/*.cs", recursive=True)) if "/obj/" not in cs and "/bin/" not in cs and not cs.endswith(".Designer.cs") and os.path.basename(cs) not in ("GlobalUsings.cs", "Program.cs") for txt in [open(cs, encoding="utf-8").read()]]; \
	[errs.extend([f"{ts}: exceeds 1000 lines ({ttxt.count(chr(10)) + 1})"] if ttxt.count(chr(10)) + 1 > 1000 else []) for ts in sorted(glob.glob("air-ui/src/**/*.ts", recursive=True)) if not ts.endswith(".spec.ts") and not ts.endswith("contracts.ts") for ttxt in [open(ts, encoding="utf-8").read()]]; \
	[errs.extend([f"{rx}: missing in .uk.resx: {sorted(b - u)}" for b, u in [(set(re.findall(r"<data\s+name=\"([^\"]+)\"", open(rx, encoding="utf-8").read())), set(re.findall(r"<data\s+name=\"([^\"]+)\"", open(rx[:-5] + ".uk.resx", encoding="utf-8").read())) if os.path.exists(rx[:-5] + ".uk.resx") else set())] if b - u]) for rx in sorted(glob.glob("**/*.resx", recursive=True)) if not rx.endswith(".uk.resx")]; \
	ui_keys = set(re.findall(r"^\s*([A-Za-z0-9_]+)\s*:", open("air-ui/src/ui-components.ts", encoding="utf-8").read(), re.M)); \
	[errs.extend([f"{cs}: ComponentModel kind {(km.group(1) if km else m.group(1))} missing in air-ui/src/ui-components.ts" for m in [re.search(r"public\s+(?:sealed\s+)?class\s+([A-Za-z0-9_]+)Model\s*:\s*ComponentModel\b", txt)] if m for km in [re.search(r"override\s+string\s+Kind\s*=>\s*\"([^\"]+)\"", txt)] if (km.group(1) if km else m.group(1)) not in ui_keys]) for cs in sorted(glob.glob("src/Air.App/**/*.cs", recursive=True)) if "/obj/" not in cs and "/bin/" not in cs for txt in [open(cs, encoding="utf-8").read()]]; \
	print("\n".join(errs)) if errs else None; sys.exit(1 if errs else 0)'

export-contracts: ## Export TypeScript contracts and localized UI texts
	dotnet run --project src/Air.UiContracts -- air-ui/src/core/generated

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
	@echo "Agent Demo servers starting: Backend http://localhost:$(AGENT_API_PORT) | Frontend http://localhost:$(AGENT_WEB_PORT)"

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
