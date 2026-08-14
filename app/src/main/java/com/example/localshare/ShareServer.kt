package com.example.localshare

import android.content.Context
import fi.iki.elonen.NanoHTTPD
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.FileInputStream
import java.net.URLConnection
import java.net.URLEncoder

/**
 * A local HTTP file server that exposes [rootDir] for full read/write sync:
 * list, download, upload, mkdir, delete, rename. Serves an embedded web UI
 * at "/" so any browser on the same network can use it with no app install.
 */
class ShareServer(
    private val context: Context,
    port: Int,
    private val roots: Map<String, File>,
    private val hiddenPaths: Set<String> = emptySet(),
    private val nsdHelper: NsdHelper? = null
) : NanoHTTPD(port) {

    // Simple in-memory tracker for delete authority (resets on server restart)
    private val uploadTokens = HashMap<String, String>()

    init {
        // Use a thread pool for better performance (P2P-like smoothness)
        setAsyncRunner(DefaultAsyncRunner())
        roots.values.forEach { if (!it.exists()) it.mkdirs() }
    }

    override fun serve(session: IHTTPSession): Response {
        return try {
            val uri = session.uri
            when {
                uri == "/" || uri == "/index.html" -> serveIndex()
                uri == "/api/list" && session.method == Method.GET -> apiList(session)
                uri == "/api/download" && session.method == Method.GET -> apiDownload(session)
                uri == "/api/upload" && session.method == Method.POST -> apiUpload(session)
                uri == "/api/mkdir" && session.method == Method.POST -> apiMkdir(session)
                uri == "/api/delete" && session.method == Method.POST -> apiDelete(session)
                uri == "/api/rename" && session.method == Method.POST -> apiRename(session)
                uri == "/api/roots" && session.method == Method.GET -> apiRoots()
                uri == "/api/peers" && session.method == Method.GET -> apiPeers()
                else -> newFixedLengthResponse(Response.Status.NOT_FOUND, "text/plain", "Not found")
            }
        } catch (e: Exception) {
            jsonError(Response.Status.INTERNAL_ERROR, e.message ?: "Server error")
        }
    }

    // ---------- static UI ----------

    private fun serveIndex(): Response {
        val input = context.assets.open("www/index.html")
        return newChunkedResponse(Response.Status.OK, "text/html", input)
    }

    // ---------- helpers ----------

    private fun resolveSafe(relPath: String?, rootName: String?): File? {
        val root = roots[rootName ?: "Default"] ?: roots.values.first()
        val clean = (relPath ?: "").trim('/', ' ')
        val target = if (clean.isEmpty()) root else File(root, clean)
        val rootCanonical = root.canonicalFile
        val targetCanonical = target.canonicalFile
        return if (targetCanonical == rootCanonical || targetCanonical.path.startsWith(rootCanonical.path + File.separator)) {
            targetCanonical
        } else {
            null
        }
    }

    private fun getRootDir(rootName: String?): File {
        return roots[rootName ?: "Default"] ?: roots.values.first()
    }

    private fun jsonError(status: Response.Status, message: String): Response {
        val obj = JSONObject()
        obj.put("error", message)
        return newFixedLengthResponse(status, "application/json", obj.toString())
    }

    private fun jsonOk(obj: JSONObject = JSONObject()): Response {
        obj.put("ok", true)
        return newFixedLengthResponse(Response.Status.OK, "application/json", obj.toString())
    }

    // ---------- API: list ----------

    private fun apiRoots(): Response {
        val arr = JSONArray()
        roots.keys.forEach { arr.put(it) }
        return newFixedLengthResponse(Response.Status.OK, "application/json", arr.toString())
    }

    private fun apiPeers(): Response {
        val obj = JSONObject()
        nsdHelper?.discoveredPeers?.forEach { (name, url) ->
            obj.put(name, url)
        }
        return newFixedLengthResponse(Response.Status.OK, "application/json", obj.toString())
    }

    private fun apiList(session: IHTTPSession): Response {
        val relPath = session.parameters["path"]?.firstOrNull() ?: ""
        val category = session.parameters["category"]?.firstOrNull() ?: "all"
        val rootName = session.parameters["root"]?.firstOrNull()
        
        val targetRoot = getRootDir(rootName)
        val dir = resolveSafe(relPath, rootName) ?: return jsonError(Response.Status.FORBIDDEN, "Invalid path")
        if (!dir.exists() || !dir.isDirectory) return jsonError(Response.Status.NOT_FOUND, "Folder not found")

        val arr = JSONArray()
        val children = if (category == "all") {
            dir.listFiles() ?: emptyArray()
        } else {
            val allFiles = mutableListOf<File>()
            findFilesByCategory(targetRoot, category, allFiles)
            allFiles.toTypedArray()
        }

        val sortedChildren = if (category == "all") {
            children.sortedWith(compareBy({ !it.isDirectory }, { it.name.lowercase() }))
        } else {
            children.sortedByDescending { it.lastModified() }
        }

        sortedChildren.forEach { f ->
            val relFile = f.absolutePath.removePrefix(targetRoot.absolutePath).trim(File.separatorChar)
            val webPath = relFile.replace(File.separatorChar, '/')
            
            // Skip hidden files/folders
            if (hiddenPaths.any { webPath == it || webPath.startsWith("$it/") }) return@forEach

            val entry = JSONObject()
            entry.put("name", f.name)
            entry.put("isDir", f.isDirectory)
            entry.put("size", if (f.isFile) f.length() else 0)
            entry.put("modified", f.lastModified())
            entry.put("path", webPath)

            arr.put(entry)
        }

        val result = JSONObject()
        result.put("path", relPath.trim('/'))
        result.put("category", category)
        result.put("root", rootName ?: roots.keys.first())
        result.put("entries", arr)
        return newFixedLengthResponse(Response.Status.OK, "application/json", result.toString())
    }

    private fun findFilesByCategory(dir: File, category: String, result: MutableList<File>) {
        val files = dir.listFiles() ?: return
        for (f in files) {
            if (f.isDirectory) {
                findFilesByCategory(f, category, result)
            } else {
                if (matchesCategory(f, category)) {
                    result.add(f)
                }
            }
        }
    }

    private fun matchesCategory(file: File, category: String): Boolean {
        val ext = file.extension.lowercase()
        return when (category) {
            "images" -> ext in listOf("jpg", "jpeg", "png", "gif", "bmp", "webp")
            "videos" -> ext in listOf("mp4", "mkv", "avi", "mov", "wmv", "flv")
            "music" -> ext in listOf("mp3", "wav", "ogg", "m4a", "flac")
            "docs" -> ext in listOf("pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "pdf")
            else -> false
        }
    }

    // ---------- API: download ----------

    private fun apiDownload(session: IHTTPSession): Response {
        val relPath = session.parameters["path"]?.firstOrNull() ?: ""
        val rootName = session.parameters["root"]?.firstOrNull()
        val isPreview = session.parameters["preview"]?.firstOrNull() == "1"
        val file = resolveSafe(relPath, rootName) ?: return jsonError(Response.Status.FORBIDDEN, "Invalid path")
        if (!file.exists() || !file.isFile) return jsonError(Response.Status.NOT_FOUND, "File not found")

        val mime = URLConnection.guessContentTypeFromName(file.name) ?: "application/octet-stream"
        
        // Handle Range requests for video/audio seeking
        var response: Response
        val rangeHeader = session.headers["range"]
        if (rangeHeader != null && rangeHeader.startsWith("bytes=")) {
            try {
                val rangeValue = rangeHeader.substring(6)
                val parts = rangeValue.split("-")
                val start = parts[0].toLong()
                val fileLen = file.length()
                
                // If end is not specified, we serve a chunk of 2MB for smoother buffering on large videos
                val chunkSize = 2 * 1024 * 1024L 
                var end = if (parts.size > 1 && parts[1].isNotEmpty()) parts[1].toLong() else start + chunkSize
                if (end >= fileLen) end = fileLen - 1
                
                if (start >= fileLen) {
                    response = newFixedLengthResponse(Response.Status.RANGE_NOT_SATISFIABLE, "text/plain", "")
                    response.addHeader("Content-Range", "bytes */$fileLen")
                } else {
                    val dataLen = end - start + 1
                    val inputStream = object : FileInputStream(file) {
                        override fun available(): Int = dataLen.toInt()
                    }
                    inputStream.channel.position(start)
                    
                    response = newFixedLengthResponse(Response.Status.PARTIAL_CONTENT, mime, inputStream, dataLen)
                    response.addHeader("Content-Range", "bytes $start-$end/$fileLen")
                    response.addHeader("Content-Length", dataLen.toString())
                    response.addHeader("Cache-Control", "no-cache")
                }
            } catch (e: Exception) {
                response = newFixedLengthResponse(Response.Status.OK, mime, FileInputStream(file), file.length())
            }
        } else {
            response = newFixedLengthResponse(Response.Status.OK, mime, FileInputStream(file), file.length())
        }
        
        if (!isPreview) {
            val encodedName = URLEncoder.encode(file.name, "UTF-8").replace("+", "%20")
            response.addHeader("Content-Disposition", "attachment; filename*=UTF-8''$encodedName")
        }
        
        response.addHeader("Accept-Ranges", "bytes")
        return response
    }

    // ---------- API: upload ----------

    private fun apiUpload(session: IHTTPSession): Response {
        val relPath = session.parameters["path"]?.firstOrNull() ?: ""
        val rootName = session.parameters["root"]?.firstOrNull()
        val targetDir = resolveSafe(relPath, rootName) ?: return jsonError(Response.Status.FORBIDDEN, "Invalid path")
        if (!targetDir.exists()) targetDir.mkdirs()

        val files = HashMap<String, String>()
        session.parseBody(files)

        val params = session.parameters
        val tmpPath = files["file"] ?: return jsonError(Response.Status.BAD_REQUEST, "No file uploaded")
        val originalName = params["file"]?.firstOrNull()?.let { File(it).name }
            ?: "upload_${System.currentTimeMillis()}"

        val safeName = sanitizeFileName(originalName)
        var destFile = File(targetDir, safeName)
        destFile = uniqueIfExists(destFile)

        val source = File(tmpPath)
        source.inputStream().use { input ->
            destFile.outputStream().use { output ->
                // Ultra-Extreme 16MB buffer for peak WiFi/LAN performance
                val buffer = ByteArray(16 * 1024 * 1024)
                var bytesRead: Int
                while (input.read(buffer).also { bytesRead = it } != -1) {
                    output.write(buffer, 0, bytesRead)
                }
                output.flush() // Force write to disk
            }
        }
        source.delete()

        // Generate a token so the uploader can delete this file later
        val token = java.util.UUID.randomUUID().toString()
        val targetRoot = getRootDir(rootName)
        val webPath = destFile.absolutePath.removePrefix(targetRoot.absolutePath).trim(File.separatorChar).replace(File.separatorChar, '/')
        uploadTokens[webPath] = token

        val result = JSONObject()
        result.put("name", destFile.name)
        result.put("token", token)
        return jsonOk(result)
    }

    private fun sanitizeFileName(name: String): String {
        return name.replace(Regex("[\\\\/:*?\"<>|]"), "_").ifBlank { "file" }
    }

    private fun uniqueIfExists(file: File): File {
        if (!file.exists()) return file
        val base = file.nameWithoutExtension
        val ext = file.extension
        var i = 1
        var candidate: File
        do {
            candidate = if (ext.isNotEmpty()) File(file.parentFile, "$base ($i).$ext") else File(file.parentFile, "$base ($i)")
            i++
        } while (candidate.exists())
        return candidate
    }

    // ---------- API: mkdir ----------

    private fun apiMkdir(session: IHTTPSession): Response {
        val relPath = session.parameters["path"]?.firstOrNull() ?: ""
        val rootName = session.parameters["root"]?.firstOrNull()
        val name = session.parameters["name"]?.firstOrNull() ?: return jsonError(Response.Status.BAD_REQUEST, "Missing name")
        val parent = resolveSafe(relPath, rootName) ?: return jsonError(Response.Status.FORBIDDEN, "Invalid path")
        val newDir = File(parent, sanitizeFileName(name))
        if (newDir.exists()) return jsonError(Response.Status.CONFLICT, "Already exists")
        if (!newDir.mkdirs()) return jsonError(Response.Status.INTERNAL_ERROR, "Could not create folder")
        return jsonOk()
    }

    // ---------- API: delete ----------

    private fun apiDelete(session: IHTTPSession): Response {
        val relPath = session.parameters["path"]?.firstOrNull() ?: ""
        val rootName = session.parameters["root"]?.firstOrNull()
        val token = session.parameters["token"]?.firstOrNull() ?: ""
        
        if (relPath.isBlank()) return jsonError(Response.Status.FORBIDDEN, "Cannot delete root")
        val target = resolveSafe(relPath, rootName) ?: return jsonError(Response.Status.FORBIDDEN, "Invalid path")
        if (!target.exists()) return jsonError(Response.Status.NOT_FOUND, "Not found")
        
        val webPath = relPath.trim('/')
        if (uploadTokens.containsKey(webPath)) {
            if (uploadTokens[webPath] != token) {
                return jsonError(Response.Status.FORBIDDEN, "You don't have authority to delete this file")
            }
        }
        
        val success = target.deleteRecursively()
        if (success) uploadTokens.remove(webPath)

        return if (success) jsonOk() else jsonError(Response.Status.INTERNAL_ERROR, "Could not delete")
    }

    // ---------- API: rename ----------

    private fun apiRename(session: IHTTPSession): Response {
        val relPath = session.parameters["path"]?.firstOrNull() ?: ""
        val rootName = session.parameters["root"]?.firstOrNull()
        val newName = session.parameters["newName"]?.firstOrNull() ?: return jsonError(Response.Status.BAD_REQUEST, "Missing newName")
        if (relPath.isBlank()) return jsonError(Response.Status.FORBIDDEN, "Cannot rename root")
        val target = resolveSafe(relPath, rootName) ?: return jsonError(Response.Status.FORBIDDEN, "Invalid path")
        if (!target.exists()) return jsonError(Response.Status.NOT_FOUND, "Not found")
        val dest = uniqueIfExists(File(target.parentFile, sanitizeFileName(newName)))
        val success = target.renameTo(dest)
        return if (success) jsonOk() else jsonError(Response.Status.INTERNAL_ERROR, "Could not rename")
    }
}
