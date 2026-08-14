package com.example.localshare

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.net.wifi.WifiManager
import android.os.Build
import android.os.IBinder
import android.util.Log
import androidx.core.app.NotificationCompat
import java.io.File
import kotlin.random.Random

class ServerService : Service() {

    companion object {
        private const val TAG = "ServerService"
        const val CHANNEL_ID = "localshare_channel"
        const val RECEIVED_CHANNEL_ID = "localshare_received_channel"
        const val NOTIF_ID = 1
        const val ACTION_STOP = "com.example.localshare.STOP"
        const val ACTION_STATE_CHANGED = "com.example.localshare.STATE_CHANGED"

        @Volatile
        var isRunning = false
            private set
            
        var currentPort = 8080

        @Volatile
        var deviceName: String = Build.MODEL
            private set
    }

    private var server: ShareServer? = null
    private var nsdHelper: NsdHelper? = null
    private var multicastLock: WifiManager.MulticastLock? = null
    private var nextNotifId = 100

    override fun onCreate() {
        super.onCreate()
        createChannels()
        nsdHelper = NsdHelper(this)
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            stopServer()
            stopSelf()
            return START_NOT_STICKY
        }

        acquireMulticastLock()
        startServer()
        startForeground(NOTIF_ID, buildNotification())
        return START_STICKY
    }

    private fun acquireMulticastLock() {
        try {
            val wifi = applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
            val lock = wifi.createMulticastLock("localshare-mdns")
            lock.setReferenceCounted(true)
            lock.acquire()
            multicastLock = lock
            Log.i(TAG, "Multicast lock acquired")
        } catch (e: Exception) {
            Log.e(TAG, "Failed to acquire multicast lock", e)
        }
    }

    private fun releaseMulticastLock() {
        try {
            multicastLock?.let { if (it.isHeld) it.release() }
        } catch (e: Exception) {
            Log.e(TAG, "Failed to release multicast lock", e)
        }
        multicastLock = null
    }

    private fun startServer() {
        if (server != null) return
        
        val prefs = getSharedPreferences("settings", Context.MODE_PRIVATE)
        val port = prefs.getInt("port", 8080)
        val fullStorage = prefs.getBoolean("full_storage", false)
        val hiddenStr = prefs.getString("hidden_paths", "") ?: ""
        val hiddenPaths = hiddenStr.split(",").map { it.trim() }.filter { it.isNotEmpty() }.toSet()
        
        currentPort = port
        deviceName = "${Build.MODEL}-${Random.nextInt(1000, 9999)}"
        
        val roots = mutableMapOf<String, File>()
        
        // Always add the default app folder
        val appDir = File(android.os.Environment.getExternalStorageDirectory(), "LocalShare")
        if (!appDir.exists()) appDir.mkdirs()
        roots["LocalShare Folder"] = appDir
        
        // ONLY add full storage and SD card if the user enabled it
        if (fullStorage) {
            // Add full internal storage root
            roots["Internal Storage"] = android.os.Environment.getExternalStorageDirectory()
            
            // Add SD Card if available
            val externalFilesDirs = getExternalFilesDirs(null)
            if (externalFilesDirs.size > 1 && externalFilesDirs[1] != null) {
                val sdRoot = externalFilesDirs[1].absolutePath.split("/Android/")[0]
                roots["SD Card"] = File(sdRoot)
            }
        }

        server = ShareServer(applicationContext, port, roots, deviceName, hiddenPaths, nsdHelper) { fileName, sender ->
            showReceivedNotification(fileName, sender)
        }
        try {
            server?.start(fi.iki.elonen.NanoHTTPD.SOCKET_READ_TIMEOUT, false)
            isRunning = true
            nsdHelper?.registerService(port, deviceName)
            nsdHelper?.discoverServices()
            sendBroadcast(Intent(ACTION_STATE_CHANGED))
        } catch (e: Exception) {
            isRunning = false
            server = null
            sendBroadcast(Intent(ACTION_STATE_CHANGED))
        }
    }

    private fun stopServer() {
        server?.stop()
        server = null
        nsdHelper?.stop()
        isRunning = false
        releaseMulticastLock()
        sendBroadcast(Intent(ACTION_STATE_CHANGED))
    }

    override fun onDestroy() {
        stopServer()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun createChannels() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val manager = getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(
                NotificationChannel(CHANNEL_ID, "LocalShare status", NotificationManager.IMPORTANCE_LOW)
            )
            manager.createNotificationChannel(
                NotificationChannel(RECEIVED_CHANNEL_ID, "Files received", NotificationManager.IMPORTANCE_DEFAULT)
            )
        }
    }

    private fun showReceivedNotification(fileName: String, sender: String?) {
        val openIntent = Intent(this, MainActivity::class.java)
        val openPending = PendingIntent.getActivity(
            this, nextNotifId, openIntent,
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )
        val notif = NotificationCompat.Builder(this, RECEIVED_CHANNEL_ID)
            .setContentTitle("File received")
            .setContentText(if (sender != null) "$fileName from $sender" else fileName)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentIntent(openPending)
            .setAutoCancel(true)
            .build()
        val manager = getSystemService(NotificationManager::class.java)
        manager.notify(nextNotifId++, notif)
    }

    private fun buildNotification(): Notification {
        val stopIntent = Intent(this, ServerService::class.java).apply { action = ACTION_STOP }
        val stopPending = PendingIntent.getService(
            this, 0, stopIntent,
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )

        val openIntent = Intent(this, MainActivity::class.java)
        val openPending = PendingIntent.getActivity(
            this, 0, openIntent,
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )

        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("LocalShare is running")
            .setContentText("Visible as \"$deviceName\" at port $currentPort")
            .setSmallIcon(R.drawable.ic_notification)
            .setContentIntent(openPending)
            .addAction(0, "Stop", stopPending)
            .setOngoing(true)
            .build()
    }
}
