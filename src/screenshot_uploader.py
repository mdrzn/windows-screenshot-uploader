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
import uuid
from pathlib import Path
from io import BytesIO
import logging
import tkinter as tk
from tkinter import ttk, filedialog, messagebox

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


class ServerManagerDialog:
    """Server management dialog using tkinter"""

    def __init__(self, parent_app):
        self.parent_app = parent_app
        self.root = None
        self.tree = None
        self.servers_data = []

    def center_window(self, window, width, height, parent=None):
        """Center window on screen or relative to parent"""
        if parent and parent.winfo_exists():
            try:
                # Center on parent window
                parent.update_idletasks()  # Ensure parent geometry is updated
                parent_x = parent.winfo_x()
                parent_y = parent.winfo_y()
                parent_width = parent.winfo_width()
                parent_height = parent.winfo_height()

                x = parent_x + (parent_width - width) // 2
                y = parent_y + (parent_height - height) // 2

                # Ensure window doesn't go off screen
                screen_width = window.winfo_screenwidth()
                screen_height = window.winfo_screenheight()
                x = max(0, min(x, screen_width - width))
                y = max(0, min(y, screen_height - height))
            except:
                # Fallback to screen center if parent positioning fails
                parent = None

        if not parent:
            # Center on screen
            window.update_idletasks()
            screen_width = window.winfo_screenwidth()
            screen_height = window.winfo_screenheight()

            x = (screen_width - width) // 2
            y = (screen_height - height) // 2

        window.geometry(f"{width}x{height}+{x}+{y}")

    def show(self):
        """Show the server management dialog"""
        self.root = tk.Toplevel()
        self.root.title("Manage Servers")
        self.root.resizable(True, True)
        self.root.grab_set()  # Make dialog modal

        # Center the window on screen
        self.center_window(self.root, 700, 500)

        self.create_widgets()
        self.load_servers()
        self.root.mainloop()

    def create_widgets(self):
        """Create the UI widgets"""
        # Main frame
        main_frame = ttk.Frame(self.root, padding="10")
        main_frame.grid(row=0, column=0, sticky=(tk.W, tk.E, tk.N, tk.S))

        # Configure grid weights
        self.root.columnconfigure(0, weight=1)
        self.root.rowconfigure(0, weight=1)
        main_frame.columnconfigure(0, weight=1)
        main_frame.rowconfigure(0, weight=1)

        # Server list frame
        list_frame = ttk.LabelFrame(main_frame, text="Servers", padding="5")
        list_frame.grid(row=0, column=0, columnspan=2, sticky=(tk.W, tk.E, tk.N, tk.S), pady=(0, 10))
        list_frame.columnconfigure(0, weight=1)
        list_frame.rowconfigure(0, weight=1)

        # Treeview for server list
        columns = ('Name', 'Host', 'User', 'Status')
        self.tree = ttk.Treeview(list_frame, columns=columns, show='headings', height=10)

        # Define headings
        self.tree.heading('Name', text='Name')
        self.tree.heading('Host', text='Host')
        self.tree.heading('User', text='User')
        self.tree.heading('Status', text='Status')

        # Define column widths
        self.tree.column('Name', width=150)
        self.tree.column('Host', width=200)
        self.tree.column('User', width=100)
        self.tree.column('Status', width=100)

        # Scrollbar for treeview
        scrollbar = ttk.Scrollbar(list_frame, orient=tk.VERTICAL, command=self.tree.yview)
        self.tree.configure(yscrollcommand=scrollbar.set)

        self.tree.grid(row=0, column=0, sticky=(tk.W, tk.E, tk.N, tk.S))
        scrollbar.grid(row=0, column=1, sticky=(tk.N, tk.S))

        # Buttons frame
        buttons_frame = ttk.Frame(main_frame)
        buttons_frame.grid(row=1, column=0, columnspan=2, sticky=(tk.W, tk.E), pady=(0, 10))

        ttk.Button(buttons_frame, text="Add Server", command=self.add_server).pack(side=tk.LEFT, padx=(0, 5))
        ttk.Button(buttons_frame, text="Edit Server", command=self.edit_server).pack(side=tk.LEFT, padx=(0, 5))
        ttk.Button(buttons_frame, text="Delete Server", command=self.delete_server).pack(side=tk.LEFT, padx=(0, 5))
        ttk.Button(buttons_frame, text="Test Connection", command=self.test_connection).pack(side=tk.LEFT, padx=(0, 5))
        ttk.Button(buttons_frame, text="Set as Active", command=self.set_active).pack(side=tk.LEFT, padx=(0, 5))

        # Close button
        close_frame = ttk.Frame(main_frame)
        close_frame.grid(row=2, column=0, columnspan=2, sticky=(tk.E))
        ttk.Button(close_frame, text="Close", command=self.close_dialog).pack(side=tk.RIGHT)

    def load_servers(self):
        """Load servers from config into the tree view"""
        # Clear existing items
        for item in self.tree.get_children():
            self.tree.delete(item)

        self.servers_data = self.parent_app.config.get('servers', [])
        active_server_id = self.parent_app.config.get('active_server_id')

        for server in self.servers_data:
            status = "Active" if server['id'] == active_server_id else "Inactive"
            if not server.get('enabled', True):
                status = "Disabled"

            self.tree.insert('', tk.END, values=(
                server['name'],
                server['ssh_host'],
                server['ssh_user'],
                status
            ))

    def add_server(self):
        """Add a new server"""
        dialog = ServerEditDialog(self.root, None, self.parent_app)
        if dialog.show():
            new_server = dialog.get_server_data()
            self.servers_data.append(new_server)
            self.save_servers()
            self.load_servers()

    def edit_server(self):
        """Edit selected server"""
        selection = self.tree.selection()
        if not selection:
            messagebox.showwarning("No Selection", "Please select a server to edit.")
            return

        item = selection[0]
        index = self.tree.index(item)
        server = self.servers_data[index]

        dialog = ServerEditDialog(self.root, server, self.parent_app)
        if dialog.show():
            updated_server = dialog.get_server_data()
            self.servers_data[index] = updated_server
            self.save_servers()
            self.load_servers()

    def delete_server(self):
        """Delete selected server"""
        selection = self.tree.selection()
        if not selection:
            messagebox.showwarning("No Selection", "Please select a server to delete.")
            return

        if len(self.servers_data) <= 1:
            messagebox.showerror("Cannot Delete", "You must have at least one server configured.")
            return

        if messagebox.askyesno("Confirm Delete", "Are you sure you want to delete this server?"):
            item = selection[0]
            index = self.tree.index(item)
            deleted_server = self.servers_data.pop(index)

            # If we deleted the active server, set first remaining as active
            if deleted_server['id'] == self.parent_app.config.get('active_server_id'):
                if self.servers_data:
                    self.parent_app.config['active_server_id'] = self.servers_data[0]['id']

            self.save_servers()
            self.load_servers()

    def test_connection(self):
        """Test connection to selected server"""
        selection = self.tree.selection()
        if not selection:
            messagebox.showwarning("No Selection", "Please select a server to test.")
            return

        item = selection[0]
        index = self.tree.index(item)
        server = self.servers_data[index]

        # Test connection in separate thread
        threading.Thread(target=self._test_connection_worker, args=(server,), daemon=True).start()

    def _test_connection_worker(self, server):
        """Worker method for testing connection"""
        try:
            import paramiko
            ssh = paramiko.SSHClient()
            ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())

            private_key = paramiko.RSAKey.from_private_key_file(server['ssh_key_path'])
            ssh.connect(
                hostname=server['ssh_host'],
                username=server['ssh_user'],
                pkey=private_key,
                port=server.get('ssh_port', 22),
                timeout=10
            )
            ssh.close()

            self.root.after(0, lambda: messagebox.showinfo("Connection Test", f"Successfully connected to {server['name']}!"))
        except Exception as e:
            self.root.after(0, lambda: messagebox.showerror("Connection Test", f"Failed to connect to {server['name']}:\n{str(e)}"))

    def set_active(self):
        """Set selected server as active"""
        selection = self.tree.selection()
        if not selection:
            messagebox.showwarning("No Selection", "Please select a server to set as active.")
            return

        item = selection[0]
        index = self.tree.index(item)
        server = self.servers_data[index]

        self.parent_app.config['active_server_id'] = server['id']
        self.parent_app.active_server = server
        self.save_servers()
        self.load_servers()

        # Update parent app's tray status
        self.parent_app.update_tray_status(f"Ready - {server['name']}")
        messagebox.showinfo("Active Server", f"'{server['name']}' is now the active server.")

    def save_servers(self):
        """Save servers back to config"""
        self.parent_app.config['servers'] = self.servers_data
        self.parent_app.save_config(self.parent_app.config)

    def close_dialog(self):
        """Close the dialog"""
        self.root.destroy()


