import AppKit
import ImageIO
import UniformTypeIdentifiers

private struct StickyRecord: Codable, Sendable {
    var id = UUID()
    var content: Data?
    var frame: CGRect?
    var tint = 0
    var pinned = false
}

struct StickyShareChannel: Decodable, Equatable {
    let name: String
    let template: String

    private static let telegram = "https://t.me/share/url?url=%E2%80%8B&text={text}"
    private static let max = "https://web.max.ru/:share?text={text}"
    private static let legacyDefaults = [
        StickyShareChannel(name: "Почта", template: "mailto:?body={text}"),
        StickyShareChannel(name: "Telegram", template: "tg://msg?text={text}"),
    ]
    private static let defaults = [
        legacyDefaults[0],
        StickyShareChannel(name: "Telegram", template: telegram),
        StickyShareChannel(name: "MAX", template: max),
    ]
    private static let schemes: Set<String> = ["https", "mailto", "tg", "max", "whatsapp", "viber", "sms"]
    private static let unreserved = CharacterSet(charactersIn:
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")

    static func load(from url: URL) -> [StickyShareChannel] {
        struct Settings: Decodable {
            let schemaVersion: Int?
            let channels: [StickyShareChannel]
        }
        guard let data = try? Data(contentsOf: url),
              let settings = try? JSONDecoder().decode(Settings.self, from: data) else { return defaults }
        if settings.schemaVersion == nil, settings.channels == legacyDefaults { return defaults }
        return settings.channels.prefix(32).map { $0.currentTemplate }
    }

    private var currentTemplate: Self {
        switch template {
        case "tg://msg?text={text}", "https://t.me/share/url?url=&text={text}":
            return Self(name: name, template: Self.telegram)
        case "https://max.ru/:share?text={text}":
            return Self(name: name, template: Self.max)
        default: return self
        }
    }

    var menuLabel: String { template == Self.max ? name + " (в браузере)" : name }

    func targets(text: String) -> (primary: URL, fallback: URL?)? {
        guard let target = currentTemplate.url(text: text) else { return nil }
        if target.host == "t.me", target.path == "/share/url",
           let web = URLComponents(url: target, resolvingAgainstBaseURL: false),
           let query = web.percentEncodedQuery,
           let native = URL(string: "tg://msg_url?" + query) {
            return (native, target)
        }
        return (target, nil)
    }

    static func verifyTargets() throws {
        let text = "Проверка 📝 & # + ?\nВторая строка"
        let legacy = StickyShareChannel(name: "Telegram", template: "tg://msg?text={text}")
        guard let telegram = legacy.targets(text: text), let fallback = telegram.fallback,
              telegram.primary.scheme == "tg", telegram.primary.host == "msg_url", fallback.host == "t.me",
              let query = URLComponents(url: telegram.primary, resolvingAgainstBaseURL: false)?.queryItems,
              query.first(where: { $0.name == "url" })?.value == "\u{200B}",
              query.first(where: { $0.name == "text" })?.value == text,
              let max = defaults.last?.targets(text: text), max.primary.host == "web.max.ru",
              max.primary.path == "/:share",
              URLComponents(url: max.primary, resolvingAgainstBaseURL: false)?.queryItems?.first?.value == text else {
            throw MacError("Ссылки отправки теряют текст заметки или не открывают выбор чата.")
        }
    }

    func url(text: String) -> URL? {
        guard !name.isEmpty, name.count <= 40, template.count <= 1800,
              template.rangeOfCharacter(from: .controlCharacters) == nil,
              let queryStart = template.firstIndex(of: "?"),
              let query = template[template.index(after: queryStart)...].split(separator: "#", maxSplits: 1).first,
              query.split(separator: "&").contains(where: { item in
                  let parts = item.split(separator: "=", maxSplits: 1)
                  return parts.count == 2 && parts[1].contains("{text}")
              }),
              let encodedText = text.addingPercentEncoding(withAllowedCharacters: Self.unreserved),
              let title = "Заметка из «Спутника»".addingPercentEncoding(withAllowedCharacters: Self.unreserved)
        else { return nil }
        let base = template[..<queryStart]
        guard !base.contains("#"), !base.contains("{text}"), !base.contains("{title}") else { return nil }
        let expanded = template.replacingOccurrences(of: "{title}", with: title)
            .replacingOccurrences(of: "{text}", with: encodedText)
        guard let target = URLComponents(string: expanded),
              let scheme = target.scheme?.lowercased(), Self.schemes.contains(scheme),
              target.user == nil, target.password == nil,
              scheme != "https" || target.host?.isEmpty == false else { return nil }
        return target.url
    }
}

@MainActor
final class MacStickyNotes {
    private let location: URL
    private let sharingSettings: URL
    private let workspaceSettings: URL
    private var records: [StickyRecord]
    private var cards: [UUID: StickyCard] = [:]
    private var pendingSave: DispatchWorkItem?
    private let saveQueue = DispatchQueue(label: "com.gamoffvad.sputnik.sticky-notes")

