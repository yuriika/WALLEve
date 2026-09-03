#!/bin/bash
set -e

echo "=== WALL-EVE Setup Check ==="

# 1. .NET SDK
if command -v dotnet &> /dev/null; then
    echo "[OK] dotnet found: $(dotnet --version)"
else
    echo "[ERROR] dotnet SDK not found"
    exit 1
fi

# 2. .esi-docs
if [ -d ".esi-docs" ]; then
    echo "[OK] .esi-docs directory present"
else
    echo "[WARN] .esi-docs missing (clone from https://github.com/esi/esi-docs)"
fi

# 3. Local data path
DATA_DIR="$HOME/.local/share/WALLEve/Data"
if [ -d "$DATA_DIR" ]; then
    echo "[OK] App data directory: $DATA_DIR"
    if [ -f "$DATA_DIR/wallet.db" ]; then
        echo "[OK] wallet.db present"
    else
        echo "[WARN] wallet.db missing (will be created on first run)"
    fi
    if [ -f "$DATA_DIR/sde.sqlite" ]; then
        echo "[OK] sde.sqlite present"
    else
        echo "[WARN] sde.sqlite missing (download in Settings if needed)"
    fi
else
    echo "[WARN] App data directory missing (will be created on first run)"
fi

# 4. Ports
if lsof -nP -iTCP:5080 -sTCP:LISTEN &> /dev/null; then
    echo "[INFO] Port 5080 is already in use"
fi

# 5. Environment
if [ -f "appsettings.Development.json" ]; then
    echo "[OK] appsettings.Development.json present"
else
    echo "[WARN] appsettings.Development.json missing"
fi

# 6. Project files
if [ -f "WALLEve.csproj" ]; then
    echo "[OK] Project file present"
else
    echo "[ERROR] Project file missing"
    exit 1
fi

echo "=== Setup Check finished ==="
