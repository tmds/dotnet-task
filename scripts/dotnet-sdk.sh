#!/usr/bin/env bash

set -euo pipefail

parsing_flag=""
for arg in "$@"; do
    if [[ "$arg" == "--env-vars" ]]; then
        parsing_flag="env-vars"
    elif [[ "$parsing_flag" == "env-vars" ]]; then
        export "${arg?}"
    fi
done

if [[ "$WORKSPACE_DOCKERCONFIG_BOUND" == "true" ]]; then
    mkdir -p ~/.config/containers
    [[ -f "$WORKSPACE_DOCKERCONFIG_PATH/config.json" ]] && [[ -f "$WORKSPACE_DOCKERCONFIG_PATH/.dockerconfigjson" ]] && \
        echo "error: 'dockerconfig' workspace provides multiple config files." >&2 && \
        exit 1
    [[ -f "$WORKSPACE_DOCKERCONFIG_PATH/config.json" ]] && ln -s "$WORKSPACE_DOCKERCONFIG_PATH/config.json" ~/.config/containers/auth.json
    [[ -f "$WORKSPACE_DOCKERCONFIG_PATH/.dockerconfigjson" ]] && ln -s "$WORKSPACE_DOCKERCONFIG_PATH/.dockerconfigjson" ~/.config/containers/auth.json
fi

if [[ "$WORKSPACE_SOURCE_BOUND" == "true" ]]; then
    cd "$WORKSPACE_SOURCE_PATH"
else
    mkdir -p ~/src
    cd ~/src
fi

eval "$PARAM_SCRIPT"