    var isVisible: Bool { cards.values.contains { $0.window.isVisible } }

    init(dataDirectory: URL) {
        location = dataDirectory.appendingPathComponent("sticky-notes.json")
        sharingSettings = dataDirectory.appendingPathComponent("share.json")
        workspaceSettings = dataDirectory.appendingPathComponent("workspace.json")
        if let stored = try? Data(contentsOf: location) {
            records = (try? JSONDecoder().decode([StickyRecord].self, from: stored)) ?? []
        } else {
            records = []
        }
    }

    func toggle(palette: SputnikPalette) {
        if isVisible {
            cards.values.forEach { $0.window.orderOut(nil) }
            flush()
        } else {
            show(palette: palette)
        }
    }

    func show(palette: SputnikPalette) {
        if records.isEmpty { records.append(newRecord()) }
        for (index, record) in records.enumerated() {
            let note = open(record, palette: palette, offset: index)
            note.updatePalette(palette)
            note.window.orderFront(nil)
        }
        if let id = records.last?.id, let card = cards[id] {
            NSApp.activate(ignoringOtherApps: true)
            card.window.makeKeyAndOrderFront(nil)
        }
        saveSoon()
    }

    func updatePalette(_ palette: SputnikPalette) {
        cards.values.forEach { $0.updatePalette(palette) }
    }

    private func newRecord() -> StickyRecord {
        let settings = (try? Data(contentsOf: workspaceSettings)).flatMap {
            try? JSONSerialization.jsonObject(with: $0) as? [String: Any]
        } ?? [:]
        var record = StickyRecord()
        if settings["noteColorMode"] as? String == "auto" {
            let previous = records.last.map { cards[$0.id]?.snapshot().tint ?? $0.tint } ?? -1
            record.tint = (previous + 1) % 5
        } else {
            record.tint = -1 // Theme; older explicit note colors stay unchanged.
        }
        return record
    }

    func flush(completion: (@MainActor @Sendable () -> Void)? = nil) {
        pendingSave?.cancel()
        pendingSave = nil
        records = records.map { cards[$0.id]?.snapshot() ?? $0 }
        let snapshot = records
        let target = location
        saveQueue.async {
            do {
                try JSONEncoder().encode(snapshot).write(to: target, options: .atomic)
            } catch {
                NSLog("Sputnik sticky notes: %@", error.localizedDescription)
            }
            if let completion { DispatchQueue.main.async { completion() } }
        }
    }

    private func saveSoon() {
        pendingSave?.cancel()
        let save = DispatchWorkItem { [weak self] in
            MainActor.assumeIsolated { self?.flush() }
        }
        pendingSave = save
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3, execute: save)
    }

