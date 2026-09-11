# Points git at ci/hooks so the pre-commit gate travels with the repository
# (idempotent; rerun after cloning on a new machine).
$root = (Resolve-Path "$PSScriptRoot/..").Path
git -C $root config core.hooksPath ci/hooks
Write-Host "core.hooksPath -> ci/hooks  (pre-commit gate installed)"
