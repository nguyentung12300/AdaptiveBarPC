# Contributing to AdaptiveBarPC

Thanks for helping improve AdaptiveBarPC. Contributions should preserve the product invariants below and include focused tests and documentation for behavior or profile-format changes.

## Development requirements

- Windows 10 or 11
- .NET 8 SDK
- Git

AdaptiveBarPC uses WPF and Windows APIs, so build and test it on Windows.

## Build and test

Run these commands from the repository root:

~~~powershell
dotnet restore AdaptiveBarPC.sln
dotnet build AdaptiveBarPC.sln --configuration Release
dotnet test AdaptiveBarPC.sln --no-build --configuration Release
~~~

The test project is tests/AdaptiveBarPC.Tests. The suite currently contains 586 tests; the build and test workflows run the complete solution.

## Architecture

Keep code in its owning layer:

- Models: JSON and data contracts only.
- Controls: reusable touch controls and presentation behavior.
- ViewModels: UI state and commands; no P/Invoke.
- Services: application logic and platform-independent orchestration.
- Interop: Win32 and COM declarations and wrappers.
- Profiles: user-editable JSON layouts.

Keep profile-specific behavior in JSON data. Preserve the non-activating bar window, v0.1 top-level items compatibility, v0.2 pages, and long-press suppression of the primary action when a secondary action runs. Keep shell actions disabled by default and core startup offline.

## Pull requests

1. Open an issue first for substantial behavior changes or new profile/action formats.
2. Create a focused branch and keep the diff limited to one change.
3. Add or update tests and user-facing documentation. Schema changes belong in docs/PROFILE_FORMAT.md and should be additive where practical.
4. Run the build and complete test suite above on Windows.
5. Open a pull request against main. Describe the user impact, implementation, validation performed, and any compatibility or licensing changes.
6. Do not include secrets, local settings, build output, or unrelated screenshots.

GitHub Actions runs the same restore, Release build, and test checks for pushes to main and pull requests targeting main.
