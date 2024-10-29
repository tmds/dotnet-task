#!/usr/bin/env bash

set -euo pipefail

declare -a PARAM_BUILD_PROPS
parsing_flag=""
for arg in "$@"; do
  if [[ "$arg" == "--env-vars" ]] || [[ "$arg" == "--build-props" ]]; then
    parsing_flag="${arg:2}"
  elif [[ "$parsing_flag" == "env-vars" ]]; then
    export "${arg?}"
  elif [[ "$parsing_flag" == "build-props" ]]; then
    if [[ "$arg" == *\;* ]] && [[ "$arg" != *=\"*\" ]]; then
      echo "error: Invalid BUILD_PROPS property: '""$arg""'." >&2
      echo "To assign a list of values, the values must be enclosed with double quotes. For example: MyProperty=\"Value1;Value2\"." >&2
      exit 1
    fi
    PARAM_BUILD_PROPS+=("$arg")
  fi
done

if [[ "$WORKSPACE_DOCKERCONFIG_BOUND" == "true" ]]; then
  mkdir -p ~/.config/containers
  [[ -f "$WORKSPACE_DOCKERCONFIG_PATH/config.json" ]] && [[ -f "$WORKSPACE_DOCKERCONFIG_PATH/.dockerconfigjson" ]] && \
    echo "error: 'dockerconfig' workspace provides multiple config files." >&2 && \
    echo "The config must provided using a single '.dockerconfigjson' or a single 'config.json' file." >&2 && \
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

SDK_IMAGE="$PARAM_SDK_IMAGE"
SDK_IMAGE_REPOSITORY="${SDK_IMAGE%%/*}"
SDK_IMAGE_NAMESPACE="${SDK_IMAGE#*/}"
SDK_IMAGE_NAMESPACE="${SDK_IMAGE_NAMESPACE%/*}"

BASE_IMAGE="$PARAM_BASE_IMAGE"
if [[ -n "$BASE_IMAGE" ]]; then
  # If the name includes no repository, use the SDK image repository.
  if [[ "${BASE_IMAGE%%/*}" != *.* ]]; then
    # If the name has no path component, use the SDK image namespace.
    if [[ "$BASE_IMAGE" != */* ]]; then
      BASE_IMAGE="$SDK_IMAGE_NAMESPACE/$BASE_IMAGE"
    fi
    BASE_IMAGE="$SDK_IMAGE_REPOSITORY/$BASE_IMAGE"
  fi
fi

# Support short names for pushing to the internal registry.
IMAGE_NAME="$PARAM_IMAGE_NAME"
# If the name includes no repository, use the SDK image repository.
if [[ "${IMAGE_NAME%%/*}" != *.* ]]; then
  # If the name has no path component, use the current namespace.
  if [[ "$IMAGE_NAME" != */* ]]; then
    IMAGE_NAME="$CurrentKubernetesNamespace/$IMAGE_NAME"
  fi
  IMAGE_NAME="$SDK_IMAGE_REPOSITORY/$IMAGE_NAME"
fi

# Determine properties used by the .NET SDK container tooling.
# Extract the repository
ContainerRegistry="${IMAGE_NAME%%/*}"
ContainerRepository="${IMAGE_NAME#*/}"
ContainerImageTag="latest"
# Extract the tag (if there is one)
if [[ "$ContainerRepository" == *:* ]]; then
  ContainerImageTag="${ContainerRepository##*:}"
  ContainerRepository="${ContainerRepository%:*}"
fi

if [[ -n "$BASE_IMAGE" ]]; then
cat >/tmp/OverrideBaseImage.targets <<'EOF'
<Project>
  <Target Name="ComputeOverrideBaseImage" BeforeTargets="ComputeContainerBaseImage">
    <PropertyGroup>
      <ContainerBaseImage>$(BASE_IMAGE)</ContainerBaseImage>
      <!-- If no tag was specified, use the target framework version as the tag. -->
      <ContainerBaseImage Condition="!$(ContainerBaseImage.Substring($(ContainerBaseImage.LastIndexOf('/'))).Contains(':'))"
      >$(ContainerBaseImage):$(_TargetFrameworkVersionWithoutV)</ContainerBaseImage>
    </PropertyGroup>
  </Target>
</Project>
EOF
fi

declare -a PUBLISH_ARGS
PUBLISH_ARGS+=( "${PARAM_BUILD_PROPS[@]/#/-p:}" )
PUBLISH_ARGS+=( "--getProperty:GeneratedContainerDigest" "--getResultOutputFile:/tmp/IMAGE_DIGEST" )
PUBLISH_ARGS+=( "-v" "$PARAM_VERBOSITY" )
PUBLISH_ARGS+=( "-p:ContainerRegistry=$ContainerRegistry" "-p:ContainerRepository=$ContainerRepository" -p:ContainerImageTag= "-p:ContainerImageTags=$ContainerImageTag" )
if [[ -n "$BASE_IMAGE" ]]; then
  PUBLISH_ARGS+=( "-p:CustomBeforeDirectoryBuildProps=/tmp/OverrideBaseImage.targets" "-p:BASE_IMAGE=$BASE_IMAGE" )
fi
PUBLISH_ARGS+=( "/t:PublishContainer" )
PUBLISH_ARGS+=( "$PARAM_PROJECT" )
dotnet publish  "${PUBLISH_ARGS[@]}"

RESULT_IMAGE_DIGEST=$(cat /tmp/IMAGE_DIGEST)
RESULT_IMAGE="$ContainerRegistry/$ContainerRepository@$RESULT_IMAGE_DIGEST"

printf "%s" "$RESULT_IMAGE_DIGEST" >/tekton/results/IMAGE_DIGEST
printf "%s" "$RESULT_IMAGE" >/tekton/results/IMAGE
