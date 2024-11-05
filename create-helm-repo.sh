#!/bin/sh

#rm -rf charts

#mkdir charts
#helm package src -d charts
rm charts/index.yaml
helm repo index charts --url https://raw.githubusercontent.com/tmds/dotnet-task/refs/heads/streams/charts