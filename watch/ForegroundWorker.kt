package com.example.sundownresearch_watch.presentation

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.bluetooth.*
import android.bluetooth.le.*
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.content.pm.ServiceInfo
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.os.Build
import android.os.Handler
import android.os.HandlerThread
import android.os.ParcelUuid
import android.os.PowerManager
import android.util.Log
import androidx.annotation.RequiresApi
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import java.util.UUID

@RequiresApi(Build.VERSION_CODES.CUPCAKE)
class ForegroundWorker : Service(), SensorEventListener {

    companion object {
        private const val TAG = "ForegroundWorker"
        private const val NOTIF_ID = 1001
        private const val MAX_RR = 300
        private const val FOREGROUND_TYPES =
            ServiceInfo.FOREGROUND_SERVICE_TYPE_HEALTH or
                    ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE

        private const val WRITE_INTERVAL_MS = 10_000L
        private const val FILENAME = "sundown_research_sensor_data.csv"

        val SERVICE_UUID: UUID = UUID.fromString("0000FFF0-0000-1000-8000-00805F9B34FB")
        val CHAR_CSV_UUID: UUID = UUID.fromString("0000FFF1-0000-1000-8000-00805F9B34FB")
        private const val MAX_NOTIFY_BYTES = 180
    }

    // Sensors
    private lateinit var sensorManager: SensorManager
    private var hrSensor: Sensor? = null
    private var accelSensor: Sensor? = null

    private var lastHr: Float? = null
    private var lastRmssd: Double? = null
    private var lastSdnn: Double? = null
    private var lastStress = "null"
    private var lastAccel = floatArrayOf(0f, 0f, 0f)

    private val rrIntervals = mutableListOf<Long>()
    private var lastRrTimestamp: Long? = null

    private val ioThread = HandlerThread("io_thread").apply { start() }
    private val ioHandler = Handler(ioThread.looper)
    private val timeFormatter = DateTimeFormatter.ofPattern("HH:mm:ss")
    private var fileInitialized = false

    private lateinit var wakeLock: PowerManager.WakeLock

    // BLE
    private lateinit var btManager: BluetoothManager
    private var gattServer: BluetoothGattServer? = null
    private var advertiser: BluetoothLeAdvertiser? = null
    private var csvCharacteristic: BluetoothGattCharacteristic? = null
    private val connectedDevices = mutableSetOf<BluetoothDevice>()
    private var headerSent = false
    private var advertiseCallback: AdvertiseCallback? = null

