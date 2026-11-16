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
        Section("manage", "Управление", "gearshape")
    ]

    private let smokeTest: Bool
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
    private var localMonitor: Any?
    private var globalMonitor: Any?
    private var reminderTimer: Timer?
    private var shownReminders: [String: Double] = [:]
    private var stickyNotes: MacStickyNotes?

    init(smokeTest: Bool) {
        self.smokeTest = smokeTest
        super.init()
    }

    var animationNames: [String] { pet?.animationNames ?? [] }

    func applicationDidFinishLaunching(_ notification: Notification) {
        do {
            let store = try MacStore()
            let server = try LocalWebServer(store: store)
            let pet = try DesktopPet(bundledRoot: store.root, dataRoot: store.dataDirectory)
            self.store = store
            self.server = server
            self.pet = pet
            pet.refresh(store.readWorkspace())
            if smokeTest {
                guard pet.hasFrame, !animationNames.isEmpty else {
                    throw NSError(domain: "Sputnik", code: 1,
                                  userInfo: [NSLocalizedDescriptionKey: "Первый спрайт не загрузился."])
                }
                openSection("pets")
                DispatchQueue.main.asyncAfter(deadline: .now() + 20) { [weak self] in
                    self?.finishSmokeTest(false, "WKWebView timed out waiting for the shared panel")
                }
                return
            }
            stickyNotes = MacStickyNotes(dataDirectory: store.dataDirectory)
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
        let output = ok ? FileHandle.standardOutput : FileHandle.standardError
        output.write(Data(("Sputnik smoke: " + detail + "\n").utf8))
        exit(ok ? 0 : 1)
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
        browser?.updatePalette(pet.palette, font: pet.fontFamily)
        if menu?.isVisible == true { buildMenu() }
        if !spheres.isEmpty { showFavorites() }
        followPet()
    }

    func setPlayback(_ command: String, index: Int) { pet?.setPlayback(command, index: index) }

    func publish(_ message: [String: Any]) { browser?.publish(message) }

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
                        self?.finishSmokeTest(heading == "Питомцы",
                            error?.localizedDescription ?? heading.map { "decoded sprite, served UI, received bridge actions, rendered \($0)" } ?? "Пустая панель")
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
        if let quick, quick.window.isVisible { position(quick.window) }
        if let bubble, bubble.isVisible { position(bubble) }
        layoutFavorites()
    }
}
