SHELL := /bin/bash
ROOT := $(CURDIR)

.PHONY: verify inventory doctor

verify:
	./scripts/verify-env.sh

inventory:
	./scripts/inventory-target.sh

doctor:
	source config/rea/rea.env && rea doctor
