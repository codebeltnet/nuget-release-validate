# Validate NuGet Release Packages from Codebelt

Validates packed `.nupkg` files against the requested release version and confirms that any already-published package bytes on nuget.org are identical. This keeps partial-release retries safe while preserving NuGet's immutable version contract.

> This action is part of the Codebelt umbrella and ensures a consistent way of:
>
> - Defining your CI/CD pipeline
> - Structuring your repository
> - Keeping your codebase small and feasible
> - Writing clean and maintainable code
> - Deploying your code to different environments
> - Automating as much as possible
>
> A paved path to excel as a DevSecOps Engineer.

## Usage

To use this action in your GitHub repository, you can follow these steps:

```yaml
uses: codebeltnet/nuget-release-validate@v1
```

### Inputs

```yaml
with:
  # Directory containing the packed .nupkg files.
  package-directory:
  # Human-selected SemVer release version without the v prefix.
  version:
```

### Outputs

```yaml
outputs:
  # Number of packages validated.
  package-count:
  # Number of package versions not yet present on nuget.org.
  new-package-count:
  # Number of already-published package versions with matching SHA-512 content.
  identical-package-count:
```

The composite action uses PowerShell Core and a .NET 10 file-based app, so it runs on Linux, Windows, and macOS. It requires the .NET 10 SDK or newer, network access to the NuGet v3 flat-container endpoint, and a directory containing the packages to validate. When the SDK is missing, the action fails immediately with an explicit diagnostic. A preceding `codebeltnet/install-dotnet` or `actions/setup-dotnet` step satisfies the SDK prerequisite. It does not publish packages.

## Examples

### Validate packed release artifacts

```yaml
- name: Validate NuGet release packages
  uses: codebeltnet/nuget-release-validate@v1
  with:
    package-directory: ${{ runner.temp }}/.nuget
    version: ${{ needs.build.outputs.version }}
```

## Contributing to Validate NuGet Release Packages from Codebelt

Contributions are welcome! Feel free to submit issues, feature requests, or pull requests to help improve this action.

## License

This project is licensed under the MIT License - see the [LICENSE](../LICENSE) file for details.

> [!TIP]
> To learn more about the Codebelt experience and offerings, visit our [organization page](https://github.com/codebeltnet) on GitHub.
