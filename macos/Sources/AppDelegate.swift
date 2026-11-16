import AppKit
import WebKit
import Darwin
import QuartzCore

@main
struct SputnikMain {
    @MainActor static func main() {
        let application = NSApplication.shared
        let delegate = AppDelegate(smokeTest: CommandLine.arguments.contains("--smoke-test"))
        application.setActivationPolicy(.accessory)
        application.delegate = delegate
        withExtendedLifetime(delegate) { application.run() }
    }
}

@MainActor
private final class BrowserPanel: NSObject, WKNavigationDelegate, WKScriptMessageHandler {
    let section: String
    let panel: SputnikPanel
    let web: WKWebView
    private let indexURL: URL
    private let titleLabel = NSTextField(labelWithString: "Спутник")
    private let card: NSView
    private let closeButton: SputnikButton
    private weak var app: AppDelegate?

    init(section: String, url: URL, size: CGSize, palette: SputnikPalette, app: AppDelegate) {
        self.section = section
        indexURL = url
        panel = SputnikPanel(size: size, acceptsKeyboard: true)
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .nonPersistent()
        config.preferences.javaScriptCanOpenWindowsAutomatically = false
        web = WKWebView(frame: .zero, configuration: config)
        card = paletteCard(palette)
        closeButton = SputnikButton(symbol: "xmark", accessibility: "Закрыть окно", tint: palette.ink) {}
        super.init()
        self.app = app
        panel.title = AppDelegate.sections.first(where: { $0.id == section })?.label ?? "Спутник"
        panel.contentView = card
        let titleBar = PanelTitleBar()
        titleBar.translatesAutoresizingMaskIntoConstraints = false
        titleLabel.translatesAutoresizingMaskIntoConstraints = false
        closeButton.translatesAutoresizingMaskIntoConstraints = false
        closeButton.invoke = { [weak panel] in panel?.orderOut(nil) }
        closeButton.keyEquivalent = "\u{1b}"
        web.translatesAutoresizingMaskIntoConstraints = false
        web.navigationDelegate = self
        card.addSubview(web)
        card.addSubview(titleBar)
        titleBar.addSubview(titleLabel)
        titleBar.addSubview(closeButton)
        NSLayoutConstraint.activate([
            titleBar.topAnchor.constraint(equalTo: card.topAnchor, constant: 1),
            titleBar.leadingAnchor.constraint(equalTo: card.leadingAnchor, constant: 1),
            titleBar.trailingAnchor.constraint(equalTo: card.trailingAnchor, constant: -1),
            titleBar.heightAnchor.constraint(equalToConstant: 32),
            titleLabel.leadingAnchor.constraint(equalTo: titleBar.leadingAnchor, constant: 13),
            titleLabel.centerYAnchor.constraint(equalTo: titleBar.centerYAnchor),
            closeButton.trailingAnchor.constraint(equalTo: titleBar.trailingAnchor, constant: -5),
            closeButton.centerYAnchor.constraint(equalTo: titleBar.centerYAnchor),
            closeButton.widthAnchor.constraint(equalToConstant: 26),
            closeButton.heightAnchor.constraint(equalToConstant: 26),
            web.topAnchor.constraint(equalTo: titleBar.bottomAnchor),
            web.leadingAnchor.constraint(equalTo: card.leadingAnchor, constant: 1),
            web.trailingAnchor.constraint(equalTo: card.trailingAnchor, constant: -1),
            web.bottomAnchor.constraint(equalTo: card.bottomAnchor, constant: -1)
        ])
        updatePalette(palette, font: nil)
        web.configuration.userContentController.add(self, name: "sputnik")
    }

    func updatePalette(_ palette: SputnikPalette, font: String?) {
        card.layer?.backgroundColor = palette.surface.cgColor
        card.layer?.borderColor = palette.accent.withAlphaComponent(0.40).cgColor
        titleLabel.textColor = palette.accent
        titleLabel.font = font.flatMap { NSFont(name: $0, size: 13) } ?? .systemFont(ofSize: 13, weight: .semibold)
        closeButton.contentTintColor = palette.ink
        closeButton.hoverColor = palette.accent.withAlphaComponent(0.16)
        web.underPageBackgroundColor = palette.surface
    }

    func start() { web.load(URLRequest(url: indexURL)) }

    func dispose() {
        web.configuration.userContentController.removeScriptMessageHandler(forName: "sputnik")
        web.stopLoading()
        panel.orderOut(nil)
    }