    static func verifyMultipleNotes(in directory: URL, palette: SputnikPalette) async throws {
        let workspace = directory.appendingPathComponent("workspace.json")
        let original = try? Data(contentsOf: workspace)
        defer { if let original { try? original.write(to: workspace) } }
        try JSONSerialization.data(withJSONObject: ["noteColorMode": "auto"]).write(to: workspace)
        let notes = MacStickyNotes(dataDirectory: directory)
        notes.show(palette: palette)
        guard let first = notes.cards.values.first else { throw MacError("Не открылась тестовая заметка.") }
        try first.verifyEditing()
        first.create?()
        first.create?()
        guard notes.cards.count == 3, notes.cards.values.allSatisfy({ $0.window.isVisible }) else {
            throw MacError("Не открылись три тестовые заметки.")
        }
        let others = notes.cards.values.filter { $0 !== first }
        let neighbour = others[0], hidden = others[1]
        hidden.window.orderOut(nil)
        let area = NSScreen.main?.visibleFrame ?? CGRect(x: 0, y: 0, width: 1200, height: 800)
        let anchor = CGRect(x: area.minX + 40, y: area.minY + 40, width: 340, height: 255)
        neighbour.window.setFrame(anchor, display: false)
        let proposed = CGRect(x: anchor.maxX + 17, y: anchor.minY + 6, width: 340, height: 255)
        let aligned = CGRect(x: anchor.maxX + 12, y: anchor.minY, width: 340, height: 255)
        first.move(to: proposed)
        guard first.window.frame == aligned, neighbour.window.frame == anchor else {
            throw MacError("Заметки не примагничиваются или двигают соседа.")
        }
        first.move(to: proposed, bypass: true)
        guard first.window.frame == proposed else { throw MacError("Option не отключает примагничивание.") }
        neighbour.window.orderOut(nil)
        first.move(to: proposed)
        guard first.window.frame == proposed else { throw MacError("Скрытая заметка влияет на выравнивание.") }
        neighbour.window.orderFront(nil)
        first.move(to: proposed)
        let vectors: [(CGRect, CGRect)] = [
            (CGRect(x: -257, y: 94, width: 340, height: 320), CGRect(x: -252, y: 100, width: 340, height: 320)),
            (CGRect(x: 106, y: 437, width: 340, height: 320), CGRect(x: 100, y: 432, width: 340, height: 320)),
            (CGRect(x: 94, y: -237, width: 340, height: 320), CGRect(x: 100, y: -232, width: 340, height: 320)),
            (CGRect(x: 167, y: 450, width: 200, height: 200), CGRect(x: 170, y: 450, width: 200, height: 200)),
            (CGRect(x: 470, y: 157, width: 200, height: 200), CGRect(x: 470, y: 160, width: 200, height: 200)),
            (CGRect(x: 105, y: 900, width: 340, height: 320), CGRect(x: 105, y: 900, width: 340, height: 320))
        ]
        for (input, expected) in vectors {
            guard NoteSnapping.snap(input, to: [CGRect(x: 100, y: 100, width: 340, height: 320)]) == expected else {
                throw MacError("Неверное выравнивание заметок по краям или центрам.")
            }
        }
        try first.verifyHeaderHitTargets()
        notes.toggle(palette: palette)
        guard !notes.isVisible else { throw MacError("Тестовые заметки не скрылись.") }
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            notes.flush { continuation.resume() }
        }
        let saved = try JSONDecoder().decode([StickyRecord].self, from: Data(contentsOf: notes.location))
        guard Set(saved.map(\.tint)).count == 3, saved.count == 3, saved.filter(\.pinned).count == 1, saved.contains(where: { $0.content != nil }) else {
            throw MacError("Не сохранились три тестовые заметки с текстом и закреплением.")
        }
        let reloaded = MacStickyNotes(dataDirectory: directory)
        reloaded.show(palette: palette)
        guard reloaded.cards[first.snapshot().id]?.window.frame == aligned else {
            throw MacError("Не восстановлено выровненное положение заметки.")
        }
        reloaded.toggle(palette: palette)
        FileHandle.standardOutput.write(Data("Sputnik smoke: note magnets align edges and centres, dock with 12pt gap, exclude hidden cards, honour Option, preserve header buttons and restore aligned positions.\n".utf8))
    }

    @discardableResult
    private func open(_ record: StickyRecord, palette: SputnikPalette, offset: Int) -> StickyCard {
        if let existing = cards[record.id] { return existing }
        let card = StickyCard(record, palette: palette, offset: offset)
        cards[record.id] = card
        card.neighbours = { [weak self] in
            self?.cards.filter { $0.key != record.id && $0.value.window.isVisible && !$0.value.window.isMiniaturized }
                .map { $0.value.window.frame } ?? []
        }
        card.didEdit = { [weak self] in self?.saveSoon() }
        card.shareChannels = { [sharingSettings] in StickyShareChannel.load(from: sharingSettings) }
        card.create = { [weak self, weak card] in
            guard let self, let card else { return }
            let second = newRecord()
            records.append(second)
            let newCard = open(second, palette: card.palette, offset: records.count - 1)
            newCard.window.makeKeyAndOrderFront(nil)
            saveSoon()
        }
        card.delete = { [weak self] in
            guard let self else { return }
            cards.removeValue(forKey: record.id)?.window.orderOut(nil)
            records.removeAll { $0.id == record.id }
            flush()
        }
        return card
    }
}

@MainActor
private final class StickyEditor: NSTextView {
    override func mouseDown(with event: NSEvent) {
        guard let layoutManager, let textContainer, let textStorage,
              layoutManager.numberOfGlyphs > 0 else {
            super.mouseDown(with: event)
            return
        }
        var point = convert(event.locationInWindow, from: nil)
        point.x -= textContainerOrigin.x
        point.y -= textContainerOrigin.y
        let glyph = layoutManager.glyphIndex(for: point, in: textContainer)
        guard glyph < layoutManager.numberOfGlyphs else {
            super.mouseDown(with: event)
            return
        }
        let hit = layoutManager.boundingRect(forGlyphRange: NSRange(location: glyph, length: 1), in: textContainer)
        let index = layoutManager.characterIndexForGlyph(at: glyph)
        if hit.contains(point), index < textStorage.length {
            let range = NSRange(location: index, length: 1)
            let current = (string as NSString).character(at: index)
            if current == 0x2610 || current == 0x2611 {
                let next = current == 0x2610 ? "☑" : "☐"
                if shouldChangeText(in: range, replacementString: next) {
                    textStorage.replaceCharacters(in: range, with: next)
                    didChangeText()
                }
                return
            }
        }
        super.mouseDown(with: event)
    }
}

