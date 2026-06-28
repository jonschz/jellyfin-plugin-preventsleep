# Linux specifics

The Linux landscape is vast, and supporting every variation isn't feasible. So this plugin has prioritised support for one of the more common combinations: a system running systemd (or a logind implementation, like elogind), with libdbus installed, and Jellyfin installed on the host.

This plugin may need additional tweaking or fail to work at all on non-standard Linux setups like Fedora Silverblue or NixOS.

To see if the plugin is doing its job, play something in Jellyfin, and run the following in a terminal:
```shell
systemd-inhibit --list | grep "Prevent Sleep Plugin for Jellyfin"
```

If the plugin is working, you will see something similar to the following in the command's output:

> Prevent Sleep Plugin for Jellyfin 963  jellyfin 34866 jellyfin sleep Serving files/waiting for the configured amount of time for further requests block

If you see no such output, look at Jellyfin's logs containing `Jellyfin.Plugin.PreventSleep` in order to pinpoint the issue.

> [!NOTE]
> Sleep may not be reattempted until the entire configured system sleep duration has passed, *starting* from when the Prevent Sleep Plugin has released its sleep inhibitor. To avoid possibly having your computer stay awake for a long while, make sure the *delay before unblocking sleep* Prevent Sleep plugin setting is set to a low value.

## Allowing non-interactive sleep inhibition

> [!NOTE]
> The Polkit rule provided here assumes Jellyfin is being run by a user called `jellyfin`. The [Debian/Ubuntu](https://github.com/jellyfin/jellyfin-packaging/blob/master/debian/conf/jellyfin.service#L8), [Arch Linux](https://gitlab.archlinux.org/archlinux/packaging/packages/jellyfin-server/-/blob/main/sysusers.conf), [Gentoo](https://codeberg.org/gentoo/gentoo/src/branch/master/www-apps/jellyfin-bin/files/jellyfin.service#L6), and [Fedora](https://github.com/rpmfusion/jellyfin/blob/master/jellyfin.service#L8) Jellyfin packages create a dedicated `jellyfin` user to run Jellyfin as. If the rule here is not suitable for your system, please read [`polkit(8)`](https://www.freedesktop.org/software/polkit/docs/latest/polkit.8.html#polkit-rules-polkit) to adapt it.

If your computer is still suspending while playing something in Jellyfin and you are seeing errors in your Jellyfin log similar to

> System.UnauthorizedAccessException: Preventing sleep requires authentication.

you will need to perform the following to allow the plugin to inhibit sleep without requiring authentication:

1. Run this in a Terminal:
```shell
sudo nano /etc/polkit-1/rules.d/10-jellyfin-preventsleep-plugin.rules
```

2. Paste the following in, save, and quit:
```js
polkit.addRule(function(action, subject) {
    if (action.id == "org.freedesktop.login1.inhibit-block-sleep" && subject.user == "jellyfin") {
        return polkit.Result.YES;
    }
});
```

3. Restart your Jellyfin server:
```shell
sudo systemctl restart jellyfin
```

## Docker support

While not tested, it may be possible to have this plugin work in a Docker container with a Linux host meeting the other requirements listed above if you install libdbus in the container and forward the host's `/run/dbus/system_bus_socket` into it.

> [!WARNING]
> This may allow malicious programs running in the container to exercise greater control over the host.
