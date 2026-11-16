import AppKit
import Foundation
import ImageIO
import UniformTypeIdentifiers

struct MacError: LocalizedError {
    let errorDescription: String?
    init(_ message: String) { errorDescription = message }
}

@MainActor
final class MacStore {
    let root: URL
    let dataDirectory: URL
    let pets: URL
    let pending: URL
    let icons: URL
    private let workspace: URL
    private let fm = FileManager.default

    init() throws {
        guard let resources = Bundle.main.resourceURL,
              FileManager.default.fileExists(atPath: resources.appendingPathComponent("wwwroot/index.html").path) else {
            throw MacError("В пакете отсутствуют файлы интерфейса «Спутника».")
        }
        root = resources.appendingPathComponent("wwwroot", isDirectory: true)
        let support = try fm.url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true)
        let directory = support.appendingPathComponent("Sputnik", isDirectory: true)
        dataDirectory = directory
        pets = directory.appendingPathComponent("pets", isDirectory: true)
        pending = directory.appendingPathComponent("preview", isDirectory: true)
        icons = directory.appendingPathComponent("icons", isDirectory: true)
        workspace = directory.appendingPathComponent("workspace.json")
        for folder in [dataDirectory, pets, pending, icons] {
            try fm.createDirectory(at: folder, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        }
        let yesterday = Date().addingTimeInterval(-86_400)
        for entry in (try? fm.contentsOfDirectory(at: pending, includingPropertiesForKeys: [.contentModificationDateKey])) ?? [] {
            if let date = try? entry.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate,
               date < yesterday { try? fm.removeItem(at: entry) }
        }
    }

    func readWorkspace() -> [String: Any] { read(workspace) ?? [:] }

    func writeWorkspace(_ value: [String: Any]) throws { try write(value, to: workspace) }

    func readSettings(_ name: String) -> [String: Any] { read(dataDirectory.appendingPathComponent(name)) ?? [:] }

    func writeSettings(_ value: [String: Any], name: String) throws {
        try write(value, to: dataDirectory.appendingPathComponent(name))
    }

    func installedPetManifests() -> [[String: Any]] {
        let items = (try? fm.contentsOfDirectory(at: pets, includingPropertiesForKeys: [.isDirectoryKey], options: .skipsHiddenFiles)) ?? []
        return items.compactMap { folder in
            guard let info = read(folder.appendingPathComponent("pet.json")),
                  info["id"] as? String == folder.lastPathComponent else { return nil }
            return info
        }.sorted { ($0["name"] as? String ?? "") < ($1["name"] as? String ?? "") }
    }

    func webPet(_ pet: [String: Any], path: String) -> [String: Any] {
        var copy = pet
        copy["animations"] = (pet["animations"] as? [[String: Any]] ?? []).map { clip in
            var clip = clip
            if let name = clip["file"] as? String { clip["file"] = path + "/" + name }
            return clip
        }
        return copy
    }

    func choosePet() throws -> String? {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [UTType(filenameExtension: "pet") ?? .data]
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        panel.message = "Выберите файл питомца «Спутника»."
        return panel.runModal() == .OK ? panel.url?.path : nil
    }

    func stagePet(_ path: String) async throws -> [String: Any] {
        let token = UUID().uuidString.lowercased()
        let stage = pending.appendingPathComponent(token, isDirectory: true)
        let bundled = root.appendingPathComponent("assets/pets.json")
        let pets = self.pets
        let file = URL(fileURLWithPath: path)
        let manifest = try await Task.detached(priority: .userInitiated) {
            try PetArchive.stage(file, at: stage, library: pets, builtins: bundled)
        }.value
        var preview = webPet(manifest, path: "preview/\(token)")
        preview["token"] = token
        return preview
    }

    func installPet(_ token: String) throws -> [String: Any] {
        guard UUID(uuidString: token)?.uuidString.lowercased() == token else {
            throw MacError("Откройте файл питомца снова.")
        }
        let stage = pending.appendingPathComponent(token, isDirectory: true)
        guard let info = read(stage.appendingPathComponent("pet.json")), let id = info["id"] as? String,
              id.range(of: "^[a-z0-9][a-z0-9-]{0,48}$", options: .regularExpression) != nil else {
            throw MacError("Пакет не найден. Откройте файл питомца снова.")
        }
        let target = pets.appendingPathComponent(id, isDirectory: true)
        guard !fm.fileExists(atPath: target.path) else { throw MacError("Этот питомец уже в библиотеке.") }
        try fm.moveItem(at: stage, to: target)
        return webPet(info, path: "pets/\(id)")
    }

