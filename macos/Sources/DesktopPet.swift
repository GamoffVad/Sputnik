import AppKit
import ImageIO
import QuartzCore

struct SputnikPalette {
    var accent: NSColor
    var background: NSColor
    var surface: NSColor
    var ink: NSColor {
        let rgb = surface.usingColorSpace(.sRGB) ?? surface
        return .init(white: rgb.redComponent * 0.2126 + rgb.greenComponent * 0.7152 +
                     rgb.blueComponent * 0.0722 > 0.52 ? 0.10 : 0.96, alpha: 1)
    }

    init(_ theme: [String: Any]?) {
        accent = Self.color(theme?["accent"], fallback: 0xA5E4F6)
        background = Self.color(theme?["background"], fallback: 0x181C20)
        surface = Self.color(theme?["surface"], fallback: 0x23292F)
    }

    private static func color(_ value: Any?, fallback: UInt32) -> NSColor {
        let code = (value as? String).flatMap { text in
            text.hasPrefix("#") && text.count == 7 ? UInt32(text.dropFirst(), radix: 16) : nil
        } ?? fallback
        return NSColor(srgbRed: CGFloat((code >> 16) & 255) / 255,
                       green: CGFloat((code >> 8) & 255) / 255,
                       blue: CGFloat(code & 255) / 255, alpha: 1)
    }
}

@MainActor
final class SputnikPanel: NSPanel {
    private let keyable: Bool

    init(size: CGSize, acceptsKeyboard: Bool, level: NSWindow.Level = .floating, shadow: Bool = true) {
        keyable = acceptsKeyboard
        var mask: NSWindow.StyleMask = [.borderless]
        if !acceptsKeyboard { mask.insert(.nonactivatingPanel) }
        super.init(contentRect: CGRect(origin: .zero, size: size), styleMask: mask,
                   backing: .buffered, defer: false)
        self.level = level
        isOpaque = false
        backgroundColor = .clear
        hasShadow = shadow
        hidesOnDeactivate = false
        isReleasedWhenClosed = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]
        animationBehavior = .none
    }

    override var canBecomeKey: Bool { keyable }
    override var canBecomeMain: Bool { keyable }
}

@MainActor
func paletteCard(_ palette: SputnikPalette, radius: CGFloat = 16, border: Bool = true) -> NSView {
    let card = NSView()
    card.wantsLayer = true
    card.layer?.backgroundColor = palette.surface.cgColor
    card.layer?.cornerRadius = radius
    card.layer?.masksToBounds = true
    card.layer?.borderWidth = border ? 1 : 0
    card.layer?.borderColor = palette.accent.withAlphaComponent(0.40).cgColor
    return card
}

@MainActor
class SputnikButton: NSButton {
    private var tracking: NSTrackingArea?
    var invoke: (() -> Void)?
    var hoverColor: NSColor = .clear
    var idleColor: NSColor = .clear

    init(title: String = "", symbol: String? = nil, accessibility: String,
         tint: NSColor, action: @escaping () -> Void) {
        super.init(frame: .zero)
        self.title = title
        if let symbol, let image = NSImage(systemSymbolName: symbol, accessibilityDescription: accessibility) {
            self.image = image
            imagePosition = title.isEmpty ? .imageOnly : .imageLeft
        } else if title.isEmpty {
            self.title = accessibility
        }
        setAccessibilityLabel(accessibility)
        toolTip = accessibility
        isBordered = false
        contentTintColor = tint
        font = .systemFont(ofSize: 13, weight: .medium)
        alignment = title.isEmpty ? .center : .left
        self.target = self
        self.action = #selector(trigger)
        self.invoke = action
        wantsLayer = true
        layer?.cornerRadius = 7
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError() }

    @objc private func trigger() { invoke?() }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(rect: .zero, options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect],
                                  owner: self, userInfo: nil)
        addTrackingArea(area)
        tracking = area
    }

    override func mouseEntered(with event: NSEvent) { layer?.backgroundColor = hoverColor.cgColor }
    override func mouseExited(with event: NSEvent) { layer?.backgroundColor = idleColor.cgColor }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

private struct DesktopClip: Decodable {
    struct Sheet: Decodable {
        let file: String
        let frames: Int
    }

    let name: String
    let file: String?
    let sheets: [Sheet]?
    let frameWidth: Int?
    let frameHeight: Int?
    let columns: Int?
    let frameCount: Int
    let fps: Int

    func location(_ frame: Int) -> (page: Int, local: Int) {
        var offset = frame
        for (page, sheet) in (sheets ?? []).enumerated() {
            if offset < sheet.frames { return (page, offset) }
            offset -= sheet.frames
        }
        return (0, frame)
    }