@MainActor
private final class StickyHeader: NSView {
    var move: ((CGRect, Bool) -> Void)?
    private var dragPoint: CGPoint?
    private var dragOrigin = CGPoint.zero
    override var mouseDownCanMoveWindow: Bool { false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func hitTest(_ point: NSPoint) -> NSView? {
        guard let hit = super.hitTest(point) else { return nil }
        var ancestor: NSView? = hit
        while let view = ancestor, view !== self {
            if view is NSButton { return hit }
            ancestor = view.superview
        }
        return self
    }
    override func mouseDown(with event: NSEvent) {
        dragPoint = NSEvent.mouseLocation
        dragOrigin = window?.frame.origin ?? .zero
        window?.makeKeyAndOrderFront(nil)
    }
    override func mouseDragged(with event: NSEvent) {
        guard let window, let dragPoint else { return }
        let cursor = NSEvent.mouseLocation
        let proposed = CGRect(x: dragOrigin.x + cursor.x - dragPoint.x,
                              y: dragOrigin.y + cursor.y - dragPoint.y,
                              width: window.frame.width, height: window.frame.height)
        move?(proposed, event.modifierFlags.contains(.option))
    }
    override func mouseUp(with event: NSEvent) { dragPoint = nil }
}

@MainActor
private final class StickyCard: NSObject, NSWindowDelegate, NSTextViewDelegate {
    let window: SputnikPanel
    private(set) var palette: SputnikPalette
    var didEdit: (() -> Void)?
    var create: (() -> Void)?
    var delete: (() -> Void)?
    var shareChannels: (() -> [StickyShareChannel])?
    var neighbours: (() -> [CGRect])?

    private var record: StickyRecord
    private var contentDirty = false
    private let editor = StickyEditor()
    private let header = StickyHeader()
    private let heading = NSTextField(labelWithString: "Заметка")
    private let swatches = NSStackView()
    private let scroll = NSScrollView()
    private let formats = NSStackView()
    private var controls: [SputnikButton] = []
    private var pin: SputnikButton?
    private var share: SputnikButton?
    private var sharingPicker: NSSharingServicePicker?
    private lazy var card = paletteCard(palette)

    private static let tints: [(UInt32, String)] = [
        (0x58BDD9, "Бирюзовый"), (0xBA9AF2, "Сиреневый"), (0xE5A075, "Персиковый"),
        (0x88C9A3, "Мятный"), (0xE3C86F, "Янтарный")
    ]

    init(_ record: StickyRecord, palette: SputnikPalette, offset: Int) {
        self.record = record
        self.palette = palette
        window = SputnikPanel(size: CGSize(width: 340, height: 255), acceptsKeyboard: true,
                              level: record.pinned ? .floating : .normal)
        super.init()
        setup()
        if let frame = record.frame {
            place(frame)
        } else if let screen = NSScreen.main {
            let area = screen.visibleFrame
            let shift = CGFloat(offset % 8) * 22
            place(CGRect(x: area.maxX - 371 - shift, y: area.maxY - 285 - shift, width: 340, height: 255))
        } else {
            window.center()
        }
        window.delegate = self
    }

    private func place(_ wanted: CGRect) {
        guard let screen = NSScreen.screens.first(where: { $0.visibleFrame.intersects(wanted) }) ?? NSScreen.main else { return }
        let area = screen.visibleFrame
        let width = min(area.width, max(window.minSize.width, wanted.width))
        let height = min(area.height, max(window.minSize.height, wanted.height))
        let origin = CGPoint(x: min(max(wanted.minX, area.minX), area.maxX - width),
                             y: min(max(wanted.minY, area.minY), area.maxY - height))
        window.setFrame(CGRect(origin: origin, size: CGSize(width: width, height: height)), display: false)
    }

