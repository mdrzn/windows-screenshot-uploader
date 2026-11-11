# Legacy: Python version (1.x)

The original Python tray app (pystray + paramiko), kept for reference. It has been replaced by the native app in [`app/`](../app); see the main [README](../README.md#-upgrading-from-1x-python) for upgrading.

Known limitations, fixed in 2.0: clipboard polling every second, new images detected by size and the first 100 bytes only (similar screenshots were skipped), RSA keys only via `paramiko.RSAKey`, any host key accepted, `~/.ssh/config` and `ssh-agent` ignored.
