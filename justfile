set shell := ["bash", "-euo", "pipefail", "-c"]

solution := "BlogWatcher.slnx"

# 利用可能なタスクを表示する
default:
    @just --list

# .NET、Nix、justfileを並列で整形する
[parallel]
format: dotnet-format nix-format just-format

# .NET、Nix、justfileのフォーマットを並列で検証する
[parallel]
format-check: dotnet-format-check nix-format-check just-format-check

# Roslyn Analyzerとactionlintを並列で実行する
[parallel]
lint: dotnet-lint actions-lint

# 全formatterとlinterを並列で検証する
[parallel]
check: dotnet-format-check nix-format-check just-format-check dotnet-lint actions-lint

# .NETソースを整形する
[private]
dotnet-format:
    dotnet format {{ solution }} --no-restore

# .NETソースのフォーマットを検証する
[private]
dotnet-format-check:
    dotnet format {{ solution }} --no-restore --verify-no-changes

# Nixソースを整形する
[private]
nix-format:
    git ls-files -z '*.nix' | xargs -0 nixfmt

# Nixソースのフォーマットを検証する
[private]
nix-format-check:
    git ls-files -z '*.nix' | xargs -0 nixfmt --check

# justfileを整形する
[private]
just-format:
    just --fmt

# justfileのフォーマットを検証する
[private]
just-format-check:
    just --fmt --check

# Roslyn Analyzerを有効にしてビルドする
[private]
dotnet-lint:
    dotnet build {{ solution }} --no-restore --configuration Release

# GitHub Actions Workflowを検証する
[private]
actions-lint:
    actionlint
