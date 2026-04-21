# Windows specifics

## Power schemes

On Windows machines supporting Modern Standby, a special _Power Scheme_ needs to be used in order to prevent sleep reliably. For example, the default power scheme deletes this plugin's request to prevent sleep after 5 minutes if the machine is running on battery power.

The special power scheme is automatically created and activated when blocking sleep. The previous power scheme is reactivated and the special one is deleted when

- all streams are ended,
- when the Jellyfin server is shut down cleanly,
- or when this plugin is uninstalled from the UI.

However, if Jellyfin is terminated abnormally while a stream is running, the special power scheme remains activated. This is automatically fixed on Jellyfin's next startup (provided this plugin is still installed and active).

If, for some reason, you need to delete the special power scheme manually, do the following:

1. Switch to a different power scheme in the system control panel.
2. Start a terminal with admin permissions (e.g. Win + X -> Terminal (Administrator)).
3. Run `powercfg /L` to get the GUID of the Jellyfin power scheme.
4. Run `powercfg /D <GUID>` with the GUID of the Jellyfin power scheme inserted.

### References

- <https://github.com/jonschz/jellyfin-plugin-preventsleep/issues/18>
- <https://github.com/jonschz/jellyfin-plugin-preventsleep/pull/23>
- <https://stackoverflow.com/a/23505373>