    private func setup() {
        window.title = "Заметка"
        window.isMovableByWindowBackground = false
        header.toolTip = "Перетащите для выравнивания с соседней заметкой. Option — без примагничивания"
        header.move = { [weak self] frame, bypass in self?.move(to: frame, bypass: bypass) }
        window.styleMask.insert(.resizable)
        window.minSize = CGSize(width: 312, height: 164)
        window.contentView = card

        let stack = NSStackView(views: [header, swatches, scroll, formats])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 0
        embed(stack, in: card)
        for view in [header, swatches, scroll, formats] {
            view.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
        }

        header.wantsLayer = true
        let add = button("plus", "Новая заметка") { [weak self] in self?.create?() }
        heading.font = .systemFont(ofSize: 12, weight: .semibold)
        heading.setContentHuggingPriority(.defaultLow, for: .horizontal)
        let color = button("paintpalette", "Цвет заметки") { [weak self] in
            guard let self else { return }
            swatches.isHidden.toggle()
        }
        let pinButton = button("pin", "Закрепить поверх окон") { [weak self] in self?.togglePin() }
        pin = pinButton
        let copy = button("doc.on.doc", "Скопировать заметку") { [weak self] in self?.copyNote() }
        let shareButton = button("square.and.arrow.up", "Поделиться заметкой") { [weak self] in self?.shareNote() }
        share = shareButton
        let remove = button("trash", "Удалить заметку") { [weak self] in self?.delete?() }
        let hide = button("xmark", "Скрыть заметку") { [weak self] in
            self?.window.orderOut(nil)
            self?.didEdit?()
        }
        let actions = NSStackView(views: [add, heading, color, pinButton, copy, shareButton, remove, hide])
        actions.spacing = 3
        actions.alignment = .centerY
        embed(actions, in: header, inset: NSEdgeInsets(top: 4, left: 7, bottom: 4, right: 7))

        swatches.spacing = 8
        swatches.edgeInsets = NSEdgeInsets(top: 5, left: 13, bottom: 5, right: 13)
        for (index, item) in Self.tints.enumerated() {
            let swatch = button("circle.fill", item.1) { [weak self] in
                guard let self else { return }
                self.record.tint = index
                swatches.isHidden = true
                updatePalette(palette)
                didEdit?()
            }
            swatch.tag = 100 + index
            swatches.addArrangedSubview(swatch)
        }
        swatches.isHidden = true

        scroll.borderType = .noBorder
        scroll.drawsBackground = false
        scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true
        scroll.scrollerStyle = .overlay
        scroll.documentView = editor
        scroll.setContentHuggingPriority(.defaultLow, for: .vertical)
        editor.frame = CGRect(x: 0, y: 0, width: 320, height: 160)
        editor.font = .systemFont(ofSize: 14)
        editor.isVerticallyResizable = true
        editor.isHorizontallyResizable = false
        editor.autoresizingMask = [.width]
        editor.textContainer?.widthTracksTextView = true
        editor.textContainerInset = CGSize(width: 12, height: 10)
        editor.drawsBackground = false
        editor.allowsUndo = true
        editor.isRichText = true
        editor.importsGraphics = true
        editor.delegate = self
        if let data = record.content,
           let stored = try? NSAttributedString(data: data, options: [.documentType: NSAttributedString.DocumentType.rtfd],
                                                documentAttributes: nil) {
            editor.textStorage?.setAttributedString(stored)
        }
        window.initialFirstResponder = editor

        formats.edgeInsets = NSEdgeInsets(top: 5, left: 9, bottom: 6, right: 9)
        formats.spacing = 7
        formats.alignment = .centerY
        formats.wantsLayer = true
        let items: [(String, String, () -> Void)] = [
            ("bold", "Полужирный", { [weak self] in self?.fontTrait(.boldFontMask) }),
            ("italic", "Курсив", { [weak self] in self?.fontTrait(.italicFontMask) }),
            ("underline", "Подчёркнутый", { [weak self] in self?.lineStyle(.underlineStyle) }),
            ("strikethrough", "Зачёркнутый", { [weak self] in self?.lineStyle(.strikethroughStyle) }),
            ("list.bullet", "Список", { [weak self] in self?.list("• ") }),
            ("checkmark.square", "Список с флажками", { [weak self] in self?.list("☐ ") }),
            ("photo", "Добавить изображение", { [weak self] in self?.image() }),
        ]
        for item in items {
            formats.addArrangedSubview(button(item.0, item.1) { [weak self] in
                guard let self else { return }
                window.makeFirstResponder(editor)
                item.2()
            })
        }
        formats.addArrangedSubview(NSView())
        updatePalette(palette)
    }

    private func button(_ symbol: String, _ label: String, action: @escaping () -> Void) -> SputnikButton {
        let control = SputnikButton(symbol: symbol, accessibility: label, tint: palette.ink, action: action)
        control.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([control.widthAnchor.constraint(equalToConstant: 25),
                                     control.heightAnchor.constraint(equalToConstant: 24)])
        controls.append(control)
        return control
    }

    private func embed(_ child: NSView, in parent: NSView, inset: NSEdgeInsets = NSEdgeInsetsZero) {
        child.translatesAutoresizingMaskIntoConstraints = false
        parent.addSubview(child)
        NSLayoutConstraint.activate([
            child.leadingAnchor.constraint(equalTo: parent.leadingAnchor, constant: inset.left),
            child.trailingAnchor.constraint(equalTo: parent.trailingAnchor, constant: -inset.right),
            child.topAnchor.constraint(equalTo: parent.topAnchor, constant: inset.top),
            child.bottomAnchor.constraint(equalTo: parent.bottomAnchor, constant: -inset.bottom),
        ])
    }

    private static func color(_ value: UInt32) -> NSColor {
        NSColor(srgbRed: CGFloat((value >> 16) & 255) / 255, green: CGFloat((value >> 8) & 255) / 255,
                blue: CGFloat(value & 255) / 255, alpha: 1)
    }

    func updatePalette(_ value: SputnikPalette) {
        palette = value
        let safeTint = record.tint >= 0 && record.tint < Self.tints.count ? record.tint : 0
        let tint = record.tint < 0 ? value.accent : Self.color(Self.tints[safeTint].0)
        card.layer?.backgroundColor = value.surface.cgColor
        card.layer?.borderColor = tint.withAlphaComponent(0.55).cgColor
        header.layer?.backgroundColor = value.surface.blended(withFraction: 0.16, of: tint)?.cgColor
        formats.layer?.backgroundColor = value.background.withAlphaComponent(0.6).cgColor
        heading.textColor = value.ink
        for control in controls {
            control.contentTintColor = control.tag >= 100 ? Self.color(Self.tints[control.tag - 100].0) : value.ink
            control.hoverColor = tint.withAlphaComponent(0.23)
            control.idleColor = .clear
        }
        updatePinSymbol()
        editor.insertionPointColor = value.ink
        if let contents = editor.textStorage, contents.length > 0 {
            contents.addAttribute(.foregroundColor, value: value.ink, range: NSRange(location: 0, length: contents.length))
        }
        var attributes = editor.typingAttributes
        attributes[.foregroundColor] = value.ink
        if attributes[.font] == nil { attributes[.font] = NSFont.systemFont(ofSize: 14) }
        editor.typingAttributes = attributes
    }