class ServerEditDialog:
    """Dialog for adding/editing server configuration"""

    def __init__(self, parent, server_data, parent_app):
        self.parent = parent
        self.server_data = server_data.copy() if server_data else {}
        self.parent_app = parent_app
        self.root = None
        self.result = None
        self.vars = {}

    def center_window(self, window, width, height, parent=None):
        """Center window on screen or relative to parent"""
        if parent and parent.winfo_exists():
            try:
                # Center on parent window
                parent.update_idletasks()  # Ensure parent geometry is updated
                parent_x = parent.winfo_x()
                parent_y = parent.winfo_y()
                parent_width = parent.winfo_width()
                parent_height = parent.winfo_height()

                x = parent_x + (parent_width - width) // 2
                y = parent_y + (parent_height - height) // 2

                # Ensure window doesn't go off screen
                screen_width = window.winfo_screenwidth()
                screen_height = window.winfo_screenheight()
                x = max(0, min(x, screen_width - width))
                y = max(0, min(y, screen_height - height))
            except:
                # Fallback to screen center if parent positioning fails
                parent = None

        if not parent:
            # Center on screen
            window.update_idletasks()
            screen_width = window.winfo_screenwidth()
            screen_height = window.winfo_screenheight()

            x = (screen_width - width) // 2
            y = (screen_height - height) // 2

        window.geometry(f"{width}x{height}+{x}+{y}")

    def show(self):
        """Show the edit dialog"""
        self.root = tk.Toplevel(self.parent)
        self.root.title("Add Server" if not self.server_data else "Edit Server")
        self.root.resizable(False, False)
        self.root.grab_set()

        # Center on parent window
        self.center_window(self.root, 500, 400, self.parent)

        # Set transient relationship
        self.root.transient(self.parent)

        self.create_widgets()
        self.populate_fields()

        # Wait for dialog to close
        self.root.wait_window()
        return self.result is not None

    def create_widgets(self):
        """Create dialog widgets"""
        main_frame = ttk.Frame(self.root, padding="15")
        main_frame.pack(fill=tk.BOTH, expand=True)

        # Server details frame
        details_frame = ttk.LabelFrame(main_frame, text="Server Details", padding="10")
        details_frame.pack(fill=tk.X, pady=(0, 10))

        # Name
        ttk.Label(details_frame, text="Name:").grid(row=0, column=0, sticky=tk.W, pady=2)
        self.vars['name'] = tk.StringVar()
        ttk.Entry(details_frame, textvariable=self.vars['name'], width=40).grid(row=0, column=1, sticky=(tk.W, tk.E), padx=(10, 0), pady=2)

        # Host
        ttk.Label(details_frame, text="Host:").grid(row=1, column=0, sticky=tk.W, pady=2)
        self.vars['ssh_host'] = tk.StringVar()
        ttk.Entry(details_frame, textvariable=self.vars['ssh_host'], width=40).grid(row=1, column=1, sticky=(tk.W, tk.E), padx=(10, 0), pady=2)

        # Port
        ttk.Label(details_frame, text="Port:").grid(row=2, column=0, sticky=tk.W, pady=2)
        self.vars['ssh_port'] = tk.StringVar(value="22")
        ttk.Entry(details_frame, textvariable=self.vars['ssh_port'], width=40).grid(row=2, column=1, sticky=(tk.W, tk.E), padx=(10, 0), pady=2)

        # Username
        ttk.Label(details_frame, text="Username:").grid(row=3, column=0, sticky=tk.W, pady=2)
        self.vars['ssh_user'] = tk.StringVar()
        ttk.Entry(details_frame, textvariable=self.vars['ssh_user'], width=40).grid(row=3, column=1, sticky=(tk.W, tk.E), padx=(10, 0), pady=2)

        # SSH Key Path
        ttk.Label(details_frame, text="SSH Key:").grid(row=4, column=0, sticky=tk.W, pady=2)
        key_frame = ttk.Frame(details_frame)
        key_frame.grid(row=4, column=1, sticky=(tk.W, tk.E), padx=(10, 0), pady=2)
        key_frame.columnconfigure(0, weight=1)

        self.vars['ssh_key_path'] = tk.StringVar()
        ttk.Entry(key_frame, textvariable=self.vars['ssh_key_path']).grid(row=0, column=0, sticky=(tk.W, tk.E))
        ttk.Button(key_frame, text="Browse", command=self.browse_ssh_key).grid(row=0, column=1, padx=(5, 0))

        # Remote Path
        ttk.Label(details_frame, text="Remote Path:").grid(row=5, column=0, sticky=tk.W, pady=2)
        self.vars['remote_path'] = tk.StringVar()
        ttk.Entry(details_frame, textvariable=self.vars['remote_path'], width=40).grid(row=5, column=1, sticky=(tk.W, tk.E), padx=(10, 0), pady=2)

        # Enabled checkbox
        self.vars['enabled'] = tk.BooleanVar(value=True)
        ttk.Checkbutton(details_frame, text="Enabled", variable=self.vars['enabled']).grid(row=6, column=1, sticky=tk.W, padx=(10, 0), pady=5)

        details_frame.columnconfigure(1, weight=1)

        # Buttons frame
        buttons_frame = ttk.Frame(main_frame)
        buttons_frame.pack(fill=tk.X, pady=(10, 0))

        ttk.Button(buttons_frame, text="Test Connection", command=self.test_connection).pack(side=tk.LEFT, padx=(0, 5))
        ttk.Button(buttons_frame, text="Cancel", command=self.cancel).pack(side=tk.RIGHT)
        ttk.Button(buttons_frame, text="Save", command=self.save).pack(side=tk.RIGHT, padx=(0, 5))

    def populate_fields(self):
        """Populate fields with existing server data"""
        if self.server_data:
            for key, var in self.vars.items():
                if key in self.server_data:
                    if isinstance(var, tk.BooleanVar):
                        var.set(self.server_data[key])
                    else:
                        var.set(str(self.server_data[key]))

    def browse_ssh_key(self):
        """Browse for SSH key file"""
        filename = filedialog.askopenfilename(
            title="Select SSH Private Key",
            filetypes=[("All files", "*.*"), ("Key files", "*.pem *.key")],
            initialdir=str(Path.home() / ".ssh")
        )
        if filename:
            self.vars['ssh_key_path'].set(filename)

    def test_connection(self):
        """Test connection with current settings"""
        if not self.validate_fields():
            return

        server_data = self.get_server_data()
        threading.Thread(target=self._test_connection_worker, args=(server_data,), daemon=True).start()

    def _test_connection_worker(self, server_data):
        """Worker method for testing connection"""
        try:
            import paramiko
            ssh = paramiko.SSHClient()
            ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())

            private_key = paramiko.RSAKey.from_private_key_file(server_data['ssh_key_path'])
            ssh.connect(
                hostname=server_data['ssh_host'],
                username=server_data['ssh_user'],
                pkey=private_key,
                port=server_data.get('ssh_port', 22),
                timeout=10
            )
            ssh.close()

            self.root.after(0, lambda: messagebox.showinfo("Connection Test", "Connection successful!"))
        except Exception as e:
            self.root.after(0, lambda: messagebox.showerror("Connection Test", f"Connection failed:\n{str(e)}"))

    def validate_fields(self):
        """Validate form fields"""
        required_fields = ['name', 'ssh_host', 'ssh_user', 'ssh_key_path', 'remote_path']
        for field in required_fields:
            if not self.vars[field].get().strip():
                messagebox.showerror("Validation Error", f"Please fill in the {field.replace('_', ' ').title()} field.")
                return False

        # Validate port
        try:
            port = int(self.vars['ssh_port'].get())
            if port < 1 or port > 65535:
                raise ValueError()
        except ValueError:
            messagebox.showerror("Validation Error", "Please enter a valid port number (1-65535).")
            return False

        # Check if SSH key file exists
        key_path = self.vars['ssh_key_path'].get().strip()
        if not Path(key_path).exists():
            messagebox.showerror("Validation Error", "SSH key file does not exist.")
            return False

        return True

    def save(self):
        """Save server configuration"""
        if not self.validate_fields():
            return

        self.result = self.get_server_data()
        self.root.destroy()

    def cancel(self):
        """Cancel and close dialog"""
        self.result = None
        self.root.destroy()

    def get_server_data(self):
        """Get server data from form"""
        server_id = self.server_data.get('id', str(uuid.uuid4()))

        return {
            'id': server_id,
            'name': self.vars['name'].get().strip(),
            'ssh_host': self.vars['ssh_host'].get().strip(),
            'ssh_port': int(self.vars['ssh_port'].get()),
            'ssh_user': self.vars['ssh_user'].get().strip(),
            'ssh_key_path': self.vars['ssh_key_path'].get().strip(),
            'remote_path': self.vars['remote_path'].get().strip(),
            'enabled': self.vars['enabled'].get()
        }


