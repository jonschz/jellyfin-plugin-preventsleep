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

Compiled binaries can be downloaded [here](https://github.com/jonschz/jellyfin-plugin-preventsleep/releases). Note that the most recent release only works with Jellyfin 10.9 or newer, and Jellyfin does not [automatically update itself](https://github.com/jellyfin/jellyfin-server-windows/issues/30). If you run into issues, check your Jellyfin version first.

To install the compiled binary, download the `.dll`, go to your plugin folder (see [here](https://jellyfin.org/docs/general/server/plugins/)), create a subfolder `Jellyfin.Plugin.PreventSleep`, and paste the `.dll` inside. After a restart of Jellyfin, the plugin should be enabled.

## Known issues

- As of now, only Windows is supported for the OS of the server.
- This plugin might not work correctly if the server is capable of connected standby, see <https://superuser.com/a/1287544>. Testing would be greatly appreciated.

Please don't hesistate to report issues if you find any.

## Contributing

Every kind of help is welcome! Feel free to test the plugin, report issues, or open a pull request.

To build this plugin yourself, follow e.g. [these steps](https://github.com/jellyfin/jellyfin-plugin-trakt/blob/master/README.md#Build). For debugging, see [this manual](https://github.com/jellyfin/jellyfin-plugin-template/blob/master/README.md).

## License

This plugin's code and packages are distributed under the GPLv3 License. See [LICENSE](./LICENSE) for more information.
