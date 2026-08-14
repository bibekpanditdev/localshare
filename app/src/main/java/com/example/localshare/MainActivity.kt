package com.example.localshare

import android.Manifest
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.view.View
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import com.example.localshare.databinding.ActivityMainBinding

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private var currentUrl: String? = null

    private val notifPermLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted -> if (granted) requestStart() }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val prefs = getSharedPreferences("settings", Context.MODE_PRIVATE)
        binding.fullStorageSwitch.isChecked = prefs.getBoolean("full_storage", false)
        binding.portEdit.setText(prefs.getInt("port", 8080).toString())
        binding.hiddenPathsEdit.setText(prefs.getString("hidden_paths", ""))

        binding.toggleButton.setOnClickListener {
            if (ServerService.isRunning) {
                stopServer()
            } else {
                saveSettings()
                requestStart()
            }
        }

        binding.copyButton.setOnClickListener {
            currentUrl?.let { url ->
                val clipboard = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
                clipboard.setPrimaryClip(ClipData.newPlainText("LocalShare URL", url))
                Toast.makeText(this, "Link copied", Toast.LENGTH_SHORT).show()
            }
        }

        refreshUi()
    }

    private fun saveSettings() {
        val port = binding.portEdit.text.toString().toIntOrNull() ?: 8080
        val fullStorage = binding.fullStorageSwitch.isChecked
        val hiddenPaths = binding.hiddenPathsEdit.text.toString()
        getSharedPreferences("settings", Context.MODE_PRIVATE).edit()
            .putInt("port", port)
            .putBoolean("full_storage", fullStorage)
            .putString("hidden_paths", hiddenPaths)
            .apply()
    }

    override fun onResume() {
        super.onResume()
        refreshUi()
    }

    private fun requestStart() {
        val needsNotif = Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        
        val needsStorage = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            !android.os.Environment.isExternalStorageManager()
        } else {
            ContextCompat.checkSelfPermission(this, Manifest.permission.WRITE_EXTERNAL_STORAGE) != PackageManager.PERMISSION_GRANTED
        }

        if (needsNotif) {
            notifPermLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
        } else if (needsStorage) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                try {
                    val intent = Intent(android.provider.Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION)
                    intent.addCategory("android.intent.category.DEFAULT")
                    intent.data = Uri.parse("package:${packageName}")
                    startActivity(intent)
                } catch (e: Exception) {
                    val intent = Intent(android.provider.Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION)
                    startActivity(intent)
                }
            } else {
                notifPermLauncher.launch(Manifest.permission.WRITE_EXTERNAL_STORAGE)
            }
        } else {
            startServer()
        }
    }

    private fun startServer() {
        val intent = Intent(this, ServerService::class.java)
        ContextCompat.startForegroundService(this, intent)
        binding.toggleButton.postDelayed({ 
            refreshUi() 
            if (!ServerService.isRunning) {
                Toast.makeText(this, "Failed to start server. Port might be in use.", Toast.LENGTH_LONG).show()
            }
        }, 800)
    }

    private fun stopServer() {
        val intent = Intent(this, ServerService::class.java).apply {
            action = ServerService.ACTION_STOP
        }
        startService(intent)
        binding.toggleButton.postDelayed({ refreshUi() }, 500)
    }

    private fun refreshUi() {
        val running = ServerService.isRunning
        binding.toggleButton.text = if (running) "Stop Sharing" else "Start Sharing"
        binding.fullStorageSwitch.isEnabled = !running
        binding.portEdit.isEnabled = !running
        binding.hiddenPathsEdit.isEnabled = !running
        
        if (running) {
            binding.statusIcon.imageTintList = android.content.res.ColorStateList.valueOf(android.graphics.Color.parseColor("#10b981"))
            (binding.statusIcon.parent as? android.view.View)?.backgroundTintList = 
                android.content.res.ColorStateList.valueOf(android.graphics.Color.parseColor("#dcfce7"))
            
            val ips = NetUtils.getLocalIpAddresses()
            if (ips.isNotEmpty()) {
                val prefs = getSharedPreferences("settings", Context.MODE_PRIVATE)
                val port = prefs.getInt("port", 8080)
                val isFull = prefs.getBoolean("full_storage", false)
                
                // Construct multiple URLs if there are multiple interfaces
                val urls = ips.map { "http://$it:$port" }
                currentUrl = urls.first()
                
                binding.statusText.text = if (isFull) "Sharing: All Drives" else "Sharing: App Folder"
                binding.urlText.text = urls.joinToString("\n")
                binding.copyButton.visibility = View.VISIBLE
                binding.copyButton.isEnabled = true
            } else {
                binding.statusText.text = "No Connection Detected"
                binding.urlText.text = ""
                binding.copyButton.visibility = View.GONE
            }
        } else {
            binding.statusIcon.imageTintList = android.content.res.ColorStateList.valueOf(android.graphics.Color.parseColor("#9ca3af"))
            (binding.statusIcon.parent as? android.view.View)?.backgroundTintList = 
                android.content.res.ColorStateList.valueOf(android.graphics.Color.parseColor("#f3f4f6"))

            binding.statusText.text = "Server is Inactive"
            binding.urlText.text = ""
            binding.copyButton.visibility = View.GONE
        }
    }
}
