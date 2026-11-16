import AppKit
import CryptoKit
import Foundation
import ImageIO
import Security
import ServiceManagement
import UniformTypeIdentifiers

@MainActor
enum MacBridge {
    private static var sending = false
    private static let session: URLSession = {
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 18
        config.timeoutIntervalForResource = 35
        config.httpCookieStorage = nil
        return URLSession(configuration: config)
    }()
    private static let keychainQuery: [String: Any] = [
        kSecClass as String: kSecClassGenericPassword,
        kSecAttrService as String: "com.gamoffvad.sputnik", kSecAttrAccount as String: "model-api"
    ]

    static func execute(_ action: String, payload: Any?, store: MacStore, app: AppDelegate) async throws -> Any? {
        let values = payload as? [String: Any] ?? [:]
        switch action {
        case "load":
            return store.readWorkspace()
        case "save":
            var state = values
            if let font = state["font"] as? String,
               !NSFontManager.shared.availableFontFamilies.contains(where: { $0.caseInsensitiveCompare(font) == .orderedSame }) {
                throw MacError("Выберите установленный шрифт.")
            }
            if state["appearanceMode"] as? String == "custom" {
                let colors = state["customTheme"] as? [String: String] ?? [:]
                guard ["accent", "surface", "background"].allSatisfy({
                    colors[$0]?.range(of: "^#[A-Fa-f0-9]{6}$", options: .regularExpression) != nil
                }) else { throw MacError("Укажите цвета в формате #RRGGBB.") }
            }
            state["messages"] = store.readWorkspace()["messages"] ?? []
            try store.writeWorkspace(state)
            app.refreshPet()
        case "appearanceFonts":
            return NSFontManager.shared.availableFontFamilies.sorted()
        case "autoStart":
            return SMAppService.mainApp.status == .enabled
        case "setAutoStart":
            guard let value = payload as? Bool else { throw MacError("Некорректная настройка автозапуска.") }
            let service = SMAppService.mainApp
            if value && service.status == .notRegistered { try service.register() }
            if !value && (service.status == .enabled || service.status == .requiresApproval) { try service.unregister() }
            if value && service.status == .requiresApproval {
                SMAppService.openSystemSettingsLoginItems()
                throw MacError("Разрешите автозапуск «Спутника» в системных настройках.")
            }
            return service.status == .enabled
        case "selectPet":
            guard let path = try store.choosePet() else { return nil }
            return try await store.stagePet(path)
        case "installPet":
            guard let token = payload as? String else { throw MacError("Откройте файл питомца снова.") }
            return try store.installPet(token)
        case "pets":
            return store.installedPetManifests().compactMap { pet in
                (pet["id"] as? String).map { store.webPet(pet, path: "pets/\($0)") }
            }
        case "pickApp":
            let panel = NSOpenPanel()
            panel.allowedContentTypes = [.applicationBundle]
            panel.treatsFilePackagesAsDirectories = false
            panel.canChooseDirectories = false
            panel.allowsMultipleSelection = false
            panel.directoryURL = FileManager.default.urls(for: .applicationDirectory, in: .localDomainMask).first
            return panel.runModal() == .OK ? panel.url?.path : nil
        case "open":
            try open(kind: values["kind"] as? String ?? "", target: values["target"] as? String ?? "")
        case "getModel":
            let saved = store.readSettings("model.json")
            return ["url": saved["url"] ?? "", "model": saved["model"] ?? "", "hasKey": readKey() != nil]
        case "saveModel":
            let settings = try model(values, store: store)
            try writeKey(settings.key)
            try store.writeSettings(["url":settings.base.absoluteString, "model":settings.model], name: "model.json")
        case "testModel":
            let settings = try model(values, store: store)
            _ = try await send(settings, messages: [["role":"user", "content":"Ответь одним словом: OK."]])
            return "Подключение работает"
        case "chat":
            return try await sendChat(payload as? String ?? "", store: store, app: app)
        case "siteIcon":
            return try await siteIcon(payload as? String ?? "", store: store)
        case "siteIconMask":
            return try iconMask(payload as? String ?? "", store: store)
        case "shareChannels":
            return store.readSettings("share.json")["channels"] ?? [
                ["name":"Почта", "template":"mailto:?body={text}"],
                ["name":"Telegram", "template":"tg://msg?text={text}"]
            ]
        case "saveShareChannels":
            guard let channels = payload as? [[String: String]], channels.count <= 32 else {
                throw MacError("Можно сохранить до 32 способов отправки.")
            }
            try store.writeSettings(["channels":channels], name:"share.json")
        case "hotkeys":
            return ["sections": AppDelegate.sections.map { ["title": $0.label, "action": $0.id] },
                    "bindings": app.hotkeyBindings()]
        case "setHotkey":
            guard let section = values["section"] as? String,
                  let modifiers = values["modifiers"] as? Int, let key = values["key"] as? Int else {
                throw MacError("Укажите поддерживаемое сочетание клавиш.")
            }
            try app.setHotkey(section, modifiers: modifiers, key: key)
        default:
            throw MacError("Неизвестное действие.")
        }
        return nil
    }

