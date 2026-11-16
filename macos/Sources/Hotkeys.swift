import Carbon
import Foundation

private let sputnikHotKeySignature: OSType = 0x53505554

private let sputnikHandleHotKey: EventHandlerUPP = { _, event, context in
    guard let event, let context else { return OSStatus(eventNotHandledErr) }
    var identity = EventHotKeyID()
    let code = GetEventParameter(event, EventParamName(kEventParamDirectObject),
                                 EventParamType(typeEventHotKeyID), nil,
                                 MemoryLayout<EventHotKeyID>.size, nil, &identity)
    guard code == noErr, identity.signature == sputnikHotKeySignature else { return OSStatus(eventNotHandledErr) }
    let owner = Unmanaged<MacHotkeys>.fromOpaque(context).takeUnretainedValue()
    let id = identity.id
    Task { @MainActor [weak owner] in owner?.trigger(id: id) }
    return noErr
}

@MainActor
final class MacHotkeys {
    private struct Binding {
        let id: UInt32
        let modifiers: Int
        let key: Int
        let ref: EventHotKeyRef
    }

    private var handler: EventHandlerRef?
    private var bindings: [String: Binding] = [:]
    private var nextID: UInt32 = 1
    private let sections: Set<String>
    private let open: (String) -> Void

    init(sections: [String], open: @escaping (String) -> Void) {
        self.sections = Set(sections)
        self.open = open
        var event = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyReleased))
        var reference: EventHandlerRef?
        let status = InstallEventHandler(GetApplicationEventTarget(), sputnikHandleHotKey, 1, &event,
                                         Unmanaged.passUnretained(self).toOpaque(), &reference)
        if status == noErr { handler = reference }
    }

    func stop() {
        for item in bindings.values { UnregisterEventHotKey(item.ref) }
        bindings.removeAll()
        if let handler { RemoveEventHandler(handler) }
        handler = nil
    }

    func restore(_ saved: [String: Any]) {
        for (section, value) in saved {
            guard let shortcut = value as? [String: Any], let modifiers = shortcut["modifiers"] as? Int,
                  let key = shortcut["key"] as? Int else { continue }
            try? set(section, modifiers: modifiers, key: key)
        }
    }

    func set(_ section: String, modifiers: Int, key: Int) throws {
        guard sections.contains(section) else { throw MacError("Выберите пункт меню.") }
        let previous = bindings[section]
        if key == 0 {
            if let previous {
                guard UnregisterEventHotKey(previous.ref) == noErr else {
                    throw MacError("Не удалось снять сочетание клавиш.")
                }
                bindings.removeValue(forKey: section)
            }
            return
        }
        guard handler != nil else { throw MacError("Горячие клавиши пока недоступны.") }
        guard (1...15).contains(modifiers), let keyCode = Self.keyCode(key) else {
            throw MacError("Выберите сочетание с буквой, цифрой или клавишей F1–F20.")
        }
        if previous?.key == key && previous?.modifiers == modifiers { return }
        if bindings.values.contains(where: { $0.key == key && $0.modifiers == modifiers }) {
            throw MacError("Сочетание уже назначено другому пункту.")
        }

        let bits: [(Int, UInt32)] = [(1, UInt32(optionKey)), (2, UInt32(controlKey)),
                                     (4, UInt32(shiftKey)), (8, UInt32(cmdKey))]
        let flags = bits.reduce(UInt32(0)) { result, item in (modifiers & item.0) != 0 ? result | item.1 : result }
        let id = nextID
        nextID &+= 1
        var ref: EventHotKeyRef?
        let status = RegisterEventHotKey(keyCode, flags, EventHotKeyID(signature: sputnikHotKeySignature, id: id),
                                         GetApplicationEventTarget(), 0, &ref)
        guard status == noErr, let ref else {
            throw MacError("Сочетание занято или недоступно в macOS. Попробуйте с ⌘ или Control.")
        }
        if let previous, UnregisterEventHotKey(previous.ref) != noErr {
            UnregisterEventHotKey(ref)
            throw MacError("Не удалось заменить сочетание клавиш.")
        }
        bindings[section] = Binding(id: id, modifiers: modifiers, key: key, ref: ref)
    }

    fileprivate func trigger(id: UInt32) {
        if let section = bindings.first(where: { $0.value.id == id })?.key { open(section) }
    }

    private static func keyCode(_ key: Int) -> UInt32? {
        let letters: [Int] = [
            kVK_ANSI_A, kVK_ANSI_B, kVK_ANSI_C, kVK_ANSI_D, kVK_ANSI_E, kVK_ANSI_F, kVK_ANSI_G,
            kVK_ANSI_H, kVK_ANSI_I, kVK_ANSI_J, kVK_ANSI_K, kVK_ANSI_L, kVK_ANSI_M, kVK_ANSI_N,
            kVK_ANSI_O, kVK_ANSI_P, kVK_ANSI_Q, kVK_ANSI_R, kVK_ANSI_S, kVK_ANSI_T, kVK_ANSI_U,
            kVK_ANSI_V, kVK_ANSI_W, kVK_ANSI_X, kVK_ANSI_Y, kVK_ANSI_Z
        ]
        let digits: [Int] = [kVK_ANSI_0, kVK_ANSI_1, kVK_ANSI_2, kVK_ANSI_3, kVK_ANSI_4,
                             kVK_ANSI_5, kVK_ANSI_6, kVK_ANSI_7, kVK_ANSI_8, kVK_ANSI_9]
        let functions: [Int] = [kVK_F1, kVK_F2, kVK_F3, kVK_F4, kVK_F5, kVK_F6, kVK_F7, kVK_F8,
                                kVK_F9, kVK_F10, kVK_F11, kVK_F12, kVK_F13, kVK_F14, kVK_F15,
                                kVK_F16, kVK_F17, kVK_F18, kVK_F19, kVK_F20]
        switch key {
        case 65...90: return UInt32(letters[key - 65])
        case 48...57: return UInt32(digits[key - 48])
        case 112...131: return UInt32(functions[key - 112])
        default: return nil
        }
    }
}
