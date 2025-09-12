# Windows Screenshot Uploader for Claude Code

A Windows tray application that automatically monitors your clipboard for screenshots and uploads them to a remote server via SSH. Perfect for seamless screenshot sharing with Claude Code when working on remote servers.

## Features

- 🖼️ **Automatic Screenshot Detection**: Monitors clipboard for new images
- 🚀 **Instant Upload**: Uploads screenshots to remote server via SSH
- 📋 **Clipboard Integration**: Automatically copies remote URL back to clipboard
- 🔔 **System Notifications**: Visual feedback for upload status
- 🎯 **System Tray**: Runs quietly in background with tray icon
- 🔐 **Secure**: Uses SSH key authentication
- ⚡ **Fast**: Minimal resource usage

## How It Works

1. Take a screenshot using Windows built-in tools (Win+Shift+S, Snipping Tool, etc.)
2. The application detects the new image in your clipboard
3. Image is uploaded to your remote server via SSH
4. Remote file path is copied to your clipboard
5. Paste the file path directly into Claude Code

## Requirements

- Windows 10/11
- Python 3.8 or higher
- SSH access to a remote server
- SSH key pair configured

## Installation

### Quick Setup

1. **Clone or download this repository**
   ```cmd
   git clone https://github.com/yourusername/windows-screenshot-uploader.git
   cd windows-screenshot-uploader
   ```

2. **Run the installation script**
   ```cmd
   install.bat
   ```

### Manual Setup

1. **Install Python dependencies**
   ```cmd
   pip install -r requirements.txt
   ```

2. **Create configuration file**
   ```cmd
   copy config.example.json config.json
   ```

3. **Edit configuration** (see Configuration section below)

## Configuration

Edit `config.json` with your server details:

```json
{
  "ssh_host": "your-server.example.com",
  "ssh_port": 22,
  "ssh_user": "your-username",
  "ssh_key_path": "C:\\Users\\YourUsername\\.ssh\\id_rsa",
  "remote_path": "/tmp/screenshots",
  "check_interval": 1,
  "auto_cleanup": true,
  "notification_enabled": true
}
```

### Configuration Options

- **ssh_host**: Your remote server hostname or IP
- **ssh_port**: SSH port (usually 22)
- **ssh_user**: Your username on the remote server
- **ssh_key_path**: Path to your private SSH key file
- **remote_path**: Directory on remote server to store screenshots
- **check_interval**: How often to check clipboard (seconds)
- **auto_cleanup**: Whether to delete local temp files after upload
- **notification_enabled**: Show system notifications

## SSH Key Setup

1. **Generate SSH key pair** (if you don't have one):
   ```cmd
   ssh-keygen -t rsa -b 4096 -C "your-email@example.com"
   ```

2. **Copy public key to your server**:
   ```cmd
   ssh-copy-id your-username@your-server.example.com
   ```

3. **Test SSH connection**:
   ```cmd
   ssh -i C:\Users\YourUsername\.ssh\id_rsa your-username@your-server.example.com
   ```

## Usage

### Start the Application

```cmd
python src/screenshot_uploader.py
```

The application will start and show an icon in your system tray.

### Taking Screenshots

Use any Windows screenshot tool:
- **Win + Shift + S**: Windows Snip & Sketch
- **Alt + Print Screen**: Active window screenshot
- **Print Screen**: Full screen screenshot
- **Snipping Tool**: Classic screenshot tool

The application automatically detects new images in your clipboard and uploads them.

### Tray Icon Menu

Right-click the tray icon for options:
- **Upload Screenshot**: Manually trigger upload of current clipboard image
- **Pause/Resume**: Toggle clipboard monitoring
- **Settings**: Open configuration file
- **View Logs**: Open log directory
- **Quit**: Exit application

## Server Setup

Your remote server needs:

1. **SSH access** with key authentication
2. **Proper permissions** for the upload directory

### Create Upload Directory

```bash
mkdir -p /tmp/screenshots
chmod 755 /tmp/screenshots
```

## Auto-Startup

The installation script automatically creates a startup shortcut. The application will start when you log into Windows.

To manually add to startup:
1. Press `Win + R`, type `shell:startup`
2. Create a shortcut to `src/screenshot_uploader.py`

## Troubleshooting

### Common Issues

1. **"pywin32 not installed"**
   ```cmd
   pip install pywin32
   ```

2. **SSH connection fails**
   - Check SSH key path in config.json
   - Test SSH connection manually
   - Verify server hostname/port

3. **No system tray icon**
   - Ensure notifications are enabled in Windows
   - Check if icon is hidden in system tray overflow

4. **Upload fails**
   - Check remote directory permissions
   - Verify SSH key has write access
   - Check logs in `%LOCALAPPDATA%/ScreenshotUploader/`

### Viewing Logs

Logs are stored in: `%LOCALAPPDATA%/ScreenshotUploader/screenshot_uploader.log`

Or use the tray menu: **View Logs**

## Development

### Project Structure

```
windows-screenshot-uploader/
├── src/
│   └── screenshot_uploader.py    # Main application
├── assets/                       # Icons and resources
├── config.example.json           # Configuration template
├── requirements.txt              # Python dependencies
├── install.bat                  # Installation script
└── README.md                    # This file
```

### Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test on Windows
5. Submit a pull request

## License

MIT License - see LICENSE file for details.

## Related Projects

- [Mac Screenshot Uploader](https://github.com/mdrzn/claude-screenshot-uploader) - Original macOS version

## Support

- 🐛 [Report Issues](https://github.com/yourusername/windows-screenshot-uploader/issues)
- 💡 [Feature Requests](https://github.com/yourusername/windows-screenshot-uploader/issues)
- 📖 [Documentation](https://github.com/yourusername/windows-screenshot-uploader/wiki)