    func sheet(at root: URL, page: Int = 0) -> CGImage? {
        let part = sheets.flatMap { $0.indices.contains(page) ? $0[page] : nil }
        guard let filename = part?.file ?? file else { return nil }
        let count = part?.frames ?? frameCount
        guard frameCount > 0, fps > 0,
              let source = CGImageSourceCreateWithURL(root.appendingPathComponent(filename) as CFURL, nil),
              let image = CGImageSourceCreateImageAtIndex(source, 0,
                  [kCGImageSourceShouldCacheImmediately: true] as CFDictionary) else { return nil }
        if frameCount == 1 && frameWidth == nil && frameHeight == nil { return image }
        guard let w = frameWidth, let h = frameHeight, let c = columns, w > 0, h > 0, c > 0,
              c <= image.width / w, (count - 1) / c < image.height / h else { return nil }
        return image
    }

    func frame(_ index: Int, in sheet: CGImage) -> CGImage? {
        guard let w = frameWidth, let h = frameHeight, let c = columns else { return sheet }
        return sheet.cropping(to: CGRect(x: index % c * w, y: index / c * h, width: w, height: h))
    }
}

private struct DesktopPetManifest: Decodable {
    struct Theme: Decodable {
        let accent: String
        let background: String
        let surface: String
        var dict: [String: Any] { ["accent": accent, "background": background, "surface": surface] }
    }
    let id: String
    let name: String
    let animations: [DesktopClip]
    let theme: Theme?
    let displayScale: CGFloat?
}

@MainActor
private final class DesktopSprite: NSView {
    var image: CGImage? { didSet { needsDisplay = true } }
    var clicked: ((NSEvent) -> Void)?
    var context: (() -> Void)?
    var moved: (() -> Void)?
    var dragBegan: (() -> Void)?
    var preferredSize: CGFloat = 230
    private var dragPoint: CGPoint?
    private var start: CGPoint = .zero
    private var dragged = false

    override func draw(_ dirtyRect: NSRect) {
        guard let image, let ctx = NSGraphicsContext.current?.cgContext else { return }
        let scale = min(bounds.width / CGFloat(image.width), bounds.height / CGFloat(image.height))
        let size = CGSize(width: CGFloat(image.width) * scale, height: CGFloat(image.height) * scale)
        let rect = CGRect(x: (bounds.width - size.width) / 2, y: 0, width: size.width, height: size.height)
        ctx.interpolationQuality = .none
        ctx.draw(image, in: rect)
    }

    override var mouseDownCanMoveWindow: Bool { false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func mouseDown(with event: NSEvent) {
        dragPoint = NSEvent.mouseLocation
        start = window?.frame.origin ?? .zero
        dragged = false
    }

    override func mouseDragged(with event: NSEvent) {
        guard let window, let dragPoint else { return }
        let mouse = NSEvent.mouseLocation
        let delta = CGPoint(x: mouse.x - dragPoint.x, y: mouse.y - dragPoint.y)
        guard dragged || hypot(delta.x, delta.y) > 4 else { return }
        if !dragged { dragBegan?() }
        dragged = true
        let area = (NSScreen.screens.first { $0.frame.contains(mouse) } ?? window.screen ?? NSScreen.main)?.visibleFrame
        var frame = window.frame
        frame.origin = CGPoint(x: start.x + delta.x, y: start.y + delta.y)
        if let area {
            let size = min(preferredSize, min(area.width, area.height))
            frame.size = CGSize(width: size, height: size)
        }
        window.setFrame(frame, display: false)
        moved?()
    }

    override func mouseUp(with event: NSEvent) {
        defer { dragPoint = nil }
        if !dragged { clicked?(event) }
    }

    override func rightMouseUp(with event: NSEvent) { context?() }
}

@MainActor
final class DesktopPet: NSObject {
    let window: SputnikPanel
    var onMenu: (() -> Void)?
    var onQuick: (() -> Void)?
    var onNotes: (() -> Void)?
    var onMove: (() -> Void)?
    var onClick: (() -> Void)?
    var onDragBegin: (() -> Void)?
    private let sprite: DesktopSprite
    private let bundled: [DesktopPetManifest]
    private let bundledRoot: URL
    private let dataRoot: URL
    private var selected: DesktopPetManifest?
    private var selectedFolder: URL
    private var timer: Timer?
    private var pending: [Int] = []
    private var unavailable: Set<Int> = []
    private var pinned: Int?
    private var current: Int?
    private var sheet: CGImage?
    private var firstSheet: CGImage?
    private var currentPage = 0
    private var segmentLoader: Task<Void, Never>?
    private var nextSheet: (page: Int, image: CGImage)?
    private var preload: Task<Void, Never>?
    private var preloaded: (index: Int, image: CGImage)?
    private var lastFrame = -1
    private var start = CACurrentMediaTime()
    private var stoppedAt: CFTimeInterval?
    private(set) var palette = SputnikPalette(nil)
    private(set) var fontFamily: String?
    var name: String { selected?.name ?? "Спутник" }
    var animationNames: [String] { selected?.animations.map(\.name) ?? [] }
    var hasFrame: Bool { sprite.image != nil }

