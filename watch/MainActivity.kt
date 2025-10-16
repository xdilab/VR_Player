package com.example.sundownresearch_watch.presentation

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.result.ActivityResultLauncher
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat

class MainActivity : ComponentActivity() {

    private lateinit var sensorPermsLauncher: ActivityResultLauncher<Array<String>>

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Register permission launcher
        sensorPermsLauncher = registerForActivityResult(
            ActivityResultContracts.RequestMultiplePermissions()
        ) { results ->
            if (results.values.all { it }) {
                startForegroundWorker()
            } else {
                // If any permission denied -> close app
                finish()
            }
        }

        requestAllPermissions()
    }

    private fun requestAllPermissions() {
        val needed = mutableListOf<String>()

        // Sensors
        needed.add(Manifest.permission.BODY_SENSORS)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            needed.add(Manifest.permission.ACTIVITY_RECOGNITION)
        }

        // Notifications
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            needed.add(Manifest.permission.POST_NOTIFICATIONS)
        }

        // Bluetooth runtime permissions (Android 12+)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            needed.add(Manifest.permission.BLUETOOTH_CONNECT)
            needed.add(Manifest.permission.BLUETOOTH_SCAN)
            needed.add(Manifest.permission.BLUETOOTH_ADVERTISE)
        }

        val missing = needed.filter {
            ContextCompat.checkSelfPermission(this, it) != PackageManager.PERMISSION_GRANTED
        }

        if (missing.isNotEmpty()) {
            sensorPermsLauncher.launch(missing.toTypedArray())
        } else {
            startForegroundWorker()
        }
    }

    private fun startForegroundWorker() {
        ContextCompat.startForegroundService(
            this,
            Intent(this, ForegroundWorker::class.java)
        )
        finish() // Close MainActivity after starting the service
    }
}
