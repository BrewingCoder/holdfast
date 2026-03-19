#!/bin/bash

telemetryFile=".telemetry"

echo 'Welcome to HoldFast!'
echo 'Thanks for helping improve the HoldFast open-source community.'
echo 'To know how folks are self-hosting HoldFast so that we can improve the product, we collect metrics about your usage.'

read -p 'Press enter to start HoldFast.'
touch $telemetryFile
