$ErrorActionPreference = 'Stop'

# The shared generator keeps Windows frames in sync with the other icon assets.
python (Join-Path $PSScriptRoot '../icons/generate.py')
exit $LASTEXITCODE
