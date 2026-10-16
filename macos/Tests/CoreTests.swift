import Foundation

@main
struct CoreTests {
    static var checks = 0
    static func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
        checks += 1
        guard condition() else { fatalError("FAIL: \(message)") }
    }
    static func machine(_ mappings: [Mapping]) -> LayerMachine {
        let result = LayerMachine()
        result.configuration.mappings = mappings
        return result
    }
    static func main() throws {
        let spaceA = Mapping(layer: .space, input: 0, action: .key, output: 123)
        let normalB = Mapping(layer: .normal, input: 11, action: .key, output: 8)
        let tap = machine([spaceA])
        expect(tap.key(49, down: true) == .consume, "defer Space Down")
        expect(tap.key(49, down: true, repeating: true) == .consume, "hold Space without duplicate output")
        expect(tap.key(49, down: false) == .spaceTap, "short and long unused Space produce one complete tap")
        expect(tap.key(49, down: true, modifiers: .command) == .pass, "preserve Spotlight shortcut")
        expect(tap.key(49, down: false) == .pass, "preserve modified Space release")

        let layer = machine([spaceA])
        _ = layer.key(49, down: true)
        expect(layer.key(0, down: true) == .remap(123, []), "Space+A maps to left arrow")
        expect(layer.key(0, down: true, repeating: true) == .remap(123, []), "mapped repeat")
        expect(layer.key(49, down: false) == .consume, "release Space before child")
        expect(layer.key(0, down: false) == .remap(123, []), "release snapshotted destination after Space release")
        expect(layer.key(0, down: true) == .pass, "next ordinary A unaffected")
        _ = layer.key(0, down: false)

        let typing = machine([spaceA])
        _ = typing.key(49, down: true)
        expect(typing.key(11, down: true) == .spaceDownThenPass, "restore Space before unassigned B")
        expect(typing.key(0, down: true) == .pass, "after typing fallback A stays ordinary")
        expect(typing.key(49, down: false) == .pass, "forward original Space Up after fallback")
        expect(typing.key(11, down: false) == .pass, "unassigned child release is not captured")

        let mouse = machine([spaceA])
        _ = mouse.key(49, down: true)
        expect(mouse.flushPendingSpace() == .spaceDownThenPass, "restore Space before click or modifier")
        expect(mouse.flushPendingSpace() == .pass, "restore Space only once")

        let edit = machine([spaceA, normalB])
        expect(edit.key(11, down: true) == .remap(8, []), "normal key mapping")
        edit.configuration.mappings = []
        expect(edit.key(11, down: true, repeating: true) == .remap(8, []), "editing cannot change held mapping")
        expect(edit.key(11, down: false) == .remap(8, []), "deleted mapping still releases original destination")
        expect(edit.key(11, down: true) == .pass, "new press uses edited configuration")
        edit.configuration.mappings = [normalB]
        expect(edit.key(11, down: false) == .pass, "new mapping cannot capture a preexisting press")

        let startup = machine([normalB])
        expect(startup.key(11, down: true, repeating: true) == .pass, "do not capture keys held before enable")
        expect(startup.key(11, down: false) == .pass, "unknown release passes through")

        let launch = machine([Mapping(layer: .space, input: 0, action: .launch, application: "/Applications/TextEdit.app")])
        _ = launch.key(49, down: true)
        expect(launch.key(0, down: true) == .launch("/Applications/TextEdit.app"), "launch exact selected path")
        expect(launch.key(0, down: true, repeating: true) == .consume, "launch never repeats")
        expect(launch.key(0, down: false) == .consume, "launch consumes matching release")
        expect(launch.reset().isEmpty, "launch has no generated held keys")

        let shortcut = machine([Mapping(layer: .normal, input: 0, action: .shortcut, output: 8, modifiers: [.command, .shift])])
        expect(shortcut.key(0, down: true) == .remap(8, [.command, .shift]), "shortcut modifier snapshot")
        expect(shortcut.reset() == [8], "disable releases held output")
        expect(shortcut.reset().isEmpty, "cleanup is idempotent")
        expect(shortcut.key(0, down: false) == .pass, "late physical release after disable passes")

        let disabled = machine([spaceA])
        disabled.configuration.spaceEnabled = false
        expect(disabled.key(49, down: true) == .pass, "Space toggle off")
        expect(disabled.key(0, down: true) == .pass, "Space mappings inactive when disabled")
        let empty = machine([])
        expect(empty.key(49, down: true) == .pass, "no Space delay without Space mappings")
        expect(empty.key(49, down: true, repeating: true) == .pass, "ordinary Space repeats without mappings")

        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("relyr-mac-tests-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: directory) }
        let store = ConfigurationStore(url: directory.appendingPathComponent("configuration.json"))
        let initial = try store.load()
        expect(initial == Configuration(), "missing configuration uses empty defaults")
        var config = Configuration()
        config.mappings = [spaceA, normalB]
        try store.save(config)
        let loaded = try store.load()
        expect(loaded == config, "configuration round trip")
        var invalid = config
        invalid.mappings.append(spaceA)
        do { try store.save(invalid); fatalError("accepted duplicate mapping") } catch {}
        let preserved = try store.load()
        expect(preserved == config, "invalid edit never replaces saved file")
        invalid = config; invalid.mappings[0].input = 49
        do { try invalid.validate(); fatalError("accepted reserved Space input") } catch {}
        invalid = config; invalid.version = 2
        do { try invalid.validate(); fatalError("accepted unknown schema") } catch {}
        try Data("broken json".utf8).write(to: store.url)
        do { _ = try store.load(); fatalError("silently reset corrupt settings") } catch {}
        let raw = try String(contentsOf: store.url, encoding: .utf8)
        expect(raw == "broken json", "corrupt file preserved for recovery")
        print("PASS: \(checks) core checks plus invalid configuration cases; no physical input generated")
    }
}
