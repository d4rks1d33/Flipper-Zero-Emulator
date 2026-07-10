#!/usr/bin/env bash

# install ModelsCompare as a Python package, with runnable script

features=()
options=()
while getopts ":v-:" opt; do
    case $opt in
        v)
            features+=("validate")
            ;;
        u)
            options+=("--user")
            ;;
        -)
            case "$OPTARG" in
                validate)
                    features+=("validate")
                    ;;
                user)
                    options+=("--user")
                    ;;
                *)
                    echo "Invalid option: --$OPTARG" >&2
                    exit 1
                    ;;
            esac
            ;;
        \?)
            echo "Invalid option: -$OPTARG" >&2
            exit 1
            ;;
    esac
done

feature_string=$(IFS=, ; echo "${features[*]}")
if [ -n "$feature_string" ]; then
    feature_string="[$feature_string]"
fi

cd ModelsCompare/
rm -r build dist *.egg-info src/*.egg-info 2> /dev/null || true
if [ -n "$options" ]; then
    pip3 install "${options[*]}" ".$feature_string"
else
    pip3 install ".$feature_string"
fi
