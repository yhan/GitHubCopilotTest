logman start MyTrace -p "Microsoft-Windows-DotNETRuntime" 0xFFFF -o ETWData.etl -ets

logman stop MyTrace -ets