# Prevent Sleep Plugin for Jellyfin

## About

This plugin for [Jellyfin](https://jellyfin.org/) prevents the server from entering sleep mode whenever there are active streams.

At this time, this plugin is not part of the Jellyfin project. Use at your own risk.

## Installation

### Via a third-party repository (recommended)

- Jellyfin 10.11: Go to _Administration_ -> _Dashboard_ -> _Plugins_, then click _Manage Repositories_ on the top right.
  - The option to add third-party repositories exists in earlier versions of Jellyfin as well, but the way to get there can be slightly different.
- Click _New Repository_, enter some name like _Prevent Sleep_ and add the following URL:

  ```url
  https://raw.githubusercontent.com/jonschz/jellyfin-plugin-preventsleep/refs/heads/main/manifest.json
  ```

- Close the window and search for _Prevent Sleep_. You should then find the plugin. Click on it and install an appropriate version.
  - You probably want to install the latest version unless you are running a pretty outdated version of Jellyfin; see also the details on the installation page and in the next paragraph.

### Manual installation

Compiled binaries can be downloaded under [Releases](https://github.com/jonschz/jellyfin-plugin-preventsleep/releases). Note that the most recent release only works with Jellyfin 10.9 or newer, and Jellyfin does not [automatically update itself](https://github.com/jellyfin/jellyfin-server-windows/issues/30). If you run into issues, check your Jellyfin version first.

To install the compiled binary, download the `.dll`, go to your [plugin folder](https://jellyfin.org/docs/general/server/plugins/), create a subfolder `Jellyfin.Plugin.PreventSleep`, and paste the `.dll` inside. After a restart of Jellyfin, the plugin should be enabled.

## Known issues and limitations

- Only Jellyfin Server running on Windows is supported. In particular, this plugin will not work on Linux, including Jellyfin docker containers (regardless of the host OS).
- If the server runs Windows and supports Modern Standby (which applies to the vast majority of non-ancient Windows laptops), this plugin makes a small but persistent change to the power scheme in order to function properly (that you are very unlikely to even notice, and are reverted whenever they are not needed). See [this issue](https://github.com/jonschz/jellyfin-plugin-preventsleep/issues/18) and [StackOverflow](https://stackoverflow.com/a/23505373) for more details. However, in order to be sure that no changes made by this plugin remain after uninstallation, make sure that Jellyfin performs a clean shutdown when this plugin is uninstalled.

Please don't hesistate to report issues if you find any.

## Contributing

Every kind of help is welcome! Feel free to test the plugin, report issues, or open a pull request.

To build this plugin yourself, follow e.g. [these steps](https://github.com/jellyfin/jellyfin-plugin-trakt/blob/master/README.md#Build). For debugging, see [this manual](https://github.com/jellyfin/jellyfin-plugin-template/blob/master/README.md).

## License

This plugin's code and packages are distributed under the GPLv3 License. See [LICENSE](./LICENSE) for more information.
