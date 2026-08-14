package com.example.localshare

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import java.io.File

class ServerService : Service() {

    companion object {
        const val CHANNEL_ID = "localshare_channel"
        const val NOTIF_ID = 1
        const val ACTION_STOP = "com.example.localshare.STOP"
        const val ACTION_STATE_CHANGED = "com.example.localshare.STATE_CHANGED"

        @Volatile
        var isRunning = false
            private set
            
        var currentPort = 8080
    }

    private var server: ShareServer? = null
    private var nsdHelper: NsdHelper? = null

    override fun onCreate() {
        super.onCreate()
        createChannel()
        nsdHelper = NsdHelper(this)
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            stopServer()
            stopSelf()
            return START_NOT_STICKY
        }

        startServer()
        startForeground(NOTIF_ID, buildNotification())
        return START_STICKY
    }

    private fun startServer() {
        if (server != null) return
        
        val prefs = getSharedPreferences("settings", Context.MODE_PRIVATE)
        val port = prefs.getInt("port", 8080)
        val fullStorage = prefs.getBoolean("full_storage", false)
        val hiddenStr = prefs.getString("hidden_paths", "") ?: ""
        val hiddenPaths = hiddenStr.split(",").map { it.trim() }.filter { it.isNotEmpty() }.toSet()
        
        currentPort = port
        
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

        server = ShareServer(applicationContext, port, roots, hiddenPaths, nsdHelper)
        try {
            server?.start(fi.iki.elonen.NanoHTTPD.SOCKET_READ_TIMEOUT, false)
            isRunning = true
            nsdHelper?.registerService(port)
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
        sendBroadcast(Intent(ACTION_STATE_CHANGED))
    }

    override fun onDestroy() {
        stopServer()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun createChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                CHANNEL_ID,
                "LocalShare server",
                NotificationManager.IMPORTANCE_LOW
            )
            val manager = getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(channel)
        }
    }

    private fun buildNotification(): Notification {
        val ip = NetUtils.getLocalIpAddress() ?: "unknown"
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
            .setContentText("http://$ip:$currentPort")
            .setSmallIcon(R.drawable.ic_notification)
            .setContentIntent(openPending)
            .addAction(0, "Stop", stopPending)
            .setOngoing(true)
            .build()
    }
}
