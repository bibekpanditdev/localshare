package com.example.localshare

import java.net.Inet4Address
import java.net.NetworkInterface
import java.util.Collections

object NetUtils {

    /**
     * Returns a unique list of the device's local IPv4 addresses on all active interfaces.
     */
    fun getLocalIpAddresses(): List<String> {
        val addresses = mutableSetOf<String>()
        try {
            val interfaces = Collections.list(NetworkInterface.getNetworkInterfaces())
            // Sort interfaces: prefer wlan (WiFi) and ap (Hotspot) over others
            val sortedInterfaces = interfaces.sortedWith(compareByDescending { 
                val name = it.name.lowercase()
                name.contains("wlan") || name.contains("ap") || name.contains("rndis")
            })

            for (intf in sortedInterfaces) {
                if (!intf.isUp || intf.isLoopback) continue
                val addrs = Collections.list(intf.inetAddresses)
                for (addr in addrs) {
                    if (addr is Inet4Address && !addr.isLoopbackAddress) {
                        val ip = addr.hostAddress ?: continue
                        // Filter out loopback and link-local (if any slipped through)
                        if (!ip.startsWith("127.") && !ip.startsWith("169.254.")) {
                            addresses.add(ip)
                        }
                    }
                }
            }
        } catch (e: Exception) {
            // ignore
        }
        
        // Return sorted list: prioritize common WiFi/Hotspot/Tethering ranges
        return addresses.toList().sortedWith(compareByDescending { ip ->
            ip.startsWith("192.168.43.") || // Common Android Hotspot
            ip.startsWith("192.168.1.") ||  // Common Home WiFi
            ip.startsWith("192.168.0.") ||  // Common Home WiFi
            ip.startsWith("192.168.42.")    // Common USB Tethering
        })
    }

    @Deprecated("Use getLocalIpAddresses", ReplaceWith("getLocalIpAddresses().firstOrNull()"))
    fun getLocalIpAddress(): String? = getLocalIpAddresses().firstOrNull()
}