    private func togglePin() {
        record.pinned.toggle()
        window.level = record.pinned ? .floating : .normal
        window.makeKeyAndOrderFront(nil)
        updatePinSymbol()
        didEdit?()
    }

    private func updatePinSymbol() {
        let label = record.pinned ? "Снять закрепление" : "Закрепить поверх окон"
        pin?.image = NSImage(systemSymbolName: record.pinned ? "pin.fill" : "pin", accessibilityDescription: label)
        pin?.contentTintColor = record.pinned ? palette.surface : palette.ink
        pin?.idleColor = record.pinned ? palette.ink : .clear
        pin?.hoverColor = palette.ink.withAlphaComponent(record.pinned ? 0.8 : 0.16)
        pin?.layer?.backgroundColor = pin?.idleColor.cgColor
        pin?.toolTip = label
        pin?.setAccessibilityLabel(label)
    }

    private func copyNote() {
        guard let contents = editor.textStorage, contents.length > 0 else { return }
        let board = NSPasteboard.general
        board.clearContents()
        board.setString(editor.string, forType: .string)
        let full = NSRange(location: 0, length: contents.length)
        if let rich = contents.rtfd(from: full, documentAttributes: [.documentType: NSAttributedString.DocumentType.rtfd]) {
            board.setData(rich, forType: .rtfd)
        }
    }