    func publish(_ message: [String: Any]) {
        guard JSONSerialization.isValidJSONObject(message),
              let data = try? JSONSerialization.data(withJSONObject: message),
              let encoded = String(data: data, encoding: .utf8) else { return }
        web.evaluateJavaScript("window.__sputnikReceive?.(\(encoded));", completionHandler: nil)
    }

    private func trusted(_ candidate: URL?) -> Bool {
        guard let candidate else { return false }
        return candidate.scheme == indexURL.scheme && candidate.host == indexURL.host &&
            candidate.port == indexURL.port && candidate.path == indexURL.path
    }

    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard message.name == "sputnik", message.frameInfo.isMainFrame,
              trusted(message.frameInfo.request.url), trusted(web.url),
              let request = message.body as? [String: Any], let id = request["id"] as? String,
              let action = request["action"] as? String else { return }
        Task { @MainActor [weak self] in
            guard let self, let app else { return }
            do {
                let response = try await app.execute(action, payload: request["payload"], browser: self)
                publish(["id": id, "ok": true, "value": response ?? NSNull()])
            } catch {
                publish(["id": id, "ok": false, "error": error.localizedDescription])
            }
        }
    }

    func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction,
                 decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        decisionHandler(action.targetFrame?.isMainFrame == true && trusted(action.request.url) ? .allow : .cancel)
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        app?.showNavigationError(error)
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        app?.showNavigationError(error)
    }
}

@MainActor
private final class PanelTitleBar: NSView {
    override func mouseDown(with event: NSEvent) { window?.performDrag(with: event) }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    struct Section {
        let id: String
        let label: String
        let symbol: String
        let hint: String
        init(_ id: String, _ label: String, _ symbol: String, _ hint: String = "") {
            self.id = id
            self.label = label
            self.symbol = symbol
            self.hint = hint
        }
    }

    static let sections: [Section] = [
        Section("quick", "Быстрый чат", "bubble.left", "3 клика"),
        Section("chat", "Открыть чат", "bubble.left.and.bubble.right"),
        Section("notes", "Заметки", "note.text", "⌘ + 2 клика"),
        Section("favorites", "Сайты и программы", "link"),
        Section("reminders", "Напоминания", "bell"),
        Section("motion", "Движения", "play"),
        Section("pets", "Питомцы", "pawprint"),
        Section("theme", "Внешний вид", "paintpalette"),
        Section("model", "Модель и API", "cpu"),
        Section("manage", "Управление", "gearshape"),
        Section("sharing", "Каналы отправки", "square.and.arrow.up")
    ]

    private let smokeTest: Bool
    private var smokeDirectory: URL?
    private var testedPetImport = false
    private var store: MacStore?
    private var server: LocalWebServer?
    private var pet: DesktopPet?
    private var browser: BrowserPanel?
    private var menu: SputnikPanel?
    private var status: NSStatusItem?
    private var quick: QuickPanel?
    private var bubble: SputnikPanel?
    private var bubbleTimer: Timer?
    private var bubbleEpoch = UUID()
    private var quickBusy = false
    private var dots = 0
    private var spheres: [SputnikPanel] = []
    private var sphereSpacing: CGFloat = 1
    private var sphereOffset: CGFloat = 15
    private var localMonitor: Any?
    private var globalMonitor: Any?
    private var reminderTimer: Timer?
    private var shownReminders: [String: Double] = [:]
    private var stickyNotes: MacStickyNotes?
    private var hotkeys: MacHotkeys?

    init(smokeTest: Bool) {
        self.smokeTest = smokeTest
        super.init()
    }

    var animationNames: [String] { pet?.animationNames ?? [] }

