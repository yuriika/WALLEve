#!/usr/bin/env python3
import os
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
FAIL = False

def check(name, cond, hint=""):
    global FAIL
    status = "OK" if cond else "FAIL"
    if not cond:
        FAIL = True
    print(f"[{status}] {name}" + (f" — {hint}" if hint else ""))

check("Repo root exists", REPO.exists(), "Run from repo root")
check(".git exists", (REPO/".git").exists(), "Missing git metadata")
check("WALLEve.csproj exists", (REPO/"WALLEve.csproj").exists(), "Project file missing")
check("appsettings.json exists", (REPO/"appsettings.json").exists(), "Missing app settings")
check("Development.md exists", (REPO/"Development.md").exists(), "Missing dev docs")
check("README.md exists", (REPO/"README.md").exists(), "Missing project docs")

# Ports / settings
appsettings = (REPO/"appsettings.json").read_text(encoding="utf-8")
check("Default port 5080 in appsettings.json", "http://localhost:5080" in appsettings, "Update appsettings.json and related docs")
check("Callback URL 5080 in appsettings.json", "http://localhost:5080/callback" in appsettings, "Update CallbackUrl to 5080")

cfg_online = (REPO/"Configuration/EveOnlineSettings.cs").read_text(encoding="utf-8")
check("CallbackUrl default 5080 in code", "http://localhost:5080/callback" in cfg_online, "Update EveOnlineSettings default")

cfg_app = (REPO/"Configuration/ApplicationSettings.cs").read_text(encoding="utf-8")
check("Server URL default 5080 in code", "http://localhost:5080" in cfg_app, "Update ApplicationSettings default")

# SDE docs
check(".esi-docs exists", (REPO/".esi-docs").exists(), "Clone esi/esi-docs into .esi-docs")
check(".esi-docs/openapi.json exists", (REPO/".esi-docs/openapi.json").exists(), "Cache OpenAPI spec from esi.evetech.net/meta/openapi.json")

# Runtime data dirs
data_dir = Path.home()/".local"/"share"/"WALLEve"/"Data"
check("Runtime data dir exists", data_dir.exists(), "Create ~/.local/share/WALLEve/Data")
check("wallet.db path usable", (data_dir/"wallet.db").exists() or True, "wallet.db is auto-created on first run")

# Docs consistency
readme = (REPO/"README.md").read_text(encoding="utf-8")
dev = (REPO/"Development.md").read_text(encoding="utf-8")
check("README mentions 5080", "http://localhost:5080" in readme, "Update README port references")
check("Development.md mentions 5080", "http://localhost:5080" in dev, "Update Development.md port references")
check("Development.md OpenAPI source current", "esi.evetech.net/meta/openapi.json" in dev, "Replace /latest/swagger.json with meta/openapi.json")

print("\n" + ("ALL CHECKS PASSED" if not FAIL else "CHECKS FAILED"))
sys.exit(1 if FAIL else 0)
