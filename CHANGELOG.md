# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.1] - 2026-11-12

### Fixed
- Images copied together with their file were ignored, with nothing in the log: Snipping Tool (<kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd>) with automatic saving and the clipboard history (<kbd>Win</kbd>+<kbd>V</kbd>) put the image and the saved file in the clipboard. The image now counts; files copied in Explorer, which carry no image data, are still ignored.

## [2.0.0] - 2026-11-11

### Added
- Native Windows tray app (C#/.NET 10, WinForms), shipped as a single self-contained `.exe`: no Python, pip or console window.
- Clipboard changes arrive as events (`AddClipboardFormatListener`), with no polling. An image is new when its bytes are new (SHA-256 of the whole image), so consecutive full-screen screenshots are all uploaded.
- Uploads through the OpenSSH client built into Windows (`BatchMode=yes`, `StrictHostKeyChecking=accept-new`): every key type, `~/.ssh/config` and `ssh-agent` work, and the host key is pinned on first contact. One connection creates the remote folder, writes the file and returns its absolute path.
- Settings window: add, edit, remove and test servers, start with Windows, notifications, global shortcut (default <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>U</kbd>).
- Tray menu with status and active server, upload clipboard image, recent uploads (click to copy the path again), server switch, pause/resume, open log.
- Icon drawn for each state: green active, orange uploading, gray paused, red error.
- Native notifications, including failures, with readable SSH error messages.
- Command line for the running app: `--upload`, `--pause`, `--resume`, `--toggle`, `--server`, `--settings`, `--quit`.
- English and Italian.

### Changed
- Config and log moved to `%APPDATA%\ScreenshotUploader`, config in the same format as the Mac version (`active` + `servers` by key). The 1.x `config.json` is imported on first launch, and the 1.x startup script can be turned off from the app.
- The image already in the clipboard at startup, or copied while paused, is no longer uploaded.
- The path replaces the image only if nothing else was copied during the upload.
- The Python version moved to `legacy/`.

### Fixed
- The second of two similar screenshots was skipped (only size and the first 100 bytes were compared).
- ed25519/ECDSA keys, `~/.ssh/config` and `ssh-agent` were ignored, and any host key was accepted.
- A missing or invalid `config.json` closed the app without a message.

## [1.1.0] - 2026-11-11

Snapshot of the Python version in use before the rewrite: multiple servers, server manager window with connection test, working pause/resume, remote folder created over SFTP.