    func applicationDidFinishLaunching(_ notification: Notification) {
        do {
            if smokeTest {
                smokeDirectory = FileManager.default.temporaryDirectory
                    .appendingPathComponent("sputnik-smoke-" + UUID().uuidString, isDirectory: true)
            }
            let store = try MacStore(dataOverride: smokeDirectory)
            let server = try LocalWebServer(store: store)
            let pet = try DesktopPet(bundledRoot: store.root, dataRoot: store.dataDirectory)
            self.store = store
            self.server = server
            self.pet = pet
            pet.refresh(store.readWorkspace())
            if smokeTest {
                DispatchQueue.main.asyncAfter(deadline: .now() + 40) { [weak self] in
                    self?.finishSmokeTest(false, "Timed out waiting for the pet import and shared WKWebView panel")
                }
                Task { @MainActor [weak self] in
                    guard let self else { return }
                    do {
                        try await prepareSmokeTest(store: store, server: server, pet: pet)
                        openSection("pets")
                    } catch {
                        finishSmokeTest(false, error.localizedDescription)
                    }
                }
                return
            }
            stickyNotes = MacStickyNotes(dataDirectory: store.dataDirectory)
            let registry = MacHotkeys(sections: Self.sections.map(\.id)) { [weak self] in self?.openSection($0) }
            registry.restore(hotkeyBindings())
            hotkeys = registry
            installStatus()
            installMonitors()
            pet.onMenu = { [weak self] in self?.toggleMenu() }
            pet.onQuick = { [weak self] in self?.openSection("quick") }
            pet.onNotes = { [weak self] in self?.openSection("notes") }
            pet.onClick = { [weak self] in self?.showFavorites() }
            pet.onMove = { [weak self] in self?.followPet() }
            pet.window.orderFrontRegardless()
            let timer = Timer(timeInterval: 1, target: self, selector: #selector(checkReminders), userInfo: nil, repeats: true)
            RunLoop.main.add(timer, forMode: .common)
            reminderTimer = timer
        } catch {
            if smokeTest { finishSmokeTest(false, error.localizedDescription); return }
            let alert = NSAlert(error: error)
            alert.messageText = "Не удалось запустить «Спутник»"
            alert.runModal()
            NSApp.terminate(nil)
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    func applicationWillTerminate(_ notification: Notification) {
        stickyNotes?.flush()
        hotkeys?.stop()
        browser?.dispose()
        pet?.stop()
        reminderTimer?.invalidate()
        bubbleTimer?.invalidate()
        if let localMonitor { NSEvent.removeMonitor(localMonitor) }
        if let globalMonitor { NSEvent.removeMonitor(globalMonitor) }
    }

    fileprivate func showNavigationError(_ error: Error) {
        if smokeTest { finishSmokeTest(false, error.localizedDescription); return }
        if (error as NSError).code == NSURLErrorCancelled { return }
        let alert = NSAlert(error: error)
        alert.messageText = "Не удалось открыть окно «Спутника»"
        alert.runModal()
    }

    private func finishSmokeTest(_ ok: Bool, _ detail: String) {
        if let smokeDirectory { try? FileManager.default.removeItem(at: smokeDirectory) }
        let output = ok ? FileHandle.standardOutput : FileHandle.standardError
        output.write(Data(("Sputnik smoke: " + detail + "\n").utf8))
        exit(ok ? 0 : 1)
    }

    private func prepareSmokeTest(store: MacStore, server: LocalWebServer, pet: DesktopPet) async throws {
        guard pet.hasFrame, !animationNames.isEmpty else { throw MacError("Первый встроенный спрайт не загрузился.") }
        guard let fixture = ProcessInfo.processInfo.environment["SPUTNIK_TEST_PET"], !fixture.isEmpty else { return }
        let preview = try await store.stagePet(fixture)
        guard let token = preview["token"] as? String else { throw MacError("Пакет тестового питомца не подготовлен.") }
        let installed = try store.installPet(token)
        guard installed["id"] as? String == "knight-watchman", let clips = installed["animations"] as? [[String: Any]],
              clips.count == 5, let file = clips.first?["file"] as? String else {
            throw MacError("В пакете Рыцаря-дозорного ожидалось пять анимаций.")
        }
        try store.writeWorkspace(["active": "knight-watchman", "themed": true])
        pet.refresh(store.readWorkspace())
        guard pet.hasFrame, animationNames.count == 5 else { throw MacError("Спрайт импортированного питомца не загрузился.") }
        var request = URLRequest(url: server.origin.appendingPathComponent(file))
        request.httpMethod = "HEAD"
        let (_, response) = try await URLSession.shared.data(for: request)
        guard (response as? HTTPURLResponse)?.statusCode == 200, response.mimeType == "image/png" else {
            throw MacError("Локальный интерфейс не смог получить кадры импортированного питомца.")
        }
        testedPetImport = true
    }

    private func installStatus() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        guard let button = item.button else { return }
        button.image = NSImage(systemSymbolName: "pawprint.fill", accessibilityDescription: "Спутник")
        button.image?.isTemplate = true
        button.toolTip = "Спутник"
        button.target = self
        button.action = #selector(toggleStatusMenu)
        status = item
    }

    @objc private func toggleStatusMenu() { toggleMenu() }

    private func installMonitors() {
        let masks: NSEvent.EventTypeMask = [.leftMouseDown, .rightMouseDown, .otherMouseDown]
        localMonitor = NSEvent.addLocalMonitorForEvents(matching: masks) { [weak self] event in
            self?.outsideClick(at: NSEvent.mouseLocation)
            return event
        }
        globalMonitor = NSEvent.addGlobalMonitorForEvents(matching: masks) { [weak self] _ in
            let point = NSEvent.mouseLocation
            Task { @MainActor in self?.outsideClick(at: point) }
        }
    }

    private func outsideClick(at point: CGPoint) {
        let inPet = pet?.window.frame.contains(point) == true
        let inMenu = menu?.isVisible == true && menu?.frame.contains(point) == true
        let inTools = browser?.panel.isVisible == true && browser?.panel.frame.contains(point) == true
        let inStatus = status?.button?.window?.frame.contains(point) == true
        if !inPet && !inMenu && !inTools && !inStatus { hideMenu() }
        if !inPet && !spheres.contains(where: { $0.isVisible && $0.frame.contains(point) }) && !inMenu && !inTools {
            hideFavorites()
        }
        if quick?.window.frame.contains(point) != true { quick?.window.orderOut(nil) }
    }

    func refreshPet() {
        guard let pet, let store else { return }
        pet.refresh(store.readWorkspace())
        stickyNotes?.updatePalette(pet.palette)
        browser?.updatePalette(pet.palette, font: pet.fontFamily)
        if menu?.isVisible == true { buildMenu() }
        if !spheres.isEmpty { showFavorites() }
        followPet()
    }

    func setPlayback(_ command: String, index: Int) { pet?.setPlayback(command, index: index) }

    func publish(_ message: [String: Any]) { browser?.publish(message) }

    func hotkeyBindings() -> [String: Any] { store?.readSettings("hotkeys.json") ?? [:] }

    func setHotkey(_ section: String, modifiers: Int, key: Int) throws {
        guard let store, let hotkeys else { throw MacError("Горячие клавиши пока недоступны.") }
        var settings = hotkeyBindings()
        let previous = settings[section] as? [String: Int]
        try hotkeys.set(section, modifiers: modifiers, key: key)
        if key == 0 {
            settings.removeValue(forKey: section)
        } else {
            settings[section] = ["modifiers": modifiers, "key": key]
        }
        do {
            try store.writeSettings(settings, name: "hotkeys.json")
        } catch {
            try? hotkeys.set(section, modifiers: previous?["modifiers"] ?? 0, key: previous?["key"] ?? 0)
            throw error
        }
    }

    func openSection(_ section: String) {
        guard let pet, let server,
              Self.sections.contains(where: { $0.id == section }) || section == "sharing" else { return }
        if section == "quick", !smokeTest {
            showQuick()
            return
        }
        if section == "notes", browser?.section == section, browser?.panel.isVisible == true {
            browser?.panel.orderOut(nil)
            return
        }
        if section == "notes", let stickyNotes, !smokeTest {
            stickyNotes.toggle(palette: pet.palette)
            if menu?.isVisible == true { buildMenu() }
            return
        }
        let heights: [String: CGFloat] = [
            "notes": 376, "model": 430, "theme": 448, "motion": 390, "pets": 390, "manage": 330,
            "chat": 465, "favorites": 446, "reminders": 420, "sharing": 400,
        ]
        let screen = pet.window.screen?.visibleFrame ?? NSScreen.main?.visibleFrame
        let size = CGSize(width: min(490, (screen?.width ?? 520)-24),
                          height: min(heights[section] ?? 390, (screen?.height ?? 510)-24))
        if browser?.section != section {
            browser?.dispose()
            browser = BrowserPanel(section: section, url: server.origin.appendingPathComponent("index.html"),
                                   size: size, palette: pet.palette, app: self)
            browser?.start()
        }
        guard let browser else { return }
        browser.updatePalette(pet.palette, font: pet.fontFamily)
        if !smokeTest {
            position(browser.panel, besideMenu: true)
            NSApp.activate(ignoringOtherApps: true)
            browser.panel.makeKeyAndOrderFront(nil)
        }
    }

    fileprivate func execute(_ action: String, payload: Any?, browser: BrowserPanel) async throws -> Any? {
        guard let store else { return nil }
        switch action {
        case "ready":
            if smokeTest {
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) { [weak self, weak browser] in
                    browser?.web.evaluateJavaScript("document.querySelector('#main h1')?.textContent") { value, error in
                        let heading = value as? String
                        let imported = self?.testedPetImport == true ? ", imported five Knight Watchman animations" : ""
                        self?.finishSmokeTest(heading == "Питомцы",
                            error?.localizedDescription ?? heading.map { "decoded sprite, served UI, received bridge actions, rendered \($0)\(imported)" } ?? "Пустая панель")
                    }
                }
            }
            return browser.section
        case "openSection":
            if let section = payload as? String {
                DispatchQueue.main.async { [weak self] in self?.openSection(section) }
            }
            return nil
        case "desktopMotions":
            return animationNames
        case "desktopPlayback":
            if let dict = payload as? [String: Any], let command = dict["command"] as? String {
                setPlayback(command, index: (dict["index"] as? NSNumber)?.intValue ?? 0)
            }
            return nil
        case "previewFavoriteSpheres":
            showFavorites()
            return nil
        default:
            return try await MacBridge.execute(action, payload: payload, store: store, app: self)
        }
    }

    private func position(_ panel: NSWindow, besideMenu: Bool = false) {
        guard let pet else { return }
        let anchor = pet.window.frame
        guard let area = pet.window.screen?.visibleFrame ?? NSScreen.main?.visibleFrame else { return }
        var f = panel.frame
        let gap: CGFloat = 10
        if besideMenu, let menu, menu.isVisible {
            let m = menu.frame
            let spaceLeft = m.minX - area.minX
            let spaceRight = area.maxX - m.maxX
            f.origin.x = spaceLeft >= f.width + gap || spaceLeft > spaceRight ? m.minX - f.width - gap : m.maxX + gap
            f.origin.y = m.maxY - f.height
        } else {
            f.origin.x = anchor.midX - f.width / 2
            if anchor.maxY + f.height + gap <= area.maxY {
                f.origin.y = anchor.maxY + gap
            } else if anchor.minY - f.height - gap >= area.minY {
                f.origin.y = anchor.minY - f.height - gap
            } else {
                let left = anchor.minX - area.minX
                let right = area.maxX - anchor.maxX
                f.origin.x = left > right ? anchor.minX - f.width - gap : anchor.maxX + gap
                f.origin.y = anchor.midY - f.height / 2
            }
        }
        panel.setFrameOrigin(DesktopPet.clamped(f, to: area.insetBy(dx: 6, dy: 6)).origin)
    }

    private func toggleMenu() {
        if menu?.isVisible == true { hideMenu() } else { buildMenu() }
    }

    private func hideMenu() { menu?.orderOut(nil); menu = nil }

    private func buildMenu() {
        guard let pet else { return }
        hideMenu()
        let palette = pet.palette
        let width: CGFloat = 265
        let rowHeight: CGFloat = 30
        let height: CGFloat = 86 + rowHeight * CGFloat(Self.sections.count)
        let panel = SputnikPanel(size: CGSize(width: width, height: height), acceptsKeyboard: false, level: .popUpMenu)
        let card = paletteCard(palette)
        panel.contentView = card
        let title = NSTextField(labelWithString: pet.name)
        title.textColor = palette.accent
        title.font = pet.fontFamily.flatMap { NSFont(name: $0, size: 15) } ?? .systemFont(ofSize: 15, weight: .semibold)
        title.lineBreakMode = .byTruncatingTail
        title.frame = CGRect(x: 17, y: height-34, width: width-62, height: 21)
        card.addSubview(title)
        let close = SputnikButton(symbol: "xmark", accessibility: "Закрыть меню", tint: palette.ink) { [weak self] in self?.hideMenu() }
        close.frame = CGRect(x: width-35, y: height-35, width: 25, height: 25)
        close.hoverColor = palette.accent.withAlphaComponent(0.12)
        card.addSubview(close)

        for (i, section) in Self.sections.enumerated() {
            let checked = section.id == "notes" && stickyNotes?.isVisible == true
            let button = SputnikButton(title: "  " + section.label, symbol: checked ? "checkmark" : section.symbol,
                                       accessibility: section.label, tint: palette.ink) { [weak self] in self?.openSection(section.id) }
            if let family = pet.fontFamily, let font = NSFont(name: family, size: 13) {
                button.attributedTitle = NSAttributedString(string: button.title, attributes: [.font: font, .foregroundColor: palette.ink])
            }
            button.hoverColor = palette.accent.withAlphaComponent(0.14)
            let y = height-71 - CGFloat(i)*rowHeight
            button.frame = CGRect(x: 12, y: y, width: width-24, height: rowHeight-2)
            card.addSubview(button)
            if !section.hint.isEmpty {
                let hint = NSTextField(labelWithString: section.hint)
                hint.font = .systemFont(ofSize: 10)
                hint.textColor = palette.ink.withAlphaComponent(0.62)
                hint.alignment = .right
                hint.frame = CGRect(x: width-90, y: y+7, width: 72, height: 14)
                card.addSubview(hint)
            }
        }

        let sep = NSView(frame: CGRect(x: 14, y: 43, width: width-28, height: 1))
        sep.wantsLayer = true
        sep.layer?.backgroundColor = palette.accent.withAlphaComponent(0.24).cgColor
        card.addSubview(sep)
        let exitButton = SputnikButton(title: "  Закрыть питомца", symbol: "xmark.circle",
                                        accessibility: "Закрыть питомца", tint: palette.ink) { NSApp.terminate(nil) }
        exitButton.frame = CGRect(x: 12, y: 8, width: width-24, height: 30)
        exitButton.hoverColor = palette.accent.withAlphaComponent(0.14)
        card.addSubview(exitButton)

        menu = panel
        position(panel)
        panel.orderFrontRegardless()
    }

    private func followPet() {
        if let menu, menu.isVisible { position(menu) }
        if let quick, quick.window.isVisible { position(quick.window, besideMenu: true) }
        if let bubble, bubble.isVisible { position(bubble, besideMenu: true) }
        layoutFavorites()
    }

    func showFavorites() {
        guard let pet, let store else { return }
        hideFavorites(animated: false)
        let active = store.readWorkspace()
        let favorites = (active["favorites"] as? [[String: Any]] ?? []).filter { item in
            guard let kind = item["kind"] as? String, ["site", "app"].contains(kind),
                  let target = item["target"] as? String else { return false }
            return !target.isEmpty
        }
        guard !favorites.isEmpty else { return }
        let diameter = CGFloat(max(30, min(86, 54 * ((active["sphereScale"] as? NSNumber)?.doubleValue ?? 100)/100)))
        sphereSpacing = CGFloat((active["sphereSpread"] as? NSNumber)?.doubleValue ?? 100)/100
        sphereOffset = CGFloat((active["sphereOffset"] as? NSNumber)?.doubleValue ?? 15)
        for item in favorites {
            let circle = SputnikPanel(size: CGSize(width: diameter, height: diameter), acceptsKeyboard: false, shadow: false)
            let bg = paletteCard(pet.palette, radius: diameter/2)
            let gloss = CAGradientLayer()
            gloss.type = .radial
            gloss.colors = [pet.palette.accent.withAlphaComponent(0.4).cgColor, pet.palette.surface.cgColor,
                            pet.palette.background.cgColor]
            gloss.locations = [0, 0.65, 1]
            gloss.startPoint = CGPoint(x: 0.28, y: 0.78)
            gloss.endPoint = CGPoint(x: 1, y: 0.08)
            gloss.frame = CGRect(x: 0.6, y: 0.6, width: diameter-1.2, height: diameter-1.2)
            bg.layer?.addSublayer(gloss)
            circle.contentView = bg
            let label = (item["title"] as? String) ?? "Открыть"
            let symbol = item["kind"] as? String == "site" ? symbolForFavorite(item["icon"] as? String) : "app"
            let button = SputnikButton(symbol: symbol, accessibility: label, tint: pet.palette.accent) { [weak self] in
                guard let self, let store = self.store else { return }
                Task { @MainActor in
                    do {
                        _ = try await MacBridge.execute("open", payload: item, store: store, app: self)
                    } catch {
                        showMessage(error.localizedDescription)
                    }
                }
            }
            if let siteIcon = item["siteIcon"] as? String,
               let file = URL(string: siteIcon)?.lastPathComponent,
               file.range(of: #"\A[a-f0-9]+\.png\z"#, options: .regularExpression) != nil {
                let monochrome = item["monochrome"] as? Bool == true
                let mask = String(file.dropLast(4)) + "-mono.png"
                let icon = NSImage(contentsOf: store.icons.appendingPathComponent(monochrome ? mask : file))
                    ?? NSImage(contentsOf: store.icons.appendingPathComponent(file))
                if let icon {
                    icon.size = NSSize(width: diameter*0.57, height: diameter*0.57)
                    icon.isTemplate = monochrome
                    button.image = icon
                }
            }
            button.layer?.cornerRadius = diameter/2
            button.hoverColor = pet.palette.accent.withAlphaComponent(0.16)
            button.frame = CGRect(x: 0, y: 0, width: diameter, height: diameter)
            bg.addSubview(button)
            circle.alphaValue = 0
            spheres.append(circle)
        }
        layoutFavorites()
        NSAnimationContext.runAnimationGroup { animation in
            animation.duration = 0.16
            spheres.forEach { $0.animator().alphaValue = 1 }
        }
    }

    private func symbolForFavorite(_ icon: String?) -> String {
        switch icon {
        case "mail": return "envelope"
        case "video", "play": return "play.circle"
        case "music": return "music.note"
        case "globe": return "globe"
        case "folder": return "folder"
        case "github", "code": return "chevron.left.forwardslash.chevron.right"
        case "search": return "magnifyingglass"
        case "map": return "map"
        case "cloud": return "cloud"
        case "bookmark": return "bookmark"
        default: return "link"
        }
    }

    private func layoutFavorites() {
        guard let pet, let first = spheres.first,
              let area = pet.window.screen?.visibleFrame ?? NSScreen.main?.visibleFrame else { return }
        let width = first.frame.width
        let anchor = pet.window.frame
        let center = CGPoint(x: anchor.midX, y: anchor.midY)
        let region = area.insetBy(dx: 7+width/2, dy: 7+width/2)
        var used: [CGPoint] = []
        let base = anchor.height*0.44 + width/2 + max(0, min(80, sphereOffset))
        let step = width + 7 + 9*max(0, min(1, sphereSpacing))
        let maxRings = Int(max(0, (max(region.width, region.height)/step).rounded(.up)))
        let firstDirection: CGFloat = center.x > area.midX ? 1 : -1

        for offset in 0..<maxRings {
            let radius = base + CGFloat(offset)*step
            let increment = min(CGFloat.pi/2, step/radius)
            let count = Int(CGFloat.pi/2/increment)
            let angles = [CGFloat(0)] + (count == 0 ? [] : (1...count).flatMap { i in
                [firstDirection*CGFloat(i)*increment, -firstDirection*CGFloat(i)*increment]
            })
            for angle in angles {
                let point = CGPoint(x: center.x - radius*sin(angle), y: center.y + radius*cos(angle))
                if region.contains(point), used.allSatisfy({ hypot($0.x-point.x, $0.y-point.y) >= width+4 }) {
                    used.append(point)
                    if used.count == spheres.count { break }
                }
            }
            if used.count == spheres.count { break }
        }

        for (i, sphere) in spheres.enumerated() {
            guard i < used.count else { sphere.orderOut(nil); continue }
            sphere.setFrameOrigin(CGPoint(x: used[i].x-width/2, y: used[i].y-width/2))
            if !sphere.isVisible { sphere.orderFrontRegardless() }
        }
    }

    private func hideFavorites(animated: Bool = true) {
        let retiring = spheres
        spheres = []
        if animated, !retiring.isEmpty {
            NSAnimationContext.runAnimationGroup({ animation in
                animation.duration = 0.14
                retiring.forEach { $0.animator().alphaValue = 0 }
            }, completionHandler: { retiring.forEach { $0.orderOut(nil) } })
        } else {
            retiring.forEach { $0.orderOut(nil) }
        }
    }

    private func showQuick() {
        guard let pet, !quickBusy else { return }
        quick?.window.orderOut(nil)
        quick = QuickPanel(palette: pet.palette, name: pet.name) { [weak self] text in self?.sendQuick(text) }
        guard let quick else { return }
        position(quick.window, besideMenu: true)
        quick.window.alphaValue = 0
        NSApp.activate(ignoringOtherApps: true)
        quick.window.makeKeyAndOrderFront(nil)
        quick.window.makeFirstResponder(quick.field)
        NSAnimationContext.runAnimationGroup { animation in
            animation.duration = 0.16
            quick.window.animator().alphaValue = 1
        }
    }

    private func sendQuick(_ text: String) {
        guard let store, !quickBusy, !text.isEmpty else { return }
        quickBusy = true
        quick?.window.orderOut(nil)
        dots = 0
        showMessage("•  •  •", wait: true)
        let label = bubble?.contentView?.subviews.compactMap { $0 as? NSTextField }.first
        let timer = Timer.scheduledTimer(withTimeInterval: 0.18, repeats: true) { [weak self, weak label] _ in
            Task { @MainActor in
                guard let self else { return }
                self.dots = (self.dots+1)%3
                let dotText = NSMutableAttributedString(string: "")
                for index in 0..<3 {
                    dotText.append(NSAttributedString(string: index == 2 ? "•" : "•  ",
                        attributes: [.font: NSFont.systemFont(ofSize: index == self.dots ? 17 : 11, weight: .semibold)]))
                }
                label?.attributedStringValue = dotText
            }
        }
        bubbleTimer = timer
        Task { @MainActor [weak self] in
            guard let self else { return }
            defer {
                quickBusy = false
                bubbleTimer?.invalidate()
                bubbleTimer = nil
            }
            do {
                let reply = try await MacBridge.sendChat(text, store: store, app: self)
                showMessage(reply)
            } catch {
                showMessage(error.localizedDescription)
            }
        }
    }

    private func showMessage(_ text: String, wait: Bool = false) {
        guard let pet else { return }
        bubble?.orderOut(nil)
        let epoch = UUID()
        bubbleEpoch = epoch
        let line = NSString(string: text)
        let width: CGFloat = wait ? 130 : 330
        let font = NSFont.systemFont(ofSize: 13)
        let measurement = line.boundingRect(with: CGSize(width: width-28, height: 200),
                                             options: [.usesLineFragmentOrigin, .truncatesLastVisibleLine],
                                             attributes: [.font: font])
        let height = max(43, min(180, ceil(measurement.height)+25))
        let panel = SputnikPanel(size: CGSize(width: width, height: height), acceptsKeyboard: false)
        let card = paletteCard(pet.palette, radius: 14)
        let label = NSTextField(wrappingLabelWithString: text)
        label.textColor = pet.palette.ink
        label.font = font
        label.alignment = wait ? .center : .left
        label.maximumNumberOfLines = 8
        label.lineBreakMode = .byTruncatingTail
        let textHeight = min(height-16, max(wait ? 23 : 17, ceil(measurement.height)))
        label.frame = CGRect(x: 14, y: (height-textHeight)/2, width: width-28, height: textHeight)
        card.addSubview(label)
        panel.contentView = card
        panel.ignoresMouseEvents = true
        bubble = panel
        position(panel, besideMenu: true)
        panel.orderFrontRegardless()
        if !wait {
            DispatchQueue.main.asyncAfter(deadline: .now() + 15) { [weak self] in
                if self?.bubbleEpoch == epoch { self?.bubble?.orderOut(nil) }
            }
        }
    }

    @objc private func checkReminders() {
        guard !quickBusy, browser?.panel.isVisible != true,
              let rows = store?.readWorkspace()["reminders"] as? [[String: Any]] else { return }
        let now = Date().timeIntervalSince1970*1_000
        let pending = rows.compactMap { row -> (String, Double, String)? in
            guard let id = row["id"] as? String, let timestamp = row["due"] as? NSNumber,
                  let title = row["title"] as? String, row["done"] as? Bool != true,
                  timestamp.doubleValue <= now, shownReminders[id] != timestamp.doubleValue else { return nil }
            return (id, timestamp.doubleValue, title)
        }
        if let (id, due, title) = pending.min(by: { $0.1 < $1.1 }), bubble?.isVisible != true {
            shownReminders[id] = due
            showMessage(title)
        }
    }
}

@MainActor
private final class QuickPanel: NSObject, NSWindowDelegate {
    let window: SputnikPanel
    let field: NSTextField
    let submit: (String) -> Void

