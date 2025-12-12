# Quake Server Manager

A simple tool for gamers to deploy and manage Quake III Arena servers on a VPS. 

![Quake Server Manager Screenshot](https://i.imgur.com/your-screenshot.png) <!-- Replace with a real screenshot -->

## Features

*   **Manage Multiple VPS:** Add and save connections to all your VPS hosts.
*   **Easy Authentication:** Connect with a password or an SSH key.
*   **Create Server Instances:** Set up as many Quake servers as you want on a single VPS.
*   **One-Click Deploy:** Get a new server running with a single click.
*   **Simple Server Controls:** Start, stop, and restart your servers from the app.
*   **Real-time Terminal:** See what's happening on your server in real-time.

## Requirements

*   Windows 10 or 11
*   .NET 8.0 (The installer will help you with this)
*   A VPS with SSH access
*   Your own Quake III Arena game files

## How to Use

1.  **Add Your VPS:**
    *   Click "Add VPS".
    *   Enter your server's IP, port (usually 22), and username (usually `root`).
    *   Enter your password or point the app to your SSH private key file.
    *   Click "Test Connection" to make sure it works.
    *   Save it.

2.  **Set Your Quake III Path:**
    *   Click "Browse" and find your local `Quake III Arena` folder on your computer. This is where the app will get the game files to upload.

3.  **Create a Server Instance:**
    *   Select the VPS you just added from the list.
    *   Click "Add Instance".
    *   Give your server a name and fill in the details.

4.  **Deploy & Manage:**
    *   Select the server instance you want to manage.
    *   Click "Deploy Server" to upload the files and get it online.
    *   Use the "Start", "Stop", and "Restart" buttons to control your server.

## Troubleshooting

*   **Can't connect?** Double-check your IP, port, username, and password/key. Make sure your VPS firewall isn't blocking the port.
*   **Deployment fails?** Make sure you've set the correct path to your Quake III files. Also, check if you have enough disk space on your VPS.
*   **Server won't start?** Make sure `screen` is installed on your VPS. You can usually install it with `apt-get install screen` (for Ubuntu/Debian).

## License

This project is licensed under the MIT License.
