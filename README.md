# Prevent Sleep Plugin for Jellyfin

## About

This plugin for [Jellyfin](https://jellyfin.org/) prevents the server from entering sleep mode whenever there are active streams.

At this time, this plugin is not part of the Jellyfin project. Use at your own risk.

## Installation

Compiled binaries can be downloaded [here](https://github.com/jonschz/jellyfin-plugin-preventsleep/releases). Note that the most recent release only works with Jellyfin 10.9 or newer, and Jellyfin does not [automatically update itself](https://github.com/jellyfin/jellyfin-server-windows/issues/30). If in doubt, [update](https://github.com/jellyfin/jellyfin/releases) your Jellyfin installation first.

To install the compiled binary, download the `.dll`, go to your plugin folder (see [here](https://jellyfin.org/docs/general/server/plugins/)), create a subfolder `Jellyfin.Plugin.PreventSleep`, and paste the `.dll` inside. After a restart of Jellyfin, the plugin should be enabled.

To build this plugin yourself, follow e.g. [these steps](https://github.com/jellyfin/jellyfin-plugin-trakt/blob/master/README.md#Build). For debugging, see [here](https://github.com/jellyfin/jellyfin-plugin-template/blob/master/README.md).

## Known issues


- As of now, only Windows is supported for the OS of the server.
- This plugin might not work correctly if the server is capable of connected standby, see https://superuser.com/a/1287544. Testing would be greatly appreciated.

Please don't hesistate to report issues if you find any.

## Contributing
Every kind of help is welcome! Feel free to test the plugin, report issues, or open a pull request.

## License

This plugin's code and packages are distributed under the GPLv3 License. See [LICENSE](./LICENSE) for more information.
