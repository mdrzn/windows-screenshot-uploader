#!/usr/bin/env python3
"""
Windows Screenshot Uploader for Claude Code
Monitors clipboard for images and uploads them to a remote server via SSH.
"""

import os
import sys
import json
import time
import tempfile
import threading
from pathlib import Path
from io import BytesIO
import logging

try:
    import win32clipboard
    import win32con
    import win32gui
    import win32api
except ImportError:
    print("ERROR: pywin32 not installed. Run 'pip install pywin32'")
    sys.exit(1)

try:
    import pystray
    from pystray import MenuItem, Menu
    from PIL import Image, ImageDraw
except ImportError:
    print("ERROR: Required packages not installed. Run 'pip install -r requirements.txt'")
    sys.exit(1)

try:
    import paramiko
except ImportError:
    print("ERROR: paramiko not installed. Run 'pip install paramiko'")
    sys.exit(1)

try:
    from plyer import notification
except ImportError:
    notification = None


class ScreenshotUploader:
    def __init__(self):
        self.config_path = Path(__file__).parent.parent / "config.json"
        self.config = self.load_config()
        self.running = True
        self.uploading = False
        self.last_clipboard_image_hash = None
        
        # Setup logging
        log_dir = Path.home() / "AppData" / "Local" / "ScreenshotUploader"
        log_dir.mkdir(exist_ok=True)
        logging.basicConfig(
            level=logging.INFO,
            format='%(asctime)s - %(levelname)s - %(message)s',
            handlers=[
                logging.FileHandler(log_dir / "screenshot_uploader.log"),
                logging.StreamHandler()
            ]
        )
        self.logger = logging.getLogger(__name__)
        
        # Create tray icon
        self.icon = None
        self.create_tray_icon()

    def load_config(self):
        """Load configuration from JSON file"""
        if not self.config_path.exists():
            self.logger.error(f"Config file not found: {self.config_path}")
            self.show_notification("Error", "Config file not found. Please create config.json", error=True)
            sys.exit(1)
        
        try:
            with open(self.config_path, 'r') as f:
                config = json.load(f)
            
            # Validate required fields
            required_fields = ['ssh_host', 'ssh_user', 'ssh_key_path', 'remote_path']
            for field in required_fields:
                if field not in config:
                    raise ValueError(f"Missing required field: {field}")
            
            return config
        except (json.JSONDecodeError, ValueError) as e:
            self.logger.error(f"Invalid config file: {e}")
            self.show_notification("Error", f"Invalid config file: {e}", error=True)
            sys.exit(1)

    def create_icon_image(self, status="ready"):
        """Create simple colored circle icon based on status"""
        colors = {
            "ready": (0, 200, 0),      # Green
            "uploading": (255, 165, 0), # Orange  
            "error": (220, 20, 60),     # Red
            "paused": (128, 128, 128)   # Gray
        }
        
        color = colors.get(status, colors["ready"])
        img = Image.new('RGB', (64, 64), (255, 255, 255))
        draw = ImageDraw.Draw(img)
        draw.ellipse([4, 4, 60, 60], fill=color)
        
        return img

    def create_tray_icon(self):
        """Create system tray icon"""
        icon_image = self.create_icon_image("ready")
        
        menu = Menu(
            MenuItem("Upload Screenshot", self.manual_upload),
            MenuItem("Pause/Resume", self.toggle_monitoring),
            Menu.SEPARATOR,
            MenuItem("Settings", self.open_settings),
            MenuItem("View Logs", self.open_logs),
            Menu.SEPARATOR,
            MenuItem("Quit", self.quit_app)
        )
        
        self.icon = pystray.Icon(
            "Screenshot Uploader",
            icon_image,
            "Screenshot Uploader - Ready",
            menu
        )

    def get_clipboard_image(self):
        """Get image from clipboard"""
        try:
            win32clipboard.OpenClipboard()
            
            if win32clipboard.IsClipboardFormatAvailable(win32clipboard.CF_DIB):
                data = win32clipboard.GetClipboardData(win32clipboard.CF_DIB)
                win32clipboard.CloseClipboard()
                
                # Convert DIB to PIL Image
                # DIB data starts with BITMAPINFOHEADER
                img_buffer = BytesIO(data)
                img = Image.open(img_buffer)
                return img
            
        except Exception as e:
            try:
                win32clipboard.CloseClipboard()
            except:
                pass
            self.logger.debug(f"No image in clipboard: {e}")
            return None
        
        win32clipboard.CloseClipboard()
        return None

    def image_hash(self, image):
        """Generate simple hash for image comparison"""
        if image is None:
            return None
        # Simple hash based on image size and mode
        return hash((image.size, image.mode, image.tobytes()[:100]))

    def upload_image(self, image):
        """Upload image to remote server"""
        if not image:
            return None
        
        self.uploading = True
        self.update_tray_status("Uploading...")
        
        try:
            # Save image to temporary file
            timestamp = int(time.time())
            filename = f"screenshot_{timestamp}.png"
            temp_path = Path(tempfile.gettempdir()) / filename
            
            # Save as PNG
            image.save(temp_path, "PNG")
            self.logger.info(f"Saved screenshot to: {temp_path}")
            
            # Upload via SSH
            remote_file_path = self.upload_via_ssh(temp_path, filename)
            
            if remote_file_path:
                # Put remote file path in clipboard
                self.set_clipboard_text(remote_file_path)
                self.logger.info(f"Upload successful. Path copied to clipboard: {remote_file_path}")
                self.show_notification("Upload Complete", f"Screenshot uploaded!\nPath copied to clipboard")
                
                # Clean up temp file
                temp_path.unlink()
                
                return remote_file_path
            
        except Exception as e:
            self.logger.error(f"Upload failed: {e}")
            self.show_notification("Upload Failed", str(e), error=True)
        finally:
            self.uploading = False
            self.update_tray_status("Ready")
        
        return None

    def upload_via_ssh(self, local_path, filename):
        """Upload file via SSH/SCP"""
        try:
            # Setup SSH client
            ssh = paramiko.SSHClient()
            ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
            
            # Connect using private key
            private_key = paramiko.RSAKey.from_private_key_file(
                self.config['ssh_key_path']
            )
            
            ssh.connect(
                hostname=self.config['ssh_host'],
                username=self.config['ssh_user'],
                pkey=private_key,
                port=self.config.get('ssh_port', 22)
            )
            
            # Upload file
            sftp = ssh.open_sftp()
            remote_path = f"{self.config['remote_path']}/{filename}"
            sftp.put(str(local_path), remote_path)
            sftp.close()
            ssh.close()
            
            self.logger.info(f"File uploaded to: {remote_path}")
            return remote_path
            
        except Exception as e:
            self.logger.error(f"SSH upload failed: {e}")
            raise

    def set_clipboard_text(self, text):
        """Set text to clipboard"""
        try:
            win32clipboard.OpenClipboard()
            win32clipboard.EmptyClipboard()
            win32clipboard.SetClipboardText(text)
            win32clipboard.CloseClipboard()
        except Exception as e:
            self.logger.error(f"Failed to set clipboard: {e}")

    def show_notification(self, title, message, error=False):
        """Show system notification"""
        if notification:
            notification.notify(
                title=title,
                message=message,
                app_name="Screenshot Uploader",
                timeout=5
            )
        else:
            # Fallback to console output
            print(f"[{title}] {message}")

    def update_tray_status(self, status):
        """Update tray icon tooltip and icon color"""
        if self.icon:
            self.icon.title = f"Screenshot Uploader - {status}"
            # Update icon color based on status
            status_key = status.lower()
            if "upload" in status_key:
                status_key = "uploading"
            elif "error" in status_key or "fail" in status_key:
                status_key = "error"
            elif "pause" in status_key:
                status_key = "paused"
            else:
                status_key = "ready"
            
            new_icon = self.create_icon_image(status_key)
            self.icon.icon = new_icon

    def monitor_clipboard(self):
        """Monitor clipboard for new images"""
        while self.running:
            if not self.uploading:
                try:
                    image = self.get_clipboard_image()
                    if image:
                        current_hash = self.image_hash(image)
                        if current_hash and current_hash != self.last_clipboard_image_hash:
                            self.last_clipboard_image_hash = current_hash
                            self.logger.info("New screenshot detected in clipboard")
                            self.upload_image(image)
                except Exception as e:
                    self.logger.error(f"Clipboard monitoring error: {e}")
            
            time.sleep(self.config.get('check_interval', 1))

    def manual_upload(self, icon, item):
        """Manual upload trigger from tray menu"""
        threading.Thread(target=self._manual_upload_worker, daemon=True).start()

    def _manual_upload_worker(self):
        """Worker for manual upload"""
        image = self.get_clipboard_image()
        if image:
            self.upload_image(image)
        else:
            self.show_notification("No Image", "No image found in clipboard")

    def toggle_monitoring(self, icon, item):
        """Toggle clipboard monitoring"""
        # This is a placeholder - in a full implementation you'd pause/resume monitoring
        self.show_notification("Info", "Monitoring toggle not implemented yet")

    def open_settings(self, icon, item):
        """Open settings file"""
        os.startfile(self.config_path)

    def open_logs(self, icon, item):
        """Open log directory"""
        log_dir = Path.home() / "AppData" / "Local" / "ScreenshotUploader"
        os.startfile(log_dir)

    def quit_app(self, icon, item):
        """Quit application"""
        self.running = False
        icon.stop()

    def run(self):
        """Start the application"""
        self.logger.info("Starting Screenshot Uploader...")
        
        # Start clipboard monitoring in background thread
        monitor_thread = threading.Thread(target=self.monitor_clipboard, daemon=True)
        monitor_thread.start()
        
        # Run tray icon (blocking)
        self.icon.run()


def main():
    try:
        app = ScreenshotUploader()
        app.run()
    except KeyboardInterrupt:
        print("\nShutting down...")
    except Exception as e:
        print(f"Error: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()