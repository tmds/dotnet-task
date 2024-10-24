#!/usr/bin/env bats

source ./test/helper/helper.sh

# E2E tests parameters for the test pipeline

# Testing the dotnet task,
@test "[e2e] dotnet task" {
    [ -n "${E2E_DOTNET_PARAMS_URL}" ]
    [ -n "${E2E_DOTNET_PARAMS_REVISION}" ]
    [ -n "${E2E_DOTNET_PARAMS_PROJECT}" ]
    [ -n "${E2E_DOTNET_PARAMS_IMAGE}" ]
    [ -n "${E2E_DOTNET_PARAMS_DOTNET_NAMESPACE}" ]
    [ -n "${E2E_DOTNET_PARAMS_DOTNET_REGISTRY}" ]
    
    run tkn pipeline start dotnet-pipeline \
        --param URL="$E2E_DOTNET_PARAMS_URL" \
        --param REVISION="$E2E_DOTNET_PARAMS_REVISION" \
        --param PROJECT="$E2E_DOTNET_PARAMS_PROJECT" \
        --param IMAGE_NAME="$E2E_DOTNET_PARAMS_IMAGE" \
        --param DOTNET_NAMESPACE="$E2E_DOTNET_PARAMS_DOTNET_NAMESPACE" \
        --param REGISTRY="$E2E_DOTNET_PARAMS_DOTNET_REGISTRY" \
        --workspace="name=source,claimName=dotnet-pvc" \
        --filename=test/e2e/resources/pipeline-dotnet.yaml \
        --showlog
    assert_success

    # waiting a few seconds before asserting results
	sleep 30

    # assering the taskrun status, making sure all steps have been successful
    assert_tekton_resource "pipelinerun" --partial '(Failed: 0, Cancelled 0), Skipped: 0'
}