    static func open(kind: String, target: String) throws {
        let url: URL
        if kind == "site" {
            url = try httpURL(target)
        } else {
            guard kind == "app", target.hasPrefix("/"), target.lowercased().hasSuffix(".app"),
                  FileManager.default.fileExists(atPath: target) else {
                throw MacError("Выберите существующее приложение macOS (.app).")
            }
            url = URL(fileURLWithPath: target)
        }
        guard NSWorkspace.shared.open(url) else { throw MacError("Не удалось открыть ярлык.") }
    }

    static func sendChat(_ input: String, store: MacStore, app: AppDelegate) async throws -> String {
        let text = input.trimmingCharacters(in: .whitespacesAndNewlines)
        guard (1...10000).contains(text.count) else { throw MacError("Введите сообщение длиной до 10 000 символов.") }
        guard !sending else { throw MacError("Дождитесь ответа на предыдущее сообщение.") }
        let settings = try model([:], store: store)
        sending = true
        defer { sending = false }
        let history = try append("user", text: text, store: store, app: app)
        let trimmed = history.suffix(40).map { item in
            ["role":item["role"] ?? "user", "content":String((item["content"] ?? "").prefix(20_000))]
        }
        let reply = try await send(settings, messages: trimmed)
        _ = try append("assistant", text: reply, store: store, app: app)
        return reply
    }

    private static func append(_ role: String, text: String, store: MacStore, app: AppDelegate) throws -> [[String: String]] {
        var state = store.readWorkspace()
        var messages = state["messages"] as? [[String: String]] ?? []
        messages.append(["role":role, "content":text])
        if messages.count > 240 { messages.removeFirst(messages.count - 240) }
        state["messages"] = messages
        try store.writeWorkspace(state)
        app.publish(["history":messages])
        return messages
    }

    private static func httpURL(_ raw: String) throws -> URL {
        guard let url = URL(string: raw.trimmingCharacters(in: .whitespacesAndNewlines)),
              let scheme = url.scheme?.lowercased(), ["http", "https"].contains(scheme),
              url.host != nil, url.user == nil, url.password == nil else {
            throw MacError("Укажите полный HTTP(S)-адрес без учётных данных.")
        }
        return url
    }

    private typealias Model = (base: URL, model: String, key: String?)