class ScreenshotUploader:
    def __init__(self):
        self.config_path = Path(__file__).parent.parent / "config.json"
        self.config = self.load_config()
        self.active_server = None
        self.running = True
        self.uploading = False
        self.monitoring_enabled = True
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
            print(f"ERROR: Config file not found: {self.config_path}")
            print("Please create config.json from config.example.json")
            sys.exit(1)

        try:
            with open(self.config_path, 'r') as f:
                config = json.load(f)

            # Check if this is an old single-server config and migrate it
            if self.is_legacy_config(config):
                config = self.migrate_legacy_config(config)
                self.save_config(config)

            # Validate multihost config
            self.validate_multihost_config(config)

            # Set active server
            self.active_server = self.get_active_server(config)

            return config
        except (json.JSONDecodeError, ValueError) as e:
            print(f"ERROR: Invalid config file: {e}")
            sys.exit(1)

    def is_legacy_config(self, config):
        """Check if config is in legacy single-server format"""
        legacy_fields = ['ssh_host', 'ssh_user', 'ssh_key_path', 'remote_path']
        return any(field in config for field in legacy_fields) and 'servers' not in config

    def migrate_legacy_config(self, config):
        """Migrate legacy single-server config to multihost format"""
        print("Migrating legacy configuration to multihost format...")

        # Create backup
        backup_path = self.config_path.with_suffix('.json.backup')
        with open(backup_path, 'w') as f:
            json.dump(config, f, indent=2)
        print(f"Backup created at: {backup_path}")

        # Extract server settings
        server_config = {
            "id": "server1",
            "name": "Main Server",
            "ssh_host": config.get('ssh_host'),
            "ssh_port": config.get('ssh_port', 22),
            "ssh_user": config.get('ssh_user'),
            "ssh_key_path": config.get('ssh_key_path'),
            "remote_path": config.get('remote_path'),
            "enabled": True
        }

        # Create new multihost config
        new_config = {
            "servers": [server_config],
            "active_server_id": "server1",
            "check_interval": config.get('check_interval', 1),
            "auto_cleanup": config.get('auto_cleanup', True),
            "notification_enabled": config.get('notification_enabled', True)
        }

        return new_config

    def validate_multihost_config(self, config):
        """Validate multihost configuration"""
        if 'servers' not in config or not isinstance(config['servers'], list):
            raise ValueError("Config must contain 'servers' list")

        if not config['servers']:
            raise ValueError("At least one server must be configured")

        # Validate each server
        for i, server in enumerate(config['servers']):
            required_fields = ['id', 'name', 'ssh_host', 'ssh_user', 'ssh_key_path', 'remote_path']
            for field in required_fields:
                if field not in server:
                    raise ValueError(f"Server {i}: Missing required field '{field}'")

        # Check for duplicate server IDs
        server_ids = [server['id'] for server in config['servers']]
        if len(server_ids) != len(set(server_ids)):
            raise ValueError("Duplicate server IDs found")

        # Validate active_server_id exists
        if 'active_server_id' in config:
            if not any(server['id'] == config['active_server_id'] for server in config['servers']):
                raise ValueError(f"Active server ID '{config['active_server_id']}' not found in servers list")

    def get_active_server(self, config):
        """Get the active server configuration"""
        active_id = config.get('active_server_id')
        if not active_id and config['servers']:
            # Default to first enabled server
            for server in config['servers']:
                if server.get('enabled', True):
                    return server
            return config['servers'][0]  # Fallback to first server

        # Find server by ID
        for server in config['servers']:
            if server['id'] == active_id:
                return server

        # Fallback to first server if active server not found
        return config['servers'][0] if config['servers'] else None

    def save_config(self, config):
        """Save configuration to JSON file"""
        try:
            with open(self.config_path, 'w') as f:
                json.dump(config, f, indent=2)
        except Exception as e:
            print(f"ERROR: Failed to save config: {e}")

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
        menu = self.create_menu()

        # Set initial tooltip with active server
        initial_tooltip = "Screenshot Uploader - Ready"
        if self.active_server:
            initial_tooltip = f"Screenshot Uploader - Ready - {self.active_server['name']}"

        self.icon = pystray.Icon(
            "Screenshot Uploader",
            icon_image,
            initial_tooltip,
            menu
        )

    def create_menu(self):
        """Create tray menu with dynamic text"""
        toggle_text = "Pause" if self.monitoring_enabled else "Resume"

        # Create server switch submenu
        server_menu_items = []
        servers = self.config.get('servers', [])
        active_server_id = self.config.get('active_server_id')

        for server in servers:
            if server.get('enabled', True):
                # Add bullet point for active server
                prefix = "● " if server['id'] == active_server_id else "○ "
                server_name = f"{prefix}{server['name']}"
                # Create a proper callback function to avoid lambda closure issues
                def make_switch_callback(srv):
                    return lambda icon, item: self.switch_server(srv)

                server_menu_items.append(
                    MenuItem(server_name, make_switch_callback(server))
                )

        # Build main menu
        menu_items = [
            MenuItem("Upload Screenshot", self.manual_upload),
            Menu.SEPARATOR,
        ]

        # Add server switching submenu if there are multiple servers
        if len([s for s in servers if s.get('enabled', True)]) > 1:
            server_submenu = Menu(*server_menu_items)
            menu_items.append(MenuItem("Switch Server", server_submenu))
            menu_items.append(Menu.SEPARATOR)

        menu_items.extend([
            MenuItem("Manage Servers...", self.open_server_manager),
            Menu.SEPARATOR,
            MenuItem(toggle_text, self.toggle_monitoring),
            Menu.SEPARATOR,
            MenuItem("Settings", self.open_settings),
            MenuItem("View Logs", self.open_logs),
            Menu.SEPARATOR,
            MenuItem("Quit", self.quit_app)
        ])

        return Menu(*menu_items)

    def update_menu(self):
        """Update tray menu with current state"""
        if self.icon:
            self.icon.menu = self.create_menu()

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

                server_name = self.active_server['name'] if self.active_server else "Unknown"
                self.show_notification("Upload Complete", f"Screenshot uploaded to {server_name}!\nPath copied to clipboard")

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

    def _sftp_ensure_dir(self, sftp, remote_dir):
        """Recursively create remote_dir on the SFTP server if missing."""
        if not remote_dir or remote_dir == "/":
            return
        try:
            sftp.stat(remote_dir)
            return
        except IOError:
            pass
        parent = remote_dir.rsplit("/", 1)[0]
        if parent and parent != remote_dir:
            self._sftp_ensure_dir(sftp, parent)
        try:
            sftp.mkdir(remote_dir)
            self.logger.info(f"Created remote directory: {remote_dir}")
        except IOError as e:
            try:
                sftp.stat(remote_dir)
            except IOError:
                raise e

    def upload_via_ssh(self, local_path, filename):
        """Upload file via SSH/SCP"""
        if not self.active_server:
            raise Exception("No active server configured")

        if not self.active_server.get('enabled', True):
            raise Exception("Active server is disabled")

        try:
            # Setup SSH client
            ssh = paramiko.SSHClient()
            ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())

            # Connect using private key
            private_key = paramiko.RSAKey.from_private_key_file(
                self.active_server['ssh_key_path']
            )

            ssh.connect(
                hostname=self.active_server['ssh_host'],
                username=self.active_server['ssh_user'],
                pkey=private_key,
                port=self.active_server.get('ssh_port', 22)
            )

            # Upload file
            sftp = ssh.open_sftp()
            remote_dir = self.active_server['remote_path']
            self._sftp_ensure_dir(sftp, remote_dir)
            remote_path = f"{remote_dir}/{filename}"
            sftp.put(str(local_path), remote_path)
            sftp.close()
            ssh.close()

            self.logger.info(f"File uploaded to {self.active_server['name']}: {remote_path}")
            return remote_path

        except Exception as e:
            self.logger.error(f"SSH upload failed to {self.active_server['name']}: {e}")
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
            elif not self.monitoring_enabled:
                status_key = "paused"
            else:
                status_key = "ready"

            new_icon = self.create_icon_image(status_key)
            self.icon.icon = new_icon

    def monitor_clipboard(self):
        """Monitor clipboard for new images"""
        while self.running:
            if self.monitoring_enabled and not self.uploading:
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
        self.monitoring_enabled = not self.monitoring_enabled

        if self.monitoring_enabled:
            status = "Resumed"
            message = "Screenshot monitoring resumed"
            self.update_tray_status("Ready")
        else:
            status = "Paused"
            message = "Screenshot monitoring paused"
            self.update_tray_status("Paused")

        self.show_notification(status, message)
        self.logger.info(f"Monitoring {status.lower()}")

        # Update menu
        self.update_menu()

    def switch_server(self, server):
        """Switch to a different server"""
        # Check if we already have this server active
        if self.active_server and server['id'] == self.active_server['id']:
            return  # Already active

        self.active_server = server
        self.config['active_server_id'] = server['id']
        self.save_config(self.config)

        # Update tray status and menu
        self.update_tray_status(f"Ready - {server['name']}")
        self.update_menu()

        # Show notification
        self.show_notification("Server Switched", f"Now using: {server['name']}")
        self.logger.info(f"Switched to server: {server['name']} ({server['ssh_host']})")

    def open_server_manager(self, icon, item):
        """Open server management dialog"""
        try:
            dialog = ServerManagerDialog(self)
            threading.Thread(target=dialog.show, daemon=True).start()
        except Exception as e:
            self.logger.error(f"Failed to open server manager: {e}")
            self.show_notification("Error", "Failed to open server manager", error=True)

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

        if self.active_server:
            self.logger.info(f"Active server: {self.active_server['name']} ({self.active_server['ssh_host']})")
        else:
            self.logger.warning("No active server configured")

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