    private func read(_ url: URL) -> [String: Any]? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        return (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
    }

    private func write(_ value: [String: Any], to path: URL) throws {
        let bytes = try JSONSerialization.data(withJSONObject: value, options: [.sortedKeys])
        guard bytes.count <= 8_000_000 else { throw MacError("Локальные данные слишком велики.") }
        try bytes.write(to: path, options: .atomic)
    }
}

enum PetArchive {
    private static let maximum = 96 * 1024 * 1024

    static func stage(_ source: URL, at directory: URL, library: URL, builtins: URL) throws -> [String: Any] {
        let fm = FileManager.default
        guard source.pathExtension.lowercased() == "pet",
              let length = try source.resourceValues(forKeys: [.fileSizeKey, .isRegularFileKey]).fileSize,
              length <= maximum + 1024 * 1024 else { throw MacError("Выберите пакет питомца размером до 96 МБ.") }
        try fm.createDirectory(at: directory, withIntermediateDirectories: false, attributes: nil)
        do {
            let archive = directory.appendingPathComponent("source.zip")
            try fm.copyItem(at: source, to: archive)
            let bytes = try Data(contentsOf: archive, options: .mappedIfSafe)
            let lengths = try entries(bytes)
            guard let length = lengths["manifest.json"], length <= 65536 else {
                throw MacError("В пакете нет допустимого manifest.json.")
            }
            let manifestData = try extract("manifest.json", size: length, from: archive)
            guard let pet = try JSONSerialization.jsonObject(with: manifestData) as? [String: Any],
                  (pet["schemaVersion"] as? Int) == 1,
                  let id = pet["id"] as? String, matches(id, "^[a-z0-9][a-z0-9-]{0,48}$"),
                  let rawTheme = pet["theme"] as? [String: Any],
                  let clips = pet["animations"] as? [[String: Any]], (1...24).contains(clips.count) else {
                throw MacError("Некорректный пакет питомца версии 1.")
            }
            let builtins = (try? JSONSerialization.jsonObject(with: Data(contentsOf: builtins))) as? [[String: Any]] ?? []
            guard !builtins.contains(where: { $0["id"] as? String == id }),
                  !fm.fileExists(atPath: library.appendingPathComponent(id).path) else {
                throw MacError("Этот питомец уже в библиотеке.")
            }
            let name = try field(pet, "name", limit: 60)
            let description = pet["description"] as? String ?? ""
            guard description.count <= 300 else { throw MacError("Описание питомца слишком длинное.") }
            var theme = ["name": try field(rawTheme, "name", limit: 35)]
            for key in ["accent", "background", "surface"] {
                let color = try field(rawTheme, key, limit: 7)
                guard matches(color, "^#[A-Fa-f0-9]{6}$") else { throw MacError("Укажите цвет в формате #RRGGBB.") }
                theme[key] = color
            }
            var animations = [[String: Any]]()
            var names = Set<String>()
            var extracted = 0
            for (index, clip) in clips.enumerated() {
                let title = try field(clip, "name", limit: 60)
                let file = try field(clip, "file", limit: 160)
                guard names.insert(title.lowercased()).inserted,
                      matches(file, "^(?:[a-zA-Z0-9_-]+/)*[a-zA-Z0-9_-]+\\.[pP][nN][gG]$"),
                      let size = lengths[file], size > 0 else {
                    throw MacError("Укажите уникальные имена анимаций и корректные PNG-листы.")
                }
                extracted += size
                guard extracted <= maximum else { throw MacError("Суммарный размер листов превышает 96 МБ.") }
                let w = try integer(clip, "frameWidth", 1...2048)
                let h = try integer(clip, "frameHeight", 1...2048)
                let count = try integer(clip, "frameCount", 1...480)
                let columns = try integer(clip, "columns", 1...64)
                let fps = try integer(clip, "fps", 1...60)
                let width = w * columns
                let height = h * ((count + columns - 1) / columns)
                guard width <= 8192, height <= 8192, width * height <= 32_000_000 else {
                    throw MacError("Размер PNG-листа превышает допустимый.")
                }
                let data = try extract(file, size: size, from: archive)
                guard let image = CGImageSourceCreateWithData(data as CFData, nil),
                      CGImageSourceGetType(image).map({ ($0 as String) == UTType.png.identifier }) == true,
                      let props = CGImageSourceCopyPropertiesAtIndex(image, 0, nil) as? [String: Any],
                      props[kCGImagePropertyPixelWidth as String] as? Int == width,
                      props[kCGImagePropertyPixelHeight as String] as? Int == height else {
                    throw MacError("Размер PNG не соответствует сетке кадров.")
                }
                let asset = "clip-\(index).png"
                try data.write(to: directory.appendingPathComponent(asset), options: .atomic)
                animations.append(["name": title, "file": asset, "frameWidth": w, "frameHeight": h,
                                   "frameCount": count, "columns": columns, "fps": fps])
            }
            try fm.removeItem(at: archive)
            let installed: [String: Any] = ["id": id, "name": name, "description": description,
                                            "theme": theme, "animations": animations]
            try JSONSerialization.data(withJSONObject: installed).write(to: directory.appendingPathComponent("pet.json"), options: .atomic)
            return installed
        } catch {
            try? fm.removeItem(at: directory)
            throw error
        }
    }

