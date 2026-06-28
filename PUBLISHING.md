# Publishing

Since we do not have CI for releases set up yet, the following manual process should be followed:

1. Decide on a new release version (following Semantic Versioning) based the most recent [public releases](https://github.com/jonschz/jellyfin-plugin-preventsleep/releases).
2. Set the new version in [Directory.Build.props](./Directory.Build.props) accordingly.
3. Open a pull request to apply the version changes.
4. On your local machine, create the release using `dotnet publish`. Verify that the version in the DLL file matches the expectations (Windows: Right Click -> Properties).
5. Create a release on GitHub following the pattern of previous releases.
6. Add and push a git tag for the commit that was released.
