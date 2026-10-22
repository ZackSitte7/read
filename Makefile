SHELL := /bin/bash
ROOT := $(CURDIR)

.PHONY: verify inventory doctor replica replica-run

verify:
	./scripts/verify-env.sh

inventory:
	./scripts/inventory-target.sh

doctor:
	source config/rea/rea.env && rea doctor

replica:
	dotnet build Replica.sln -c Release

replica-run:
	dotnet run --project src/Replica.Simulator -c Release