    private func shareNote() {
        guard let share, !editor.string.isEmpty else { return }
        let payload: NoteAttachments.Payload
        do { payload = try NoteAttachments.read(editor.attributedString()) }
        catch { NSAlert(error: error).runModal(); return }
        let photos = !payload.images.isEmpty
        let options = (shareChannels?() ?? []).filter { $0.targets(text: payload.text) != nil }
        guard !options.isEmpty else {
            shareUsingSystem()
            return
        }
        let menu = NSMenu()
        if photos {
            let all = NSMenuItem(title: "Поделиться текстом и фото…", action: #selector(shareUsingSystem), keyEquivalent: "")
            all.target = self
            menu.addItem(all)
            menu.addItem(.separator())
        }
        for channel in options {
            let label = photos ? channel.menuLabel + " — вставить фото" : channel.menuLabel
            let entry = NSMenuItem(title: label, action: #selector(openShareChannel(_:)), keyEquivalent: "")
            entry.target = self
            entry.representedObject = channel
            menu.addItem(entry)
        }
        menu.addItem(.separator())
        if photos {
            let text = NSMenuItem(title: "Скопировать текст заметки", action: #selector(copyShareText), keyEquivalent: "")
            text.target = self
            text.isEnabled = !payload.text.isEmpty
            menu.addItem(text)
        }
        let system = NSMenuItem(title: "Другие способы…", action: #selector(shareUsingSystem), keyEquivalent: "")
        system.target = self
        menu.addItem(system)
        menu.popUp(positioning: nil, at: NSPoint(x: 0, y: share.bounds.minY), in: share)
    }

    private func shareText() -> String {
        editor.string.replacingOccurrences(of: "\u{FFFC}", with: "").trimmingCharacters(in: .whitespacesAndNewlines)
    }

    @objc private func openShareChannel(_ sender: NSMenuItem) {
        guard let channel = sender.representedObject as? StickyShareChannel else { return }
        do {
            let payload = try NoteAttachments.read(editor.attributedString())
            if !payload.images.isEmpty { try handoffPhotos(payload, channel: channel); return }
        } catch { NSAlert(error: error).runModal(); return }
        guard let target = channel.targets(text: shareText()) else { return }
        if NSWorkspace.shared.open(target.primary) { return }
        if let fallback = target.fallback, NSWorkspace.shared.open(fallback) { return }
        shareUsingSystem()
    }

    @objc private func copyShareText() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(shareText(), forType: .string)
    }

    private func handoffPhotos(_ payload: NoteAttachments.Payload, channel: StickyShareChannel) throws {
        guard let target = channel.targets(text: payload.text) else { throw MacError("Проверьте адрес канала отправки.") }
        let prompt = NSAlert()
        prompt.messageText = "Фото для «\(channel.name)»"
        prompt.informativeText = "Фото: \(payload.images.count). Скопируем их для вставки. Откройте нужный чат и нажмите ⌘V, затем подтвердите отправку. Текст заметки передадим через ссылку канала. Если клиент не принимает несколько фото из буфера, прикрепите подготовленные файлы через скрепку."
        prompt.addButton(withTitle: "Открыть чат")
        prompt.addButton(withTitle: "Показать файлы фото")
        prompt.addButton(withTitle: "Отмена")
        let response = prompt.runModal()
        guard response == .alertFirstButtonReturn || response == .alertSecondButtonReturn else { return }
        let root = try FileManager.default.url(for: .cachesDirectory, in: .userDomainMask,
                                               appropriateFor: nil, create: true)
            .appendingPathComponent("Sputnik/SharedNotes", isDirectory: true)
        let files = try NoteAttachments.export(payload, root: root)
        if response == .alertSecondButtonReturn {
            NSWorkspace.shared.activateFileViewerSelecting(files)
            return
        }
        try NoteAttachments.copyPhotos(files, to: .general)
        if NSWorkspace.shared.open(target.primary) { return }
        if let fallback = target.fallback, NSWorkspace.shared.open(fallback) { return }
        NSWorkspace.shared.activateFileViewerSelecting(files)
        throw MacError("Канал не открылся. Фото подготовлены: прикрепите их в нужном приложении.")
    }

    @objc private func shareUsingSystem() {
        guard let share else { return }
        let items: [Any]
        do { items = try NoteAttachments.read(editor.attributedString()).items }
        catch { NSAlert(error: error).runModal(); return }
        guard !items.isEmpty else { return }
        let picker = NSSharingServicePicker(items: items)
        sharingPicker = picker
        picker.show(relativeTo: share.bounds, of: share, preferredEdge: .minY)
    }

    private func fontTrait(_ trait: NSFontTraitMask) {
        let manager = NSFontManager.shared
        let selected = editor.selectedRange()
        let current = editor.typingAttributes[.font] as? NSFont ?? .systemFont(ofSize: 14)
        let remove = manager.traits(of: current).contains(trait)
        func update(_ font: NSFont) -> NSFont {
            remove ? manager.convert(font, toNotHaveTrait: trait) : manager.convert(font, toHaveTrait: trait)
        }
        if selected.length == 0 {
            editor.typingAttributes[.font] = update(current)
        } else if let storage = editor.textStorage, editor.shouldChangeText(in: selected, replacementString: nil) {
            var fonts: [(NSRange, NSFont)] = []
            storage.enumerateAttribute(.font, in: selected) { value, range, _ in
                fonts.append((range, update(value as? NSFont ?? current)))
            }
            storage.beginEditing()
            fonts.forEach { storage.addAttribute(.font, value: $0.1, range: $0.0) }
            storage.endEditing()
            editor.didChangeText()
        }
    }

    private func lineStyle(_ key: NSAttributedString.Key) {
        let value = (editor.typingAttributes[key] as? Int ?? 0) == 0 ? NSUnderlineStyle.single.rawValue : 0
        let selected = editor.selectedRange()
        if selected.length == 0 {
            editor.typingAttributes[key] = value
        } else if editor.shouldChangeText(in: selected, replacementString: nil) {
            editor.textStorage?.addAttribute(key, value: value, range: selected)
            editor.didChangeText()
        }
    }

    private func listMark(at index: Int, in source: NSString) -> String? {
        guard index + 2 <= source.length else { return nil }
        let prefix = source.substring(with: NSRange(location: index, length: 2))
        return ["• ", "☐ ", "☑ "].contains(prefix) ? prefix : nil
    }

    private func list(_ mark: String) {
        guard let contents = editor.textStorage else { return }
        let source = editor.string as NSString
        let previous = editor.selectedRange()
        let section = source.paragraphRange(for: previous)
        var offsets = [section.location]
        if section.length > 1 {
            for index in section.location..<(NSMaxRange(section) - 1) where source.character(at: index) == 10 {
                offsets.append(index + 1)
            }
        }
        let marks = offsets.map { listMark(at: $0, in: source) }
        let remove = marks.allSatisfy { prefix in
            mark == "• " ? prefix == mark : prefix == "☐ " || prefix == "☑ "
        }
        let replacement = remove ? "" : mark
        let attributes = editor.typingAttributes
        var delta = 0
        for (index, prefix) in zip(offsets, marks).reversed() {
            if prefix == replacement || (!remove && mark == "☐ " && prefix == "☑ ") { continue }
            let range = NSRange(location: index, length: prefix == nil ? 0 : 2)
            guard editor.shouldChangeText(in: range, replacementString: replacement) else { continue }
            contents.replaceCharacters(in: range, with: NSAttributedString(string: replacement, attributes: attributes))
            delta += replacement.utf16.count - range.length
        }
        if previous.length == 0 {
            let caret = max(section.location + replacement.utf16.count, previous.location + delta)
            editor.setSelectedRange(NSRange(location: min(contents.length, caret), length: 0))
        } else {
            editor.setSelectedRange(NSRange(location: section.location, length: section.length + delta))
        }
        editor.typingAttributes = attributes
        editor.didChangeText()
    }

    func textView(_ textView: NSTextView, doCommandBy commandSelector: Selector) -> Bool {
        guard commandSelector == #selector(NSResponder.insertNewline(_:)) else { return false }
        let source = textView.string as NSString
        let selection = textView.selectedRange()
        var start = 0
        var end = 0
        source.getParagraphStart(&start, end: nil, contentsEnd: &end, for: NSRange(location: selection.location, length: 0))
        guard let mark = listMark(at: start, in: source), NSMaxRange(selection) <= end else { return false }
        let item = NSRange(location: start + 2, length: end - start - 2)
        let attributes = textView.typingAttributes
        if source.substring(with: item).trimmingCharacters(in: .whitespaces).isEmpty {
            textView.insertText("", replacementRange: NSRange(location: start, length: end - start))
        } else {
            let split = max(selection.location, item.location)
            let range = NSRange(location: split, length: max(0, NSMaxRange(selection) - split))
            textView.insertText("\n" + (mark == "• " ? mark : "☐ "), replacementRange: range)
        }
        textView.typingAttributes = attributes
        return true
    }

    func verifyEditing() throws {
        let enter = #selector(NSResponder.insertNewline(_:))
        list("☐ ")
        editor.insertText("Первый", replacementRange: editor.selectedRange())
        guard textView(editor, doCommandBy: enter) else { throw MacError("Не продолжился список с флажками.") }
        editor.insertText("Второй", replacementRange: editor.selectedRange())
        guard textView(editor, doCommandBy: enter), textView(editor, doCommandBy: enter),
              editor.string == "☐ Первый\n☐ Второй\n" else {
            throw MacError("Не завершился список с флажками на пустом пункте.")
        }
        list("• ")
        editor.insertText("Пункт", replacementRange: editor.selectedRange())
        guard textView(editor, doCommandBy: enter), editor.string.hasSuffix("• Пункт\n• ") else {
            throw MacError("Не продолжился маркированный список.")
        }
        togglePin()
        guard record.pinned, window.level == .floating, pin?.idleColor == palette.ink else {
            throw MacError("Не закрепилась тестовая заметка.")
        }
    }

    private func image() {
        let picker = NSOpenPanel()
        picker.allowedContentTypes = [.image]
        picker.allowsMultipleSelection = false
        picker.canChooseDirectories = false
        guard picker.runModal() == .OK, let url = picker.url,
              let source = CGImageSourceCreateWithURL(url as CFURL, nil),
              let thumbnail = CGImageSourceCreateThumbnailAtIndex(source, 0, [
                kCGImageSourceCreateThumbnailFromImageAlways: true,
                kCGImageSourceCreateThumbnailWithTransform: true,
                kCGImageSourceThumbnailMaxPixelSize: 245,
              ] as CFDictionary),
              let data = NSBitmapImageRep(cgImage: thumbnail).representation(using: .png, properties: [:]) else { return }
        let file = FileWrapper(regularFileWithContents: data)
        file.preferredFilename = "image-\(UUID().uuidString).png"
        let attachment = NSTextAttachment(fileWrapper: file)
        editor.insertText(NSAttributedString(attachment: attachment), replacementRange: editor.selectedRange())
    }

    func snapshot() -> StickyRecord {
        record.frame = window.frame
        if contentDirty, let text = editor.textStorage {
            if text.length == 0 {
                record.content = nil
                contentDirty = false
            } else if let data = text.rtfd(from: NSRange(location: 0, length: text.length),
                                           documentAttributes: [.documentType: NSAttributedString.DocumentType.rtfd]) {
                record.content = data
                contentDirty = false
            }
        }
        return record
    }

    func move(to proposed: CGRect, bypass: Bool = false) {
        let aligned = bypass ? proposed : NoteSnapping.snap(proposed, to: neighbours?() ?? [])
        window.setFrameOrigin(aligned.origin)
    }

    func verifyHeaderHitTargets() throws {
        window.contentView?.layoutSubtreeIfNeeded()
        let labelPoint = header.convert(CGPoint(x: heading.bounds.midX, y: heading.bounds.midY), from: heading)
        let hitPoint = header.convert(labelPoint, to: header.superview)
        guard header.hitTest(hitPoint) === header, let button = controls.first else {
            throw MacError("Заголовок заметки не доступен для перетаскивания.")
        }
        let buttonPoint = header.convert(CGPoint(x: button.bounds.midX, y: button.bounds.midY), from: button)
        let controlPoint = header.convert(buttonPoint, to: header.superview)
        var hit = header.hitTest(controlPoint)
        while let view = hit, view !== button, view !== header { hit = view.superview }
        guard hit === button else { throw MacError("Примагничивание перехватывает кнопки заголовка.") }
    }

    func textDidChange(_ notification: Notification) {
        contentDirty = true
        didEdit?()
    }
    func windowDidMove(_ notification: Notification) { didEdit?() }
    func windowDidResize(_ notification: Notification) { didEdit?() }
}
