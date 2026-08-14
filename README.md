# LocalShare

A high-speed, peer-to-peer local network file server and manager available for **Android** and **Windows**.

Share full storage, dedicated folders, or Windows drive letters with any device (phone, tablet, laptop, smart TV) on the same WiFi or LAN network using a modern browser interface — no internet, accounts, or third-party cloud required.

---

## Features

- **Cross-Platform**: Run the server on your Android phone or your Windows PC.
- **Customizable Storage & Roots**:
  - **Android**: Share dedicated `LocalShare` folder, full internal storage (`/sdcard`), or SD card.
  - **Windows**: Share dedicated `LocalShare` folder, all drive letters (`C:\`, `D:\`, etc.), or custom chosen directories via folder picker.
- **Customizable Port & Hidden Paths**: Change the HTTP port (default `8080`) and blacklist sensitive paths (e.g., `.git`, `node_modules`).
- **Modern Responsive Web Interface**:
  - **Categorized View**: Quick filters for Images, Videos, Music, and Documents.
  - **Multimedia Streaming**: Full HTTP 206 Range request support for smooth video and audio seeking with built-in preview players.
  - **Fast Multi-File Upload**: Drag & drop support, real-time progress bars, and buffered streaming disk writes.
  - **Full Management**: List, download, upload, create folders, rename, search/filter, and delete.
  - **Dark & Light Mode**: Seamless theme switcher.
- **Instant Connection**:
  - Automatically detects local IPv4 network addresses (Wi-Fi, Ethernet, Hotspot).
  - Displays dynamic **QR Code** for scanning with phones to connect instantly without typing IP addresses.
- **Background Execution**:
  - **Android**: Persistent Foreground Service with notification.
  - **Windows**: System Tray integration with background minimize and tray context menu.

---

## Project Structure

```
LocalShare/
  ├── app/                     # Android Native Application (Kotlin)
  │     ├── src/main/java/     # Android UI, Service, & NanoHTTPD server
  │     └── src/main/assets/   # Embedded web interface
  │
  ├── windows/                 # Windows Native Desktop Application (Electron & Node.js)
  │     ├── src/
  │     │    ├── main.js       # Electron main process & System Tray
  │     │    ├── server.js     # High-throughput HTTP File Server & Streaming
  │     │    ├── preload.js    # Secure IPC bridge
  │     │    └── ui/           # Windows 11 Fluent desktop control panel
  │     ├── public/web/        # Enhanced responsive web file manager
  │     └── package.json
  │
  └── README.md
```

---

## Windows App: How to Run & Build

### Prerequisites
- [Node.js](https://nodejs.org/) (v18 or higher)

### 1. Run in Development Mode
```powershell
cd windows
npm install
npm start
```

### 2. Build Standalone Windows Executable (.exe)
```powershell
cd windows
npm run pack     # Creates standalone directory build in windows/dist/
npm run dist     # Generates full NSIS Windows Installer (.exe)
```

---

## Android App: How to Build & Run

1. Open the project root in **Android Studio**.
2. Build and run on your Android device.
3. Grant **All Files Access** permission if sharing full storage.
4. Tap **Start Sharing** and open the URL or scan the QR code.