    init(bundledRoot: URL, dataRoot: URL) throws {
        self.bundledRoot = bundledRoot
        self.dataRoot = dataRoot
        bundled = try JSONDecoder().decode([DesktopPetManifest].self,
            from: Data(contentsOf: bundledRoot.appendingPathComponent("assets/pets.json")))
        selectedFolder = bundledRoot
        let size: CGFloat = 230
        window = SputnikPanel(size: CGSize(width: size, height: size), acceptsKeyboard: false, shadow: false)
        sprite = DesktopSprite(frame: CGRect(x: 0, y: 0, width: size, height: size))
        super.init()
        sprite.autoresizingMask = [.width, .height]
        sprite.wantsLayer = true
        window.contentView = sprite
        if let screen = NSScreen.main {
            window.setFrameOrigin(CGPoint(x: screen.visibleFrame.maxX - size - 24, y: screen.visibleFrame.minY + 18))
        }
        constrainToScreen()
        NotificationCenter.default.addObserver(self, selector: #selector(screenParametersChanged),
                                               name: NSApplication.didChangeScreenParametersNotification, object: nil)
        sprite.clicked = { [weak self] event in
            switch event.clickCount {
            case 3: self?.onQuick?()
            case 2 where event.modifierFlags.contains(.command): self?.onNotes?()
            default: self?.onClick?()
            }
        }
        sprite.context = { [weak self] in self?.onMenu?() }
        sprite.moved = { [weak self] in self?.onMove?() }
        sprite.dragBegan = { [weak self] in self?.onDragBegin?() }
        let timer = Timer(timeInterval: 1 / 60, target: self, selector: #selector(advance), userInfo: nil, repeats: true)
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    func stop() {
        NotificationCenter.default.removeObserver(self, name: NSApplication.didChangeScreenParametersNotification, object: nil)
        timer?.invalidate()
        timer = nil
        cancelPreload()
    }

    func refresh(_ workspace: [String: Any]) {
        let desired = workspace["active"] as? String ?? "klyaksa"
        if selected?.id != desired || !hasFrame {
            var folder = bundledRoot
            var manifest = bundled.first { $0.id == desired }
            if manifest == nil, desired.range(of: #"\A[a-z0-9][a-z0-9-]{0,48}\z"#, options: .regularExpression) != nil {
                folder = dataRoot.appendingPathComponent("pets").appendingPathComponent(desired)
                if let data = try? Data(contentsOf: folder.appendingPathComponent("pet.json")) {
                    manifest = try? JSONDecoder().decode(DesktopPetManifest.self, from: data)
                }
            }
            if manifest == nil {
                folder = bundledRoot
                manifest = bundled.first { $0.id == "klyaksa" } ?? bundled.first
            }
            if selected?.id != manifest?.id || !hasFrame {
                setPet(manifest, folder: folder)
                if !hasFrame, let fallback = bundled.first(where: { $0.id == "klyaksa" }), manifest?.id != fallback.id {
                    setPet(fallback, folder: bundledRoot)
                }
            }
        }

        let theme = workspace["appearanceMode"] as? String == "custom" ? workspace["customTheme"] as? [String: Any] :
            (workspace["themed"] as? Bool == false ? nil : selected?.theme?.dict)
        palette = SputnikPalette(theme)
        fontFamily = workspace["font"] as? String
        let scale = (workspace["petScale"] as? NSNumber)?.doubleValue ?? 100
        let displayScale = max(0.75, min(1.6, selected?.displayScale ?? 1))
        sprite.preferredSize = 230 * max(0.6, min(1.6, scale / 100)) * displayScale
        resizeToFit()
    }

    @objc private func screenParametersChanged() {
        resizeToFit()
        onMove?()
    }

    // One-time clamp for the very first placement, matching Windows' Loaded-only ClampPetFrame call.
    private func constrainToScreen() {
        guard let area = window.screen?.visibleFrame ?? NSScreen.main?.visibleFrame else { return }
        let width = min(sprite.preferredSize, min(area.width, area.height))
        let old = window.frame
        let new = CGRect(x: old.midX - width / 2, y: old.minY, width: width, height: width)
        let fitted = Self.clamped(new, to: area)
        if old != fitted { window.setFrame(fitted, display: true) }
    }

    // Re-centers on scale/display changes without re-clamping to the screen, matching Windows'
    // unclamped ApplyPetSize so the pet doesn't teleport back once it has been dragged near an edge.
    private func resizeToFit() {
        guard let area = window.screen?.visibleFrame ?? NSScreen.main?.visibleFrame else { return }
        let width = min(sprite.preferredSize, min(area.width, area.height))
        let old = window.frame
        let new = CGRect(x: old.midX - width / 2, y: old.minY, width: width, height: width)
        if old != new { window.setFrame(new, display: true) }
    }

    func setPlayback(_ command: String, index: Int) {
        let now = CACurrentMediaTime()
        switch command {
        case "pause":
            stoppedAt = stoppedAt ?? now
        case "resume":
            if let stoppedAt { start += now - stoppedAt; self.stoppedAt = nil }
        case "clip", "shuffle":
            if command == "clip", selected?.animations.indices.contains(index) != true { return }
            cancelPreload()
            pinned = command == "clip" ? index : nil
            stoppedAt = nil
            pending.removeAll()
            playNext()
        default: break
        }
    }

    static func clamped(_ frame: CGRect, to region: CGRect) -> CGRect {
        CGRect(x: min(max(frame.minX, region.minX), max(region.minX, region.maxX-frame.width)),
               y: min(max(frame.minY, region.minY), max(region.minY, region.maxY-frame.height)),
               width: frame.width, height: frame.height)
    }

    private func setPet(_ manifest: DesktopPetManifest?, folder: URL) {
        cancelPreload()
        selected = manifest
        selectedFolder = folder
        pending.removeAll()
        unavailable.removeAll()
        current = nil
        pinned = nil
        sheet = nil
        firstSheet = nil
        currentPage = 0
        sprite.image = nil
        playNext()
    }

    private func refillQueue() {
        guard let clips = selected?.animations else { return }
        if pending.isEmpty {
            pending = (pinned.map { [$0] } ?? Array(clips.indices)).filter { !unavailable.contains($0) }.shuffled()
            if pending.count > 1, pending.first == current {
                pending.swapAt(0, Int.random(in: 1..<pending.count))
            }
        }
    }

    private func cancelPreload() {
        preload?.cancel()
        preload = nil
        preloaded = nil
        segmentLoader?.cancel()
        segmentLoader = nil
        nextSheet = nil
    }

    private func preloadNext() {
        refillQueue()
        guard let index = pending.first, index != current, let clip = selected?.animations[index] else { return }
        let folder = selectedFolder
        preload = Task(priority: .utility) { [weak self] in
            let image = await Task.detached(priority: .utility) { clip.sheet(at: folder) }.value
            guard !Task.isCancelled, let self, let image else { return }
            preloaded = (index, image)
        }
    }

    private func preloadSegment(_ page: Int, for clip: DesktopClip) {
        guard let sheets = clip.sheets, sheets.indices.contains(page) else { preloadNext(); return }
        let folder = selectedFolder
        segmentLoader = Task(priority: .utility) { [weak self] in
            let image = await Task.detached(priority: .utility) { clip.sheet(at: folder, page: page) }.value
            guard !Task.isCancelled, let self else { return }
            if let image { nextSheet = (page, image) }
            segmentLoader = nil
        }
    }

    private func playNext() {
        guard let clips = selected?.animations, !clips.isEmpty else { return }
        let cached = preloaded
        cancelPreload()
        refillQueue()
        while !pending.isEmpty {
            let index = pending.removeFirst()
            let clip = clips[index]
            let bitmap = index == current ? firstSheet : (cached?.index == index ? cached?.image : clip.sheet(at: selectedFolder))
            if let bitmap, let first = clip.frame(0, in: bitmap) {
                sheet = bitmap
                firstSheet = bitmap
                currentPage = 0
                current = index
                lastFrame = 0
                sprite.image = first
                start = CACurrentMediaTime()
                if stoppedAt != nil { stoppedAt = start }
                preloadSegment(1, for: clip)
                return
            }
            unavailable.insert(index)
        }
    }

    @objc private func advance() {
        guard stoppedAt == nil, let current, let clip = selected?.animations[current], sheet != nil else { return }
        if clip.frameCount == 1, selected?.animations.count == 1 { return }
        let index = Int((CACurrentMediaTime() - start) * Double(clip.fps))
        if index >= clip.frameCount {
            playNext()
        } else if index != lastFrame {
            let location = clip.location(index)
            if location.page != currentPage {
                if segmentLoader != nil { return }
                let prepared = nextSheet?.page == location.page ? nextSheet?.image : nil
                guard let bitmap = prepared ?? clip.sheet(at: selectedFolder, page: location.page) else {
                    unavailable.insert(current)
                    pending.removeAll { $0 == current }
                    playNext()
                    return
                }
                sheet = bitmap
                nextSheet = nil
                currentPage = location.page
                preloadSegment(location.page + 1, for: clip)
            }
            lastFrame = index
            if let sheet, let frame = clip.frame(location.local, in: sheet) { sprite.image = frame }
        }
    }
}
