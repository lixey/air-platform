.PHONY: all help build clean contracts export-contracts format format-check check-paths prepare reset db-prepare db-reset agent-up agent-down agent-restart mcp-check

VOLUME ?= default
WEB := demo/web
PRETTIER_PATHS := . ../../air-ui/src ../../docs
AGENT_API_PORT ?= 5191
AGENT_WEB_PORT ?= 4400
ABS_PATH_PATTERN := ([/]Volumes[/]|[/]Users[/]|[/]home[/]|[A-Za-z]:\\Users\\)

all: build

help: ## Show available targets
	@grep -E '^[a-zA-Z0-9_-]+:.*?## ' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-18s\033[0m %s\n", $$1, $$2}'

build: ## Build the entire solution
	dotnet build AirPlatform.slnx

format: ## Format all C# files via dotnet format (.editorconfig) and frontend/docs via Prettier
	@dotnet format AirPlatform.slnx --verbosity minimal && cd $(WEB) && npx prettier --write --log-level warn --config .prettierrc $(PRETTIER_PATHS)

format-check: ## Verify formatting without modifying files
	@dotnet format AirPlatform.slnx --verify-no-changes --verbosity minimal && cd $(WEB) && npx prettier --check --config .prettierrc $(PRETTIER_PATHS)

check-paths: ## Verify zero absolute filesystem paths exist in tracked files
	@! git grep -nI -E '$(ABS_PATH_PATTERN)'

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
	@nohup dotnet run --project demo/src/Demo.Api --urls http://localhost:$(AGENT_API_PORT) >/tmp/air-platform-agent-api.log 2>&1 &
	@cd $(WEB) && DEMO_API_URL=http://localhost:$(AGENT_API_PORT) nohup npx nx serve demo --configuration=agent --port $(AGENT_WEB_PORT) >/tmp/air-platform-agent-web.log 2>&1 &
	@echo "Agent Demo servers starting: Backend http://localhost:$(AGENT_API_PORT) | Frontend http://localhost:$(AGENT_WEB_PORT)"

agent-down: ## Stop Demo backend (5191) and frontend (4400) on dedicated agent ports
	@lsof -ti :$(AGENT_API_PORT) -i :$(AGENT_WEB_PORT) | xargs kill -9 2>/dev/null || true
	@echo "Agent Demo servers stopped ($(AGENT_API_PORT), $(AGENT_WEB_PORT))."

agent-restart: ## Restart Demo backend (5191) and frontend (4400) on dedicated agent ports
	@$(MAKE) --no-print-directory agent-down
	@$(MAKE) --no-print-directory agent-up

mcp-check: ## Check JetBrains Rider (64482) and WebStorm (64542) MCP endpoints
	@lsof -iTCP:64482 -sTCP:LISTEN -P -n >/dev/null 2>&1 && echo "Rider MCP (64482):    [READY]" || echo "Rider MCP (64482):    [OFFLINE]"
	@lsof -iTCP:64542 -sTCP:LISTEN -P -n >/dev/null 2>&1 && echo "WebStorm MCP (64542): [READY]" || echo "WebStorm MCP (64542): [OFFLINE]"

clean: ## Remove bin and obj directories
	find . -type d \( -name "bin" -o -name "obj" \) -exec rm -rf {} +