    private static func entries(_ data: Data) throws -> [String: Int] {
        func u16(_ p: Int) -> Int { Int(data[p]) | Int(data[p+1]) << 8 }
        func u32(_ p: Int) -> Int { u16(p) | u16(p+2) << 16 }
        let error = MacError("В архиве питомца некорректный ZIP-каталог.")
        guard data.count >= 22, data.count <= maximum + 1024 * 1024 else { throw error }
        let lower = max(0, data.count - 65557)
        guard let end = stride(from: data.count - 22, through: lower, by: -1).first(where: {
            u32($0) == 0x06054b50 && $0 + 22 + u16($0 + 20) == data.count
        }), u16(end+4) == 0, u16(end+6) == 0 else { throw error }
        let count = u16(end+10)
        let size = u32(end+12)
        var offset = u32(end+16)
        guard (1...40).contains(count), u16(end+8) == count, offset + size == end else { throw error }
        var listed = [String: Int]()
        var total = 0
        for _ in 0..<count {
            guard offset + 46 <= end, u32(offset) == 0x02014b50, u16(offset+8) & 1 == 0,
                  [0,8].contains(u16(offset+10)) else { throw error }
            let length = u32(offset+24), nameLength = u16(offset+28)
            let next = offset + 46 + nameLength + u16(offset+30) + u16(offset+32)
            guard next <= end, nameLength > 0, nameLength <= 160,
                  let name = String(data: data[(offset+46)..<(offset+46+nameLength)], encoding: .utf8),
                  matches(name, "^[a-zA-Z0-9_-]+(?:/[a-zA-Z0-9_-]+)*(?:\\.[a-zA-Z0-9]+)?/?$"),
                  listed[name] == nil else { throw error }
            total += length
            guard total <= maximum else { throw MacError("Пакет слишком велик.") }
            listed[name] = length
            offset = next
        }
        guard offset == end else { throw error }
        return listed
    }

    private static func extract(_ name: String, size: Int, from archive: URL) throws -> Data {
        let pipe = Pipe()
        let command = Process()
        command.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
        command.arguments = ["-p", archive.path, name]
        command.standardInput = FileHandle.nullDevice
        command.standardOutput = pipe
        command.standardError = FileHandle.nullDevice
        try command.run()
        var data = Data()
        do {
            while let chunk = try pipe.fileHandleForReading.read(upToCount: 64 * 1024), !chunk.isEmpty {
                guard data.count + chunk.count <= size else { throw MacError("Размер элемента ZIP не совпадает с каталогом.") }
                data.append(chunk)
            }
        } catch {
            command.terminate()
            command.waitUntilExit()
            throw error
        }
        command.waitUntilExit()
        guard command.terminationStatus == 0, data.count == size else {
            throw MacError("Не удалось прочитать элемент архива.")
        }
        return data
    }

    private static func matches(_ value: String, _ expression: String) -> Bool {
        value.range(of: expression, options: .regularExpression)?.count == value.count
    }

    private static func field(_ object: [String: Any], _ key: String, limit: Int) throws -> String {
        let value = (object[key] as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        guard (1...limit).contains(value.count) else { throw MacError("Проверьте поле \(key).") }
        return value
    }

    private static func integer(_ object: [String: Any], _ key: String, _ limit: ClosedRange<Int>) throws -> Int {
        guard let value = object[key] as? Int, limit.contains(value) else { throw MacError("Проверьте поле \(key).") }
        return value
    }
}