    private static func model(_ draft: [String: Any], store: MacStore) throws -> Model {
        let saved = store.readSettings("model.json")
        let url = (draft["url"] ?? saved["url"]) as? String ?? ""
        let model = ((draft["model"] ?? saved["model"]) as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        guard !url.isEmpty, !model.isEmpty, model.count <= 500 else {
            throw MacError("Укажите базовый URL и ID модели в настройках.")
        }
        let base = try httpURL(url)
        guard base.query == nil, base.fragment == nil else { throw MacError("Базовый URL должен быть без параметров и фрагмента.") }
        let freshKey = (draft["key"] as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        let savedKey = base.absoluteString == (saved["url"] as? String) ? readKey() : nil
        let key = draft["clearKey"] as? Bool == true ? nil : freshKey.isEmpty ? savedKey : freshKey
        return (base, model, key)
    }

    private static func readKey() -> String? {
        var query = keychainQuery
        query[kSecReturnData as String] = true
        var value: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &value) == errSecSuccess, let data = value as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    private static func writeKey(_ key: String?) throws {
        let query = keychainQuery as CFDictionary
        if let key = key {
            let data = Data(key.utf8)
            let status = SecItemUpdate(query, [kSecValueData as String:data] as CFDictionary)
            if status == errSecItemNotFound {
                var item = keychainQuery
                item[kSecValueData as String] = data
                guard SecItemAdd(item as CFDictionary, nil) == errSecSuccess else { throw MacError("Не удалось сохранить ключ в Связке ключей.") }
            } else if status != errSecSuccess { throw MacError("Не удалось обновить ключ в Связке ключей.") }
        } else {
            let status = SecItemDelete(query)
            guard status == errSecSuccess || status == errSecItemNotFound else { throw MacError("Не удалось удалить ключ модели.") }
        }
    }

    private static func send(_ config: Model, messages: [[String: String]]) async throws -> String {
        var request = URLRequest(url: config.base.appendingPathComponent("chat/completions"))
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        if let key = config.key { request.setValue("Bearer " + key, forHTTPHeaderField: "Authorization") }
        request.httpBody = try JSONSerialization.data(withJSONObject: ["model":config.model, "messages":messages, "stream":false])
        let (data,response) = try await session.data(for: request)
        guard let status = (response as? HTTPURLResponse)?.statusCode, (200...299).contains(status) else {
            throw MacError("Модель вернула HTTP \((response as? HTTPURLResponse)?.statusCode ?? 0). Проверьте URL, модель и ключ.")
        }
        let result = try JSONSerialization.jsonObject(with: data) as? [String:Any]
        let choices = result?["choices"] as? [[String:Any]]
        let message = choices?.first?["message"] as? [String:Any]
        guard let reply = message?["content"] as? String, !reply.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw MacError("Модель вернула пустой ответ.")
        }
        return String(reply.prefix(20_000))
    }

    private static func siteIcon(_ address: String, store: MacStore) async throws -> String? {
        let url = try httpURL(address)
        guard var host = URLComponents(url: url, resolvingAgainstBaseURL: false) else { return nil }
        host.path = "/favicon.ico"; host.query = nil; host.fragment = nil
        guard let fallback = host.url else { return nil }
        let file = SHA256.hash(data: Data(fallback.absoluteString.utf8)).map { String(format:"%02x",$0) }.joined() + ".png"
        let destination = store.icons.appendingPathComponent(file)
        if FileManager.default.fileExists(atPath: destination.path) { return file }
        var candidates = [fallback]
        if let (data, response) = try? await session.data(from: url), data.count < 2_000_000,
           let html = String(data:data,encoding:.utf8),
           let pattern = try? NSRegularExpression(pattern:"<link\\b[^>]*>",options:.caseInsensitive) {
            for match in pattern.matches(in:html,range:NSRange(html.startIndex...,in:html)).prefix(128) {
                guard let range = Range(match.range,in:html) else { continue }
                let tag = String(html[range])
                let rel = attribute("rel", in:tag)?.lowercased() ?? ""
                guard rel.split(separator:" ").contains(where: { ["icon","apple-touch-icon"].contains(String($0)) }),
                      let href = attribute("href",in:tag),
                      let target = URL(string:href,relativeTo:response.url ?? url)?.absoluteURL,
                      let checked = try? httpURL(target.absoluteString) else { continue }
                candidates.insert(checked, at:0)
                if candidates.count == 5 { break }
            }
        }
        for candidate in candidates {
            guard let (data,response) = try? await session.data(from:candidate), data.count < 2_000_000,
                  let status = (response as? HTTPURLResponse)?.statusCode, (200...299).contains(status),
                  let image = CGImageSourceCreateWithData(data as CFData,nil),
                  let icon = CGImageSourceCreateThumbnailAtIndex(image,0,[
                    kCGImageSourceCreateThumbnailFromImageAlways:true, kCGImageSourceThumbnailMaxPixelSize:128
                  ] as CFDictionary),
                  let png = NSBitmapImageRep(cgImage:icon).representation(using:.png,properties:[:]) else { continue }
            try png.write(to:destination,options:.atomic)
            return file
        }
        return nil
    }

    private static func attribute(_ name: String, in tag: String) -> String? {
        guard let expression = try? NSRegularExpression(pattern:"\\b" + name + "\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", options:.caseInsensitive),
              let hit = expression.firstMatch(in:tag,range:NSRange(tag.startIndex...,in:tag)) else { return nil }
        for index in 1..<hit.numberOfRanges {
            if let range = Range(hit.range(at:index),in:tag) { return String(tag[range]) }
        }
        return nil
    }

    private static func iconMask(_ file: String, store: MacStore) throws -> String? {
        guard file.range(of:"^[a-f0-9]{64}\\.png$",options:.regularExpression) != nil else { return nil }
        let output = String(file.dropLast(4)) + "-mono.png"
        let destination = store.icons.appendingPathComponent(output)
        if FileManager.default.fileExists(atPath: destination.path) { return output }
        guard let data = try? Data(contentsOf:store.icons.appendingPathComponent(file)),
              let source = CGImageSourceCreateWithData(data as CFData,nil),
              let image = CGImageSourceCreateImageAtIndex(source,0,nil) else { return nil }
        let width = image.width, height = image.height
        guard let rgb = CGContext(data:nil,width:width,height:height,bitsPerComponent:8,bytesPerRow:width*4,
                                  space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        rgb.draw(image,in:CGRect(x:0,y:0,width:width,height:height))
        guard let pixels = rgb.data?.assumingMemoryBound(to:UInt8.self) else { return nil }
        let corners = [0,width-1,(height-1)*width,height*width-1]
        let alphaOnly = (corners.map { pixels[$0*4+3] }.min() ?? 0) < 128
        let base = (0..<3).map { channel in corners.reduce(0) { $0 + Int(pixels[$1*4+channel]) } / 4 }
        for y in 0..<height {
            for x in 0..<width {
                let offset = 4*(y*width+x)
                let difference = (0..<3).map { abs(Int(pixels[offset+$0])-base[$0]) }.max() ?? 0
                let alpha = alphaOnly ? pixels[offset+3] : UInt8(clamping:(difference-24)*3)
                for channel in 0..<4 { pixels[offset+channel] = alpha }
            }
        }
        guard let mask = rgb.makeImage(), let png = NSBitmapImageRep(cgImage:mask).representation(using:.png,properties:[:]) else { return nil }
        try png.write(to:destination,options:.atomic)
        return output
    }
}
