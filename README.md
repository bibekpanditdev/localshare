# 🌐 LocalShare

LocalShare is a high-performance, minimalist file-sharing bridge between your Android device and your PC. Designed with a Windows 11 aesthetic, it offers ultra-fast offline transfers and smooth media streaming without needing an internet connection.

## ✨ Key Features

- **Win11 UI**: A beautiful, minimalist web interface that feels native to your laptop.
- **Turbo Uploads**: Parallel 8-stream uploading and 16MB server buffering for max speed.
- **Smooth Streaming**: Watch long 4K videos smoothly with chunked range-request technology.
- **Full Offline**: Works 100% without internet. Uses your local WiFi or USB Tethering.
- **Privacy First**: Choose exactly what to share. Toggle between one folder or full storage.

## 🚀 Getting Started

### 1. On your Phone
1. Install the APK from the [Latest Release](https://github.com/bibekpanditdev/localshare/releases).
2. Open the app and tap **Start Sharing**.
3. You will see an address like `http://192.168.1.5:8080`.

### 2. On your PC
1. Open your browser and type in the address shown on your phone.
2. **To Upload to Phone**: Drag any file or folder from your PC and drop it into the browser.
3. **To Download to PC**: Browse to your file and click the download icon.

## 💡 Performance Tips

- **USB Tethering (Extreme Speed)**: For the absolute fastest transfer (bypassing WiFi), connect your phone via USB cable, enable **USB Tethering** in Android settings, and use the `192.168.42.x` IP shown in the app.
- **Install as App**: On Chrome or Edge, click the **Install** icon in the address bar to add LocalShare to your Windows taskbar for a native experience.
- **5GHz WiFi**: Use a 5GHz router for high-speed wireless transfers.

## 🛠️ Development

Built with Kotlin, NanoHTTPD, and modern Vanilla JS.

1. Clone the repo: `git clone https://github.com/bibekpanditdev/localshare.git`
2. Open in Android Studio.
3. Run on device.

---
Created with ❤️ for fast, offline sharing.
