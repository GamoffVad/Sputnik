import AppKit

@MainActor
enum NoteAttachments {
    struct Payload {
        let text: String
        let images: [NSImage]
        var items: [Any] { (text.isEmpty ? [] : [text as Any]) + images.map { $0 as Any } }
    }

    static func read(_ contents: NSAttributedString) throws -> Payload {
        var images: [NSImage] = []
        var unreadable = false
        contents.enumerateAttribute(.attachment, in: NSRange(location: 0, length: contents.length)) { value, _, _ in
            guard let attachment = value as? NSTextAttachment else { return }
            // RTFD stores imported photos in fileWrapper; attachment.image can
            // be nil even while its attachment cell displays the image.
            let image: NSImage?
            if let wrapper = attachment.fileWrapper, wrapper.isRegularFile,
               let data = wrapper.regularFileContents {
                image = NSImage(data: data)
            } else if let data = attachment.contents {
                image = NSImage(data: data)
            } else {
                image = attachment.image ?? (attachment.attachmentCell as? NSCell)?.image
            }
            if let image, image.isValid { images.append(image) }
            else { unreadable = true }
        }
        if unreadable { throw MacError("Не удалось прочитать фото из заметки. Вставьте его заново перед отправкой.") }
        return Payload(text: contents.string.replacingOccurrences(of: "\u{FFFC}", with: "")
            .trimmingCharacters(in: .whitespacesAndNewlines), images: images)
    }

    static func export(_ payload: Payload, root: URL) throws -> [URL] {
        let folder = root.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true,
                                               attributes: [.posixPermissions: 0o700])
        var files: [URL] = []
        do {
            for (index, image) in payload.images.enumerated() {
                guard let tiff = image.tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff),
                      let png = bitmap.representation(using: .png, properties: [:]) else {
                    throw MacError("Не удалось подготовить фото для отправки.")
                }
                let file = folder.appendingPathComponent(String(format: "Фото-%02d.png", index + 1))
                try png.write(to: file, options: .atomic)
                try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: file.path)
                files.append(file)
            }
        } catch {
            try? FileManager.default.removeItem(at: folder) // Only this new export.
            throw error
        }
        return files
    }

    static func copyPhotos(_ files: [URL], to board: NSPasteboard) throws {
        let items = try files.map { file -> NSPasteboardItem in
            let item = NSPasteboardItem()
            let data = try Data(contentsOf: file)
            item.setString(file.absoluteString, forType: .fileURL)
            item.setData(data, forType: .png)
            if let tiff = NSImage(data: data)?.tiffRepresentation { item.setData(tiff, forType: .tiff) }
            return item
        }
        guard !items.isEmpty else { throw MacError("В заметке нет фотографий.") }
        board.clearContents()
        guard board.writeObjects(items) else { throw MacError("Не удалось скопировать фото. Повторите попытку.") }
    }

    static func verify(in directory: URL) throws {
        let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 64, pixelsHigh: 48,
            bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
            colorSpaceName: .deviceRGB, bytesPerRow: 256, bitsPerPixel: 32)!
        bitmap.setColor(.red, atX: 10, y: 10)
        let png = bitmap.representation(using: .png, properties: [:])!
        let text = NSMutableAttributedString(string: "Фото & подпись 📝\n")
        for _ in 0..<2 {
            let file = FileWrapper(regularFileWithContents: png)
            file.preferredFilename = "photo.png"
            text.append(NSAttributedString(attachment: NSTextAttachment(fileWrapper: file)))
        }
        let rich = try text.data(from: NSRange(location: 0, length: text.length),
                                documentAttributes: [.documentType: NSAttributedString.DocumentType.rtfd])
        let reopened = try NSAttributedString(data: rich, options: [.documentType: NSAttributedString.DocumentType.rtfd],
                                             documentAttributes: nil)
        let payload = try read(reopened)
        guard payload.images.count == 2, payload.text == "Фото & подпись 📝", payload.items.count == 3 else {
            throw MacError("Sharing dropped saved note photos or their caption")
        }
        let onlyPhoto = try read(NSAttributedString(attachment: NSTextAttachment(fileWrapper: FileWrapper(regularFileWithContents: png))))
        guard onlyPhoto.text.isEmpty, onlyPhoto.items.count == 1 else { throw MacError("Photo-only note cannot be shared") }
        let files = try export(payload, root: directory.appendingPathComponent("share-test"))
        let board = NSPasteboard.withUniqueName()
        defer { board.releaseGlobally() }
        try copyPhotos(files, to: board)
        guard board.pasteboardItems?.count == 2, let copied = board.pasteboardItems?.first?.data(forType: .png),
              let decoded = NSBitmapImageRep(data: copied), decoded.pixelsWide == 64, decoded.pixelsHigh == 48,
              board.pasteboardItems?.allSatisfy({ $0.string(forType: .fileURL) != nil }) == true else {
            throw MacError("Photo clipboard/export lost an attachment or resized it")
        }
        print("PASS: saved RTFD photos + Unicode caption, photo-only note, two PNG exports and isolated photo clipboard; no external sending")
    }
}
