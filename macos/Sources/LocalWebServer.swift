import Foundation
import Network

final class LocalWebServer {
    let origin: URL
    private let listener: NWListener

    @MainActor
    init(store: MacStore) throws {
        let queue = DispatchQueue(label: "com.gamoffvad.sputnik.http", qos: .userInitiated)
        let publicRoot = store.root
        let storeRoot = store.dataDirectory
        let prefix = UUID().uuidString.lowercased()
        let parameters = NWParameters.tcp
        parameters.requiredLocalEndpoint = .hostPort(host: "127.0.0.1", port: .any)
        let listener = try NWListener(using: parameters)
        let ready = DispatchSemaphore(value: 0)
        listener.stateUpdateHandler = { state in
            switch state {
            case .ready, .failed, .cancelled: ready.signal()
            default: break
            }
        }
        listener.newConnectionHandler = { [weak listener] connection in
            guard let port = listener?.port?.rawValue else { connection.cancel(); return }
            connection.start(queue: queue)
            Self.receive(connection, bytes: Data(), publicRoot: publicRoot, storeRoot: storeRoot, prefix: prefix, port: port)
        }
        listener.start(queue: queue)
        guard ready.wait(timeout: .now() + 4) == .success,
              let port = listener.port,
              let url = URL(string: "http://127.0.0.1:\(port.rawValue)/\(prefix)/") else {
            listener.cancel()
            throw MacError("Не удалось открыть локальный интерфейс.")
        }
        self.listener = listener
        origin = url
    }

    deinit { listener.cancel() }

    private static func receive(_ connection: NWConnection, bytes: Data, publicRoot: URL, storeRoot: URL, prefix: String, port: UInt16) {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 8192) { data, _, complete, error in
            var request = bytes
            if let data = data { request.append(data) }
            if error != nil || request.count > 16384 {
                connection.cancel()
            } else if let end = request.range(of: Data("\r\n\r\n".utf8)) {
                let headers = String(decoding: request[..<end.lowerBound], as: UTF8.self)
                serve(connection, headers: headers, publicRoot: publicRoot, storeRoot: storeRoot, prefix: prefix, port: port)
            } else if complete {
                connection.cancel()
            } else {
                receive(connection, bytes: request, publicRoot: publicRoot, storeRoot: storeRoot, prefix: prefix, port: port)
            }
        }
    }

    private static func serve(_ connection: NWConnection, headers: String, publicRoot: URL, storeRoot: URL, prefix: String, port: UInt16) {
        let lines = headers.components(separatedBy: "\r\n")
        let request = (lines.first ?? "").split(separator: " ")
        let expected = "host: 127.0.0.1:\(port)"
        guard request.count == 3, request[0] == "GET" || request[0] == "HEAD",
              lines.dropFirst().contains(where: { $0.trimmingCharacters(in: .whitespaces).lowercased() == expected }),
              let target = request[1].split(separator: "?", maxSplits: 1).first?.removingPercentEncoding,
              target.hasPrefix("/\(prefix)/") else {
            respond(connection, status: "404 Not Found")
            return
        }
        let relative = String(target.dropFirst(prefix.count + 2))
        let parts = relative.split(separator: "/", omittingEmptySubsequences: false)
        guard !relative.isEmpty, parts.allSatisfy({ !$0.isEmpty && $0 != "." && $0 != ".." }),
              !relative.contains("\\"), !relative.contains("\0") else {
            respond(connection, status: "404 Not Found")
            return
        }
        let privateAsset = ["pets", "preview", "icons"].contains(String(parts[0]))
        let root = privateAsset ? storeRoot : publicRoot
        let file = root.appendingPathComponent(relative).resolvingSymlinksInPath()
        let extensionName = file.pathExtension.lowercased()
        let types = ["html": "text/html; charset=utf-8", "css": "text/css; charset=utf-8",
                     "js": "text/javascript; charset=utf-8", "json": "application/json",
                     "png": "image/png", "svg": "image/svg+xml", "jpg": "image/jpeg", "webp": "image/webp"]
        guard file.path.hasPrefix(root.resolvingSymlinksInPath().path + "/"),
              let mime = types[extensionName], !privateAsset || extensionName == "png",
              let info = try? file.resourceValues(forKeys: [.isRegularFileKey, .fileSizeKey]),
              info.isRegularFile == true, let size = info.fileSize, size <= 96 * 1024 * 1024 else {
            respond(connection, status: "404 Not Found")
            return
        }

        let head = request[0] == "HEAD"
        let body = head ? Data() : try? Data(contentsOf: file, options: .mappedIfSafe)
        guard let body = body else {
            respond(connection, status: "404 Not Found")
            return
        }
        respond(connection, status: "200 OK", type: mime, bytes: body, length: size)
    }

    private static func respond(_ connection: NWConnection, status: String, type: String = "text/plain",
                                bytes: Data = Data(), length: Int = 0) {
        let header = "HTTP/1.1 \(status)\r\nContent-Type: \(type)\r\nContent-Length: \(length)\r\n"
            + "Connection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n"
            + "Cross-Origin-Resource-Policy: same-origin\r\nReferrer-Policy: no-referrer\r\n\r\n"
        connection.send(content: Data(header.utf8), isComplete: bytes.isEmpty, completion: .contentProcessed { error in
            if error != nil || bytes.isEmpty {
                connection.cancel()
            } else {
                connection.send(content: bytes, isComplete: true, completion: .contentProcessed { _ in connection.cancel() })
            }
        })
    }
}