    private val gattCallback = object : BluetoothGattServerCallback() {
        override fun onConnectionStateChange(device: BluetoothDevice, status: Int, newState: Int) {
            if (newState == BluetoothProfile.STATE_CONNECTED) {
                connectedDevices.add(device)
                headerSent = false
                Log.d(TAG, "BLE device connected: ${device.address}")
                sendHeaderIfNeeded()
            } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                connectedDevices.remove(device)
                Log.d(TAG, "BLE device disconnected: ${device.address}")
            }
        }
    }

    private val writeRunnable = Runnable { writeDataAndNotify() }

    override fun onCreate() {
        super.onCreate()
        sensorManager = getSystemService(Context.SENSOR_SERVICE) as SensorManager
        hrSensor = sensorManager.getDefaultSensor(Sensor.TYPE_HEART_RATE)
        accelSensor = sensorManager.getDefaultSensor(Sensor.TYPE_ACCELEROMETER)

        if (hrSensor == null || accelSensor == null) {
            Log.e(TAG, "Missing HR or Accel sensor, stopping service.")
            stopSelf()
            return
        }

        val pm = getSystemService(PowerManager::class.java)
        wakeLock = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "$TAG:WakeLock")
        wakeLock.acquire(10 * 60 * 1000L)

        startForegroundServiceCompat()
        registerSensors()
        setupGattServerAndAdvertise()

        if (ioThread.isAlive) {
            ioHandler.postDelayed(writeRunnable, WRITE_INTERVAL_MS)
        }
    }

    private fun startForegroundServiceCompat() {
        val chanId = "sensor_data_service"
        val mgr = getSystemService(NotificationManager::class.java)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            mgr.createNotificationChannel(
                NotificationChannel(chanId, "Sensor Data", NotificationManager.IMPORTANCE_LOW)
            )
        }

        val notif = NotificationCompat.Builder(this, chanId)
            .setSmallIcon(android.R.drawable.ic_popup_sync)
            .setContentTitle("Collecting sensor data")
            .setContentText("Heart & accelerometer active")
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .build()

        ServiceCompat.startForeground(this, NOTIF_ID, notif, FOREGROUND_TYPES)
        Log.d(TAG, "Foreground service started.")
    }

    private fun registerSensors() {
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.BODY_SENSORS)
            == PackageManager.PERMISSION_GRANTED
        ) {
            sensorManager.registerListener(this, hrSensor, SensorManager.SENSOR_DELAY_NORMAL)
            Log.d(TAG, "Heart rate sensor registered.")
        } else {
            Log.w(TAG, "BODY_SENSORS permission not granted.")
        }
        sensorManager.registerListener(this, accelSensor, SensorManager.SENSOR_DELAY_NORMAL)
        Log.d(TAG, "Accelerometer sensor registered.")
    }

    // === GATT setup with explicit permission checks ===
    private fun setupGattServerAndAdvertise() {
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED ||
            ContextCompat.checkSelfPermission(this, Manifest.permission.BLUETOOTH_ADVERTISE) != PackageManager.PERMISSION_GRANTED
        ) {
            Log.w(TAG, "Missing BLUETOOTH_* permissions")
            return
        }

        btManager = getSystemService(BLUETOOTH_SERVICE) as BluetoothManager
        val adapter = btManager.adapter ?: return
        if (!adapter.isEnabled) return

        gattServer = try {
            btManager.openGattServer(this, gattCallback)
        } catch (e: SecurityException) {
            Log.e(TAG, "openGattServer denied", e); null
        }

        val service = BluetoothGattService(SERVICE_UUID, BluetoothGattService.SERVICE_TYPE_PRIMARY)
        val characteristic = BluetoothGattCharacteristic(
            CHAR_CSV_UUID,
            BluetoothGattCharacteristic.PROPERTY_NOTIFY or BluetoothGattCharacteristic.PROPERTY_READ,
            BluetoothGattCharacteristic.PERMISSION_READ
        )
        val cccd = BluetoothGattDescriptor(
            UUID.fromString("00002902-0000-1000-8000-00805f9b34fb"),
            BluetoothGattDescriptor.PERMISSION_READ or BluetoothGattDescriptor.PERMISSION_WRITE
        )
        characteristic.addDescriptor(cccd)
        service.addCharacteristic(characteristic)
        csvCharacteristic = characteristic
        gattServer?.addService(service)

        advertiser = adapter.bluetoothLeAdvertiser
        if (advertiser == null) return

        val settings = AdvertiseSettings.Builder()
            .setAdvertiseMode(AdvertiseSettings.ADVERTISE_MODE_LOW_LATENCY)
            .setTxPowerLevel(AdvertiseSettings.ADVERTISE_TX_POWER_HIGH)
            .setConnectable(true)
            .build()

        val data = AdvertiseData.Builder()
            .addServiceUuid(ParcelUuid(SERVICE_UUID))
            .setIncludeDeviceName(true)
            .build()

        advertiseCallback = object : AdvertiseCallback() {
            override fun onStartSuccess(settingsInEffect: AdvertiseSettings?) {
                Log.d(TAG, "BLE advertising started")
            }
            override fun onStartFailure(errorCode: Int) {
                Log.e(TAG, "BLE advertising failed: $errorCode")
            }
        }
        try {
            advertiser?.startAdvertising(settings, data, advertiseCallback)
        } catch (e: SecurityException) {
            Log.e(TAG, "startAdvertising denied", e)
        }
    }

    private fun notifyRow(row: String) {
        val ch = csvCharacteristic ?: return
        if (connectedDevices.isEmpty()) return

        val bytes = row.toByteArray(Charsets.UTF_8)
        var offset = 0
        while (offset < bytes.size) {
            val end = (offset + MAX_NOTIFY_BYTES).coerceAtMost(bytes.size)
            val slice = bytes.copyOfRange(offset, end)
            ch.value = slice
            connectedDevices.forEach { dev ->
                try {
                    gattServer?.notifyCharacteristicChanged(dev, ch, false)
                } catch (e: SecurityException) {
                    Log.e(TAG, "notifyCharacteristicChanged denied", e)
                }
            }
            offset = end
        }
    }

    private fun sendHeaderIfNeeded() {
        if (!headerSent) {
            notifyRow("Timestamp,HeartRate,RMSSD,SDNN,StressLevel,AccelX,AccelY,AccelZ\n")
            headerSent = true
            Log.d(TAG, "CSV header sent.")
        }
    }

    override fun onSensorChanged(event: SensorEvent) {
        when (event.sensor.type) {
            Sensor.TYPE_HEART_RATE -> {
                lastHr = event.values[0]
                Log.d(TAG, "HeartRate=$lastHr")
            }
            Sensor.TYPE_ACCELEROMETER -> {
                lastAccel = event.values.clone()
                Log.d(TAG, "Accel=${lastAccel.joinToString()}")
            }
            else -> {
                Log.d(TAG, "Other sensor event: type=${event.sensor.type}, values=${event.values.joinToString()}")
            }
        }
    }

    override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) = Unit

    private fun writeDataAndNotify() {
        val timestamp = LocalTime.now().format(timeFormatter)
        val hr = lastHr ?: -1f
        val rmssd = lastRmssd?.toString() ?: "null"
        val sdnn = lastSdnn?.toString() ?: "null"
        val stress = lastStress
        val accelX = lastAccel[0]
        val accelY = lastAccel[1]
        val accelZ = lastAccel[2]

        val row = "$timestamp,$hr,$rmssd,$sdnn,$stress,$accelX,$accelY,$accelZ\n"

        Log.d(TAG, "Row=$row")
        notifyRow(row)

        // schedule again
        if (ioThread.isAlive) {
            ioHandler.postDelayed(writeRunnable, WRITE_INTERVAL_MS)
        }
    }

    private fun releaseWakeLock() {
        if (this::wakeLock.isInitialized && wakeLock.isHeld) wakeLock.release()
    }

    override fun onTaskRemoved(rootIntent: Intent?) {
        cleanupAndStop()
        super.onTaskRemoved(rootIntent)
    }

    override fun onDestroy() {
        cleanupAndStop()
        super.onDestroy()
    }

    private fun cleanupAndStop() {
        try { sensorManager.unregisterListener(this) } catch (_: Exception) {}
        try { ioHandler.removeCallbacks(writeRunnable); ioThread.quitSafely() } catch (_: Exception) {}
        try {
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.BLUETOOTH_ADVERTISE) == PackageManager.PERMISSION_GRANTED) {
                advertiser?.stopAdvertising(advertiseCallback)
                Log.d(TAG, "BLE advertising stopped")
            }
        } catch (e: SecurityException) {
            Log.e(TAG, "stopAdvertising denied", e)
        }
        try { gattServer?.close() } catch (_: Exception) {}
        releaseWakeLock()
        stopSelf()
        Log.d(TAG, "Service stopped and cleaned up.")
    }

    override fun onBind(intent: Intent?) = null
}