    init(palette: SputnikPalette, name: String, onSend: @escaping (String) -> Void) {
        let size = CGSize(width: 338, height: 43)
        window = SputnikPanel(size: size, acceptsKeyboard: true)
        field = NSTextField(frame: CGRect(x: 13, y: 10, width: 280, height: 23))
        submit = onSend
        super.init()
        let card = paletteCard(palette, radius: 13)
        field.placeholderString = "Спросить \(name)…"
        field.isBordered = false
        field.drawsBackground = false
        field.textColor = palette.ink
        field.font = .systemFont(ofSize: 14)
        field.focusRingType = .none
        field.target = self
        field.action = #selector(send)
        field.cell?.sendsActionOnEndEditing = false
        let button = SputnikButton(symbol: "arrow.up", accessibility: "Отправить", tint: palette.background) { [weak self] in self?.send() }
        button.idleColor = palette.accent
        button.hoverColor = palette.accent.blended(withFraction: 0.20, of: palette.ink) ?? palette.accent
        button.layer?.backgroundColor = palette.accent.cgColor
        button.frame = CGRect(x: 300, y: 6, width: 31, height: 31)
        card.addSubview(field)
        card.addSubview(button)
        window.contentView = card
        window.delegate = self
    }

    @objc private func send() {
        let text = field.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        if !text.isEmpty { submit(text) }
    }

    func windowDidResignKey(_ notification: Notification) { window.orderOut(nil) }
}
