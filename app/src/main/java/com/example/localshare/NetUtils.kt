package com.example.localshare

import java.net.Inet4Address
import java.net.NetworkInterface
import java.util.Collections

object NetUtils {

    /**
     * Returns a list of the device's local IPv4 addresses on all active interfaces.
     */
    fun getLocalIpAddresses(): List<String> {
        val addresses = mutableListOf<String>()
        try {
            val interfaces = Collections.list(NetworkInterface.getNetworkInterfaces())
            for (intf in interfaces) {
                if (!intf.isUp || intf.isLoopback) continue
                val addrs = Collections.list(intf.inetAddresses)
                for (addr in addrs) {
                    if (addr is Inet4Address && !addr.isLoopbackAddress) {
                        val ip = addr.hostAddress ?: continue
                        if (!ip.startsWith("127.")) {
                            addresses.add(ip)
                        }
                    }
                }
            }
        } catch (e: Exception) {
            // ignore
        }
        // Prioritize common hotspot/WiFi ranges
        return addresses.sortedWith(compareByDescending { it.startsWith("192.168.43.") || it.startsWith("192.168.1.") })
    }

    @Deprecated("Use getLocalIpAddresses", ReplaceWith("getLocalIpAddresses().firstOrNull()"))
    fun getLocalIpAddress(): String? = getLocalIpAddresses().firstOrNull()
}
