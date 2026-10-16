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
    static func expectInvalid(_ configuration: Configuration, _ message: String) {
        do { try configuration.validate(); fatalError("FAIL: \(message)") }
        catch { checks += 1 }
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

        let overlap = machine([normalB, Mapping(layer: .normal, input: 0, action: .key, output: 8)])
        _ = overlap.key(11, down: true)
        _ = overlap.key(0, down: true)
        expect(overlap.key(11, down: false) == .consume, "shared output stays down until final mapped source releases")
        expect(overlap.key(0, down: false) == .remap(8, []), "last source releases shared output")
        _ = overlap.key(11, down: true)
        _ = overlap.key(8, down: true)
        expect(overlap.key(11, down: false) == .consume, "physical destination retains ownership")
        expect(overlap.key(8, down: false) == .pass, "physical destination releases normally")
        _ = overlap.key(8, down: true)
        _ = overlap.key(11, down: true)
        expect(overlap.key(8, down: false) == .consume, "remapped source retains ownership after physical destination releases")
        expect(overlap.key(11, down: false) == .remap(8, []), "final remapped release after physical destination")
        _ = overlap.key(8, down: true)
        _ = overlap.key(11, down: true)
        expect(overlap.reset().isEmpty, "disable does not release a physically held destination")

        let spaceOutput = machine([Mapping(layer: .normal, input: 0, action: .key, output: 49)])
        _ = spaceOutput.key(0, down: true)
        _ = spaceOutput.key(49, down: true)
        expect(spaceOutput.key(0, down: false) == .consume, "physical Space retains remapped Space output")
        expect(spaceOutput.key(49, down: false) == .pass, "final physical Space release")
        _ = spaceOutput.key(49, down: true)
        _ = spaceOutput.key(0, down: true)
        expect(spaceOutput.key(49, down: false) == .consume, "remapped Space survives physical Space release")
        expect(spaceOutput.key(0, down: false) == .remap(49, []), "final remapped Space release")

        let disabled = machine([spaceA])
        disabled.configuration.spaceEnabled = false
        expect(disabled.key(49, down: true) == .pass, "Space toggle off")
        expect(disabled.key(0, down: true) == .pass, "Space mappings inactive when disabled")
        let empty = machine([])
        expect(empty.key(49, down: true) == .pass, "no Space delay without Space mappings")
        expect(empty.key(49, down: true, repeating: true) == .pass, "ordinary Space repeats without mappings")

        guard CommandLine.arguments.count == 2 else { fatalError("Pass a repository-local verification directory") }
        let directory = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
            .appendingPathComponent("core-tests-\(UUID().uuidString)")
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
        invalid = config; invalid.version = 999
        do { try invalid.validate(); fatalError("accepted unknown schema") } catch {}
        try Data("broken json".utf8).write(to: store.url)
        do { _ = try store.load(); fatalError("silently reset corrupt settings") } catch {}
        let raw = try String(contentsOf: store.url, encoding: .utf8)
        expect(raw == "broken json", "corrupt file preserved for recovery")

        var expanded = Configuration()
        expanded.mappings = [spaceA, normalB]
        let editor = Profile(name: "Editor", bundleIdentifiers: ["com.apple.TextEdit"], mappings: [
            Mapping(layer: .normal, input: 11, action: .key, output: 2)
        ])
        let browser = Profile(name: "Browser", bundleIdentifiers: ["com.apple.Safari"], mappings: [spaceA])
        expanded.profiles = [editor, browser]
        expanded.manualProfileID = browser.id
        expect(expanded.activeProfileID(bundleIdentifier: "com.apple.TextEdit") == browser.id, "manual profile wins while automatic is off")
        expanded.automaticProfiles = true
        expect(expanded.activeProfileID(bundleIdentifier: "com.apple.TextEdit") == editor.id, "exact foreground bundle chooses profile")
        expect(expanded.activeProfileID(bundleIdentifier: "com.apple.Safari") == browser.id, "other foreground bundle selects its own profile")
        expect(expanded.activeProfileID(bundleIdentifier: "com.apple.TextEdit.fake") == "default", "bundle prefix cannot broaden profile match")
        expect(expanded.activeProfileID(bundleIdentifier: nil) == "default", "unknown foreground falls back to standard")
        expect(expanded.activeProfileID(bundleIdentifier: "io.github.zitan-source.relyr.mac.trial") == "default", "RELYR foreground is an ordinary app, not a previous-profile exception")
        let switching = LayerMachine()
        switching.configuration = expanded
        expect(switching.key(11, down: true) == .remap(8, []), "begin key on standard profile")
        switching.runtimeProfileID = editor.id
        expect(switching.key(11, down: true, repeating: true) == .remap(8, []), "profile switch preserves active repeat target")
        expect(switching.key(11, down: false) == .remap(8, []), "profile switch releases original target")
        expect(switching.key(11, down: true) == .remap(2, []), "next press uses newly active profile")
        _ = switching.reset()
        switching.runtimeProfileID = browser.id
        _ = switching.key(49, down: true)
        expect(switching.key(0, down: true) == .remap(123, []), "profile-local Space mapping")
        switching.runtimeProfileID = editor.id
        expect(switching.key(49, down: false) == .consume, "switch cannot replay consumed layer key")
        expect(switching.key(0, down: false) == .remap(123, []), "Space child output survives profile switch")

        let item = DeckItem(title: "Left", profileID: "default", layer: .space, input: 0)
        expanded.deck = [item]
        try expanded.validate()
        expect(expanded.mapping(for: item) == spaceA, "Deck resolves shared mapping")
        let changed = Mapping(layer: .normal, input: 2, action: .key, output: 124)
        expanded.replaceMapping(changed, at: 0, profileID: "default")
        expect(expanded.mapping(for: expanded.deck[0]) == changed, "editing source updates live Deck reference")
        expect(expanded.deck[0].title == "Left", "editing output preserves custom Deck label")
        expect(expanded.profiles == [editor, browser], "editing standard leaves other profiles unchanged")
        expanded.removeMapping(at: 0, profileID: "default")
        expect(expanded.deck.isEmpty, "deleting mapping removes its Deck references")
        expanded.deck = [DeckItem(title: "Browser Left", profileID: browser.id, layer: .space, input: 0)]
        expanded.removeProfile(browser.id)
        expect(expanded.manualProfileID == "default" && expanded.deck.isEmpty, "deleting manual profile resets selection and removes its Deck")
        expect(expanded.profiles == [editor], "deleting one profile preserves unrelated profile")
        invalid = expanded; invalid.manualProfileID = "missing"
        expectInvalid(invalid, "reject missing manual profile")
        invalid = expanded; invalid.profiles.append(Profile(name: "Conflict", bundleIdentifiers: ["com.apple.TextEdit"]))
        expectInvalid(invalid, "reject ambiguous automatic routing")
        invalid = expanded; invalid.profiles.append(editor)
        expectInvalid(invalid, "reject duplicate profile IDs")
        invalid = expanded; invalid.profiles[0].id = "default"
        expectInvalid(invalid, "protect standard profile ID")
        invalid = expanded; invalid.profiles[0].name = "   "
        expectInvalid(invalid, "reject blank profile name")
        invalid = expanded; invalid.deck = [item]
        expectInvalid(invalid, "reject dangling Deck mapping")
        invalid = expanded
        let existing = DeckItem(title: "C", profileID: "default", layer: .normal, input: 11)
        invalid.deck = [existing, existing]
        expectInvalid(invalid, "reject duplicate Deck IDs")
        invalid = expanded; invalid.deck = [DeckItem(title: "", profileID: "default", layer: .normal, input: 11)]
        expectInvalid(invalid, "reject blank Deck title")
        invalid = expanded; invalid.deck = (0..<49).map { DeckItem(title: "\($0)", profileID: "default", layer: .normal, input: 11) }
        expectInvalid(invalid, "enforce Deck bound")

        // Round trip with profile-specific mappings and Deck references.
        expanded.deck = [existing]
        let export = try ConfigurationStore.encoded(expanded)
        let roundTrip = try JSONDecoder().decode(Configuration.self, from: export)
        expect(roundTrip == expanded, "extended configuration round trip")
        let imported = try store.importConfiguration(expanded)
        expect(imported == expanded, "valid import can recover a corrupt current file")
        let backups = try FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)
            .filter { $0.lastPathComponent.hasPrefix("configuration.before-import-") }
        expect(backups.count == 1, "import creates recovery backup")
        let backupData = try String(contentsOf: backups[0], encoding: .utf8)
        expect(backupData == "broken json", "import preserves corrupt original bytes")
        let source = directory.appendingPathComponent("import.json")
        try Data("invalid".utf8).write(to: source)
        do { _ = try store.importConfiguration(from: source); fatalError("accepted invalid import") } catch { checks += 1 }
        let afterInvalidImport = try store.load()
        expect(afterInvalidImport == expanded, "invalid import cannot replace settings")
        try Data(repeating: 32, count: 1_048_577).write(to: source)
        do { _ = try ConfigurationStore.read(source); fatalError("accepted oversized import") } catch { checks += 1 }

        // Encode actual v1-shaped bytes rather than using the new encoder to simulate migration.
        let legacyObject: [String: Any] = ["version": 1, "spaceEnabled": true,
            "mappings": [["layer": "space", "input": 0, "action": "key", "output": 123,
                          "modifiers": 0, "application": ""]]]
        let legacyData = try JSONSerialization.data(withJSONObject: legacyObject, options: [.sortedKeys])
        try legacyData.write(to: store.url)
        let migrated = try store.load()
        expect(migrated.version == 2 && migrated.mappings == [spaceA], "v1 upgrade preserves exact assignments")
        expect(migrated.profiles.isEmpty && migrated.deck.isEmpty && !migrated.automaticProfiles, "v1 upgrade adds no implicit actions or routes")
        try store.save(migrated)
        let legacyBackup = directory.appendingPathComponent("configuration.v1.backup.json")
        let savedLegacy = try Data(contentsOf: legacyBackup)
        expect(savedLegacy == legacyData, "upgrade keeps byte-for-byte legacy backup")
        try store.save(expanded)
        let unchangedLegacy = try Data(contentsOf: legacyBackup)
        expect(unchangedLegacy == legacyData, "later saves never overwrite migration backup")
        var missingV2 = legacyObject; missingV2["version"] = 2
        let missingData = try JSONSerialization.data(withJSONObject: missingV2)
        do { _ = try JSONDecoder().decode(Configuration.self, from: missingData); fatalError("silently defaulted damaged v2") } catch { checks += 1 }
        print("PASS: \(checks) core checks plus invalid configuration cases; no physical input generated")
    }
}
