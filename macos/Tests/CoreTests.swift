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
        for input: UInt16 in [270,271,272,273] + Array(400...425) {
            expect(InputPolicy.allows(input,layer:.trackpad),"Trackpad direction can be assigned")
            expect(!InputPolicy.allows(input,layer:.trackpad,trigger:.hold),"Trackpad cannot retain held actions")
        }
        expect(!InputPolicy.allows(0,layer:.trackpad),"Trackpad excludes keyboard inputs")
        var trackpadConfig = Configuration()
        trackpadConfig.mappings = Array(400...425).map { Mapping(layer:.trackpad,input:UInt16($0),action:.key,output:126) }
        try trackpadConfig.validate()
        let trackpadRoundTrip = try JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(trackpadConfig))
        expect(trackpadRoundTrip.mappings == trackpadConfig.mappings,"Trackpad mapping survives export/import")
        trackpadConfig.mappings.append(Mapping(layer:.trackpad,input:270,action:.key,output:126))
        expectInvalid(trackpadConfig,"Same-direction edge swipe and scrolling cannot double-execute")

        for x in stride(from:0.0,through:500.0,by:25) {
            for y in stride(from:0.0,through:340.0,by:20) {
                let tile = MouseLayoutItem(source:3,x:x,y:y,width:108,height:52).squared
                expect(tile.valid && tile.width == tile.height,"Square mouse tile stays in bounds")
                expect(tile.x.truncatingRemainder(dividingBy:12) == 0 && tile.y.truncatingRemainder(dividingBy:12) == 0,"Mouse tile snaps to grid")
                expect(tile.squared == tile,"Square normalization is stable")
            }
        }
        let mouseDefaults = MouseButtonLayout.standard(0,forward:4,back:3).items
        for (index,item) in mouseDefaults.enumerated() {
            expect(item.valid && item.width == item.height,"Default mouse buttons are squares")
            for other in mouseDefaults.dropFirst(index+1) {
                expect(item.x+item.width <= other.x || other.x+other.width <= item.x || item.y+item.height <= other.y || other.y+other.height <= item.y,"Default mouse squares do not overlap")
            }
        }

        var mouseLayouts = Configuration()
        mouseLayouts.mouseButtonLayouts = (0..<5).map { .standard($0,forward:5,back:6) }
        mouseLayouts.selectedMouseButtonLayout = 4
        try mouseLayouts.validate()
        let layoutRoundTrip = try JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(mouseLayouts))
        expect(layoutRoundTrip == mouseLayouts,"All five mouse layout positions and active pattern survive export/import")
        let legacyLayout = mouseLayouts.mouseButtonLayouts![4]
        var scopedLayout = legacyLayout
        scopedLayout.items.removeFirst()
        mouseLayouts.mouseButtonLayoutsByDevice = ["mx":scopedLayout]
        expect(mouseLayouts.mouseButtonLayout(for:"mx") == scopedLayout &&
               mouseLayouts.mouseButtonLayout(for:"ist") == legacyLayout,
               "Mouse layout follows selected device and other mice inherit the legacy layout")
        let scopedLayoutRoundTrip = try JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(mouseLayouts))
        expect(scopedLayoutRoundTrip == mouseLayouts,"Per-device layouts and saved templates survive export/import")
        var invalidLayout = mouseLayouts; invalidLayout.mouseButtonLayouts![0].items[0].x = -1
        expectInvalid(invalidLayout,"Off-canvas layout rejected")
        invalidLayout = mouseLayouts; invalidLayout.mouseButtonLayouts![0].items.append(invalidLayout.mouseButtonLayouts![0].items[0])
        expectInvalid(invalidLayout,"Duplicate physical button rejected")
        invalidLayout = mouseLayouts; invalidLayout.selectedMouseButtonLayout = 5
        expectInvalid(invalidLayout,"Missing active pattern rejected")
        invalidLayout = mouseLayouts; invalidLayout.mouseButtonLayouts!.append(.standard(5,forward:5,back:6))
        expectInvalid(invalidLayout,"More than five layouts rejected")
        expect(Configuration().mouseButtonLayout(for: nil) == nil,"Older settings preserve original mouse display")
        var deviceMappings = Configuration()
        let commonMouse = Mapping(layer:.space,input:260,action:.key,output:11)
        let mxMouse = Mapping(layer:.space,input:260,action:.key,output:12)
        let commonKeyboard = Mapping(layer:.space,input:0,action:.key,output:13)
        deviceMappings.mappings = [commonMouse,commonKeyboard]
        deviceMappings.setMouseDeviceMapping(mxMouse,deviceID:"046d:b042",profileID:"default",layer:.space,input:260,trigger:.tap)
        expect(deviceMappings.mappings(for:"default",mouseDevice:"046d:b042") == [commonKeyboard,mxMouse],"MX mouse overrides only its mouse slot")
        expect(deviceMappings.mappings(for:"default",mouseDevice:"056e:1234") == [commonMouse,commonKeyboard],"ist pro keeps common assignments")
        deviceMappings.setMouseDeviceMapping(nil,deviceID:"046d:b042",profileID:"default",layer:.space,input:260,trigger:.tap)
        expect(deviceMappings.mappings(for:"default",mouseDevice:"046d:b042") == [commonKeyboard],"Device-specific unassigned slot does not inherit common action")
        try deviceMappings.validate()
        let deviceRoundTrip = try JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(deviceMappings))
        expect(deviceRoundTrip == deviceMappings,"Mouse-specific assignments survive export and import")
        var runtimeDevices = Configuration()
        runtimeDevices.mappings = [Mapping(layer:.normal,input:260,action:.key,output:11), commonKeyboard]
        runtimeDevices.setMouseDeviceMapping(Mapping(layer:.normal,input:260,action:.key,output:12),deviceID:"046d:b042",profileID:"default",layer:.normal,input:260,trigger:.tap)
        let deviceMachine = LayerMachine(); deviceMachine.configuration = runtimeDevices
        deviceMachine.mouseDeviceID = "046d:b042"
        expect(deviceMachine.key(260,down:true) == .remap(12,[]),"MX Master uses its own mouse action")
        _ = deviceMachine.key(260,down:false)
        deviceMachine.mouseDeviceID = "056e:1234"
        expect(deviceMachine.key(260,down:true) == .remap(11,[]),"ist pro uses the common mouse action")
        _ = deviceMachine.key(260,down:false)
        expect(deviceMachine.key(0,down:true,modifiers:[]) == .pass,"Mouse switching leaves ordinary keyboard input untouched")
        _ = deviceMachine.key(0,down:false)
        let indexed = machine([Mapping(layer:.normal,input:0,action:.key,output:11)])
        expect(indexed.key(0,down:true) == .remap(11,[]),"Cached assignment resolves")
        _ = indexed.key(0,down:false)
        indexed.configuration.mappings[0].output = 12
        expect(indexed.key(0,down:true) == .remap(12,[]),"Nested edit invalidates lookup")
        _ = indexed.key(0,down:false)
        let alternate = Profile(name:"Latency fixture",mappings:[Mapping(layer:.normal,input:0,action:.key,output:13)])
        indexed.configuration.profiles = [alternate]; indexed.runtimeProfileID = alternate.id
        expect(indexed.key(0,down:true) == .remap(13,[]),"Profile change invalidates lookup")
        _ = indexed.key(0,down:false)
        indexed.runtimeProfileID = "default"
        expect(indexed.key(0,down:true) == .remap(12,[]),"Returning profile restores its own mapping")
        _ = indexed.key(0,down:false)
        indexed.configuration.mappings = []
        expect(indexed.key(0,down:true) == .pass,"Removed assignment does not remain cached")
        _ = indexed.key(0,down:false)
        let began = ProcessInfo.processInfo.systemUptime
        for _ in 0..<10000 {
            expect(indexed.key(2,down:true) == .pass && indexed.key(2,down:false) == .pass,"Ordinary typing passes unchanged")
            expect(indexed.nextDeadline == nil && indexed.advance().isEmpty,"Ordinary typing creates no hold work")
        }
        print("TIMING: 10000 ordinary key pairs with checks: \(ProcessInfo.processInfo.systemUptime-began)s; state machine only, not OS delivery latency")
        let typingHold = Mapping(layer:.normal,input:5,action:.launch,application:"/Applications/Google Chrome.app",trigger:.hold,holdMilliseconds:500)
        for releaseGFirst in [true, false] {
            let typing = machine([typingHold])
            expect(typing.key(5,down:true,now:0) == .deferPress,"G HOLD waits")
            expect(typing.releaseTypingHolds(before:5).isEmpty,"Same key does not interrupt HOLD")
            expect(typing.releaseTypingHolds(before:0) == [5],"G Down restored before A Down")
            expect(typing.key(0,down:true,now:0.05) == .pass,"A follows restored G")
            expect(typing.advance(now:1).isEmpty,"Typing rollover cannot launch Chrome")
            for code: UInt16 in releaseGFirst ? [5,0] : [0,5] {
                expect(typing.key(code,down:false,now:1.1) == .pass,"Both release orders pass exactly once")
            }
        }
        let fluent = machine([typingHold])
        expect(fluent.key(0,down:true,now:0) == .pass,"Typing starts with unchanged native Down")
        _ = fluent.key(0,down:false,now:0.08)
        expect(fluent.key(5,down:true,now:0.2) == .pass,"HOLD-only G passes at physical Down during typing")
        expect(fluent.nextDeadline == nil && fluent.advance(now:1).isEmpty,"Typing priority schedules no HOLD action")
        expect(fluent.key(5,down:true,repeating:true,now:1.1) == .pass,"Native repeat remains immediate")
        expect(fluent.key(5,down:false,now:1.2) == .pass,"Typing priority preserves native Up")
        expect(fluent.key(5,down:true,now:1.501) == .deferPress,"HOLD returns after 300ms idle")
        expect(fluent.advance(now:2.001).first?.0 == typingHold,"Isolated HOLD still executes once")
        _ = fluent.key(5,down:false,now:2.1)
        let off = machine([typingHold]); off.configuration.typingPriority = false
        _ = off.key(0,down:true,now:0); _ = off.key(0,down:false,now:0.1)
        expect(off.key(5,down:true,now:0.2) == .deferPress,"Opt-out keeps previous HOLD behavior")
        let typingDecoded = try JSONDecoder().decode(Configuration.self,from:ConfigurationStore.encoded(off.configuration))
        expect(typingDecoded.typingPriority == false,"Typing preference survives export/import")
        let tapped = machine([typingHold,Mapping(layer:.normal,input:5,action:.key,output:1)])
        _ = tapped.key(0,down:true,now:0); _ = tapped.key(0,down:false,now:0.1)
        expect(tapped.key(5,down:true,now:0.2) == .deferPress,"Explicit TAP/HOLD is never overridden")
        let controlHold = Mapping(layer:.control,input:5,action:.text,payload:"hold",trigger:.hold)
        let layered = machine([controlHold])
        _ = layered.key(0,down:true,now:0); _ = layered.key(0,down:false,now:0.1)
        expect(layered.key(5,down:true,modifiers:.control,now:0.2) == .deferPress,"Control HOLD keeps its semantics while typing")
        let resetTyping = machine([typingHold])
        _ = resetTyping.key(0,down:true,now:0); _ = resetTyping.reset()
        expect(resetTyping.key(5,down:true,now:0.1) == .deferPress,"Recovery clears typing priority state")
        let isolatedHold = machine([typingHold])
        _ = isolatedHold.key(5,down:true,now:0)
        expect(isolatedHold.advance(now:0.5).first?.0 == typingHold,"Isolated long G still launches")
        expect(isolatedHold.key(5,down:false,now:0.6) == .consume,"Long G release does not type")
        let explicitTap = machine([typingHold,Mapping(layer:.normal,input:5,action:.text,payload:"tap")])
        _ = explicitTap.key(5,down:true,now:0)
        expect(explicitTap.releaseTypingHolds(before:0).isEmpty,"Explicit TAP remains configured")
        var excluded = Configuration()
        excluded.disabledApplications = [DisabledApplication(name:"Test",bundleIdentifier:"test.excluded")]
        excluded.archiveSettings = ArchiveSettings(enabled:false,watchFolder:"/tmp/watch",destinationFolder:"/tmp/out",trashAfterExtraction:false)
        expect(excluded.disablesInput(bundleIdentifier:"test.excluded"),"Exact excluded app match")
        expect(!excluded.disablesInput(bundleIdentifier:"test.excluded.other") && !excluded.disablesInput(bundleIdentifier:nil),"No broad or nil exclusion")
        let decodedExclusions = try JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(excluded))
        expect(decodedExclusions == excluded,"Exclusion and archive preferences persist")
        let jisOutputCodes = Set(Keys.outputs(for:.jis).map(\.code))
        let usOutputCodes = Set(Keys.outputs(for:.us).map(\.code))
        expect([UInt16(93),94,102,104].allSatisfy(jisOutputCodes.contains) && !jisOutputCodes.contains(50),"JIS output chooser follows the physical JIS key set")
        expect(usOutputCodes.contains(50) && [UInt16(93),94,102,104].allSatisfy { !usOutputCodes.contains($0) },"US output chooser excludes JIS-only keys")
        let legacyDeck = Configuration()
        var positionedDeck = legacyDeck
        var positionedLayout = positionedDeck.deckLayout("default")
        positionedLayout.expanded = DeckBounds(x:100,y:100,width:400,height:200)
        positionedLayout.pinned = true; positionedDeck.setDeckLayout(positionedLayout)
        expect(legacyDeck.ignoringDeckPresentation() == positionedDeck.ignoringDeckPresentation(), "First position save preserves editor history despite implicit defaults")
        expect(legacyDeck.preservingDeckPresentation(from:positionedDeck).deckLayout("default") == positionedLayout, "Undo preserves current position and pin")
        var decks = Configuration()
        let standard = DeckItem(title:"Default",profileID:"default",layer:.normal,input:0,directAction:Mapping(layer:.normal,input:0,action:.key,output:8))
        decks.deck = [standard]
        let extra = DeckLayout(name:"Second",columns:3,labels:false,pinned:true,expanded:DeckBounds(x:-900,y:400,width:400,height:200),collapsed:DeckBounds(x:-1200,y:500,width:130,height:34))
        decks.setDeckLayout(extra)
        decks.deck += (0..<3).map { DeckItem(title:"Extra \($0)",profileID:"default",layer:.normal,input:0,directAction:Mapping(layer:.normal,input:0,action:.key,output:9),layoutID:extra.id) }
        decks.deckAfterAction = .collapse; decks.deckOnLeave = .hide
        try decks.validate()
        let savedDecks = try JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(decks))
        expect(savedDecks == decks,"Independent Deck state survives JSON")
        let before = decks.deck
        expect(decks.moveDeck([before[1].id],layout:extra.id,anchoredAt:before[1].id,toSlot:decks.deckPositions(extra.id)[before[3].id]!),"Move within second Deck")
        expect(decks.deckItems("default") == [standard],"Moving second Deck preserves standard Deck")
        expect(!decks.moveDeck([standard.id],layout:extra.id,anchoredAt:standard.id,toSlot:0),"Cross-Deck move cannot overwrite unrelated layout")
        var invalidDeck = decks; invalidDeck.deck[0].layoutID = "missing"; expectInvalid(invalidDeck,"Reject orphan Deck button")
        invalidDeck = decks; invalidDeck.deckLayouts?.append(extra); expectInvalid(invalidDeck,"Reject duplicate Deck identity")
        invalidDeck = decks; invalidDeck.deckLayouts?[0].expanded?.x = .infinity; expectInvalid(invalidDeck,"Reject invalidDeck saved frame")

        expect(DeckMetrics.stepX(labels:true)-54 == DeckMetrics.stepY(labels:true)-52,"Windows equal button gaps including label")
        expect(DeckMetrics.stepX(labels:false) == 58 && DeckMetrics.stepY(labels:false) == 56,"Hidden names remove only reserved name area")
        var sparse = Configuration()
        sparse.deck = (0..<4).map { DeckItem(title:"Slot \($0)",profileID:"default",layer:.normal,input:0,directAction:Mapping(layer:.normal,input:0,action:.disabled),slot:[0,3,5,9][$0]) }
        let sparseIDs = sparse.deck.map { $0.id }
        expect(sparse.moveDeck([sparseIDs[0],sparseIDs[1]],layout:"default",anchoredAt:sparseIDs[0],toSlot:7),"Move group across sparse positions")
        expect(sparse.deckPositions("default")[sparseIDs[0]] == 7 && sparse.deckPositions("default")[sparseIDs[1]] == 10,"Selected items keep gaps")
        expect(sparse.deckPositions("default")[sparseIDs[3]] == 9,"Items in the gaps remain in place")
        expect(sparse.deckPositions("default")[sparseIDs[2]] == 5,"Unrelated sparse slot unchanged")
        expect(!sparse.moveDeck([sparseIDs[0],sparseIDs[1]],layout:"default",anchoredAt:sparseIDs[0],toSlot:29),"Out-of-bounds group rejected without shifting target")
        let restoredSparse = try JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(sparse))
        expect(restoredSparse == sparse,"Sparse positions survive restart")
        var badSparse = sparse; badSparse.deck[1].slot = badSparse.deck[0].slot; expectInvalid(badSparse,"Duplicate slots rejected")
        var keypad = Configuration()
        var keypadLayout = keypad.deckLayout("default")
        keypadLayout.columns = 18
        keypadLayout.rows = 10
        keypad.setDeckLayout(keypadLayout)
        let keypadSlots = [118, 119, 120, 136, 137, 138, 154, 155, 156]
        let keypadItems = keypadSlots.map { DeckItem(title:"Key \($0)", profileID:"default", layer:.normal, input:0, slot:$0) }
        let displaced = DeckItem(title:"Displaced", profileID:"default", layer:.normal, input:0, slot:101)
        let neighbor = DeckItem(title:"Neighbor", profileID:"default", layer:.normal, input:0, slot:103)
        keypad.deck = keypadItems + [displaced, neighbor]
        let keypadIDs = Set(keypadItems.map(\.id))
        for anchor in keypadItems {
            var moved = keypad
            expect(moved.moveDeck(keypadIDs, layout:"default", anchoredAt:anchor.id, toSlot:anchor.slot!-18),
                   "3x3 Deck moves up one row from any grabbed key")
            let positions = moved.deckPositions("default")
            expect(keypadItems.allSatisfy { positions[$0.id] == $0.slot!-18 },
                   "3x3 Deck retains all rows and columns")
            expect(positions[displaced.id] == 155 && positions[neighbor.id] == 103,
                   "Overlapping move swaps only occupied destinations into vacated cells")
        }

        var layoutOptions = sparse.deckLayout("default"); layoutOptions.rows = 7; layoutOptions.panelPadding = 20; layoutOptions.showAtCursor = true; sparse.setDeckLayout(layoutOptions)
        expect(sparse.ignoringDeckPresentation().deckLayout("default").rows == 7,"Undo normalization retains layout options")
        let oldLayout = try JSONDecoder().decode(DeckLayout.self,from:Data(#"{"id":"legacy","name":"Legacy","columns":6,"labels":true,"pinned":false}"#.utf8))
        expect(oldLayout.padding == 12,"Legacy Deck options decode safely")

        var linkedDecks = Configuration()
        linkedDecks.profiles = [Profile(name:"Writer"),Profile(name:"Browser")]
        linkedDecks.deck = [standard]
        let writer = linkedDecks.profiles[0].id, browserDeckProfile = linkedDecks.profiles[1].id
        try linkedDecks.enableDeckProfiles("default",profile:"default")
        let writerDeck = linkedDecks.resolvedDeck("default",profile:writer)!
        expect(writerDeck != "default" && linkedDecks.deckItems(writerDeck).isEmpty,"Profile variants start empty without copying or losing existing assignments")
        expect(linkedDecks.deckItems("default") == [standard],"Linking retains original content")
        linkedDecks.deckDefaults = [writer:writerDeck]
        expect(linkedDecks.resolvedDeck(nil,profile:writer) == writerDeck,"Profile-specific default resolves")
        expect(linkedDecks.visibleDeckIDs(profile:browserDeckProfile).count == 1,"Each profile sees its own variant")
        let linkedRoundtrip = try JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(linkedDecks))
        expect(linkedRoundtrip == linkedDecks,"Deck profile groups/defaults persist")
        linkedDecks.profiles.append(Profile(name:"New")); try linkedDecks.synchronizeDeckProfiles()
        expect(linkedDecks.resolvedDeck("default",profile:linkedDecks.profiles.last!.id) != nil,"New profile receives empty variant")
        _ = linkedDecks.disableDeckProfiles(writerDeck)
        expect(linkedDecks.deckItems("default") == [standard] && linkedDecks.deckLayoutIDs.count == 1,"Disabling retains standard shared Deck")
        try linkedDecks.validate()

        var timerModel = DeckCountdown()
        expect(!timerModel.start(seconds:0,now:100) && !timerModel.start(seconds:.infinity,now:100),"Timer rejects invalid duration")
        expect(timerModel.start(seconds:60,now:100),"Timer accepts valid duration")
        expect(timerModel.label(now:101) == "00:59","Timer displays remaining wall-clock time")
        expect(!timerModel.consumeCompletion(now:159) && timerModel.consumeCompletion(now:160) && !timerModel.consumeCompletion(now:161),"Timer completion is emitted exactly once")
        _ = timerModel.start(seconds:120,now:200); timerModel.cancel(); expect(!timerModel.consumeCompletion(now:500) && timerModel.label(now:500) == "READY","Cancelled timer never completes")
        var hiddenSlots = sparse; var smallLayout = hiddenSlots.deckLayout("default"); smallLayout.rows = 1; smallLayout.columns = 2; hiddenSlots.setDeckLayout(smallLayout)
        expect(hiddenSlots.deckSlotCount("default") == 2 && hiddenSlots.deck == sparse.deck,"Smaller grid hides but preserves outside assignments")
        smallLayout.rows = 7; hiddenSlots.setDeckLayout(smallLayout); expect(hiddenSlots.deckPositions("default") == sparse.deckPositions("default"),"Expansion restores original slot locations")

        var first = GestureDefinition(); first.threshold = 12
        first.actions["right"] = Mapping(layer:.normal,input:0,action:.window,payload:"snapRight")
        first.actions["left"] = Mapping(layer:.normal,input:0,action:.window,payload:"snapLeft")
        var second = first; second.id = UUID().uuidString; second.threshold = 24
        var one = GestureSession(definition:first,source:303,lastMotion:0)
        var two = GestureSession(definition:second,source:304,lastMotion:0)
        one.move(x:7,y:0,now:1); expect(one.commit() == nil,"Small segment waits")
        one.move(x:6,y:0,now:2); expect(one.commit()?.payload == "snapRight","Slow movement accumulates across pauses")
        two.move(x:-13,y:0,now:2); expect(two.commit() == nil,"Second gesture has independent threshold")
        two.move(x:-12,y:0,now:3); expect(two.finish()?.payload == "snapLeft","Second gesture commits final movement")
        one.move(x:13,y:0,now:4); expect(one.commit()?.payload == "snapRight","Repeated direction remains usable")
        expect(one.finish() == nil,"Release does not duplicate committed direction")

        let repeatSpace = machine([Mapping(layer:.space,input:0,action:.key,output:11)])
        expect(repeatSpace.key(49,down:true,now:10) == .consume,"First Space is deferred")
        expect(repeatSpace.key(49,down:false,now:10.1) == .spaceTap,"First tap inserts one Space")
        expect(repeatSpace.key(49,down:true,now:10.3) == .consume,"Second press is deferred")
        expect(repeatSpace.advance(now:10.69).isEmpty,"No repeat before hold delay")
        expect(repeatSpace.advance(now:10.71).first?.0.output == 49,"Double tap hold repeats Space")
        expect(repeatSpace.advance(now:10.72).isEmpty,"Repeat interval is bounded")
        expect(repeatSpace.advance(now:10.78).count == 1,"Continuous repeat")
        expect(repeatSpace.key(49,down:false,now:10.8) == .consume,"Release adds no extra Space")
        expect(repeatSpace.nextDeadline == nil && repeatSpace.advance(now:20).isEmpty,"Release stops repeat")
        _ = repeatSpace.key(49,down:true,now:21); _ = repeatSpace.key(49,down:false,now:21.1)
        _ = repeatSpace.key(49,down:true,now:21.3)
        expect(repeatSpace.key(0,down:true,now:21.4) == .remap(11,[]),"Second press still supports Space layer")
        expect(repeatSpace.advance(now:22).isEmpty,"Layer action cancels repeat")
        _ = repeatSpace.reset()
        _ = repeatSpace.key(49,down:true,now:23); _ = repeatSpace.key(49,down:false,now:23.1)
        _ = repeatSpace.key(49,down:true,now:24)
        expect(repeatSpace.advance(now:25).isEmpty,"Late second press does not repeat")
        _ = repeatSpace.reset()
        expect(repeatSpace.nextDeadline == nil,"Reset cancels repeat")
        for button in UInt16(5)...31 {
            var configuration = Configuration()
            configuration.mappings = [Mapping(layer:.space,input:310,action:.mouse,output:button)]
            try configuration.validate()
            let decoded = try JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(configuration))
            expect(decoded == configuration,"Extra output buttons survive persistence")
        }

        for source: UInt16 in [257,259,260] {
            for layer in [Layer.space,.capsLock] {
                let prefix: UInt16 = layer == .space ? 49 : 105
                let mouseLayer = Layer.allCases.first { $0.mouseCode == source }!
                for prefixReleasedFirst in [false,true] {
                    let test = machine([Mapping(layer:layer,input:source,action:.shortcut,output:12,modifiers:.command),Mapping(layer:mouseLayer,input:0,action:.key,output:1)])
                    _ = test.key(prefix,down:true)
                    expect(test.mouseSource(source,down:true) == .ordinary,"Keyboard-layer button mapping wins over mouse source")
                    expect(test.key(source,down:true) == .remap(12,.command),"Mapped shortcut reaches output")
                    expect(test.mouseLayer == nil,"Mapped child must not arm mouse layer")
                    if prefixReleasedFirst { _ = test.key(prefix,down:false) }
                    expect(test.key(source,down:false) == .remap(12,.command),"Release remains paired after prefix release")
                    if !prefixReleasedFirst { _ = test.key(prefix,down:false) }
                    expect(test.mouseSource(source,down:true) == .consume,"Standalone mouse layer still works after chord")
                    expect(test.mouseSource(source,down:false) == .replayClick,"Standalone short press remains replayable")
                }
                let hold = machine([Mapping(layer:layer,input:source,action:.shortcut,output:12,modifiers:.command,trigger:.hold,holdMilliseconds:100),Mapping(layer:mouseLayer,input:0,action:.key,output:1)])
                _ = hold.key(prefix,down:true)
                expect(hold.mouseSource(source,down:true) == .ordinary,"Keyboard-layer HOLD also wins")
                expect(hold.key(source,down:true,now:0) == .deferPress,"Keyboard-layer HOLD begins")
                expect(hold.advance(now:0.2).count == 1,"Keyboard-layer HOLD executes once")
            }
        }
        var recovery = RecoveryBackoff()
        expect(recovery.ready(now:0),"initial recovery can start")
        recovery.failed(now:10); expect(!recovery.ready(now:10.49) && recovery.ready(now:10.5),"failed tap retries after bounded backoff")
        for n in 1...30 { recovery.failed(now:Double(n)); expect(recovery.retryAt <= Double(n)+5,"repeated failures never disable retry") }
        recovery.reset(); expect(recovery.ready(now:0) && recovery.failures == 0,"successful reconnect clears backoff")
        var startupConfig = Configuration(); startupConfig.startInputOnLaunch = true; startupConfig.loginLaunchDesired = true
        let startupRoundtrip = try JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(startupConfig))
        expect(startupRoundtrip.startInputOnLaunch == true && startupRoundtrip.loginLaunchDesired == true,"explicit login and engine startup choices persist")
        let disabledMachine = machine([Mapping(layer:.normal,input:0,action:.disabled)])
        expect(disabledMachine.key(0,down:true) == .consume && disabledMachine.key(0,down:false) == .consume,"disabled Action owns full input pair")
        let profileMachine = machine([Mapping(layer:.normal,input:0,action:.profile,payload:"default")])
        expect(profileMachine.key(0,down:true) == .profile("default"),"profile Action selected")
        var invalidProfile = Configuration(); invalidProfile.mappings = [Mapping(layer:.normal,input:0,action:.profile,payload:"missing")]
        expectInvalid(invalidProfile,"profile Action reference validated")
        var innerMacro = MacroDefinition(); innerMacro.steps = [MacroStep(delayMilliseconds:20,action:Mapping(layer:.normal,input:0,action:.key,output:8))]
        var outerMacro = MacroDefinition(); outerMacro.steps = [MacroStep(delayMilliseconds:50,action:Mapping(layer:.normal,input:0,action:.macro,payload:innerMacro.id)),MacroStep(delayMilliseconds:100)]
        let plan = try MacroPlan.expand(outerMacro.id,definitions:[innerMacro,outerMacro])
        expect(plan.count == 3 && plan.map { $0.delayMilliseconds } == [50,20,100],"nested macro preserves wait order")
        var playback = MacroPlayback(steps:plan,now:10)
        expect(playback.next(now:10.049) == nil,"macro waits before first step")
        expect(playback.next(now:10.05)?.action == nil,"macro outer wait completes")
        expect(playback.next(now:10.071)?.action?.output == 8,"macro nested Action executes in order")
        playback.cancel(); expect(playback.next(now:100) == nil && playback.finished,"cancel stops even after long wait")
        var macroConfig = Configuration(); macroConfig.macros = [innerMacro,outerMacro]
        macroConfig.mappings = [Mapping(layer:.space,input:0,action:.macro,payload:outerMacro.id)]
        let macroRoundtrip = try JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(macroConfig))
        expect(macroRoundtrip == macroConfig,"macro JSON roundtrip")
        let macroMachine = LayerMachine(); macroMachine.configuration = macroConfig
        _ = macroMachine.key(49,down:true)
        expect(macroMachine.key(0,down:true) == .macro(outerMacro.id),"macro is assignable to Space layer")
        expect(macroMachine.key(0,down:true,repeating:true) == .consume,"macro repeat key does not replay")
        expect(macroMachine.key(0,down:false) == .consume,"macro release owned")
        macroConfig.macros[0].steps = [MacroStep(action:Mapping(layer:.normal,input:0,action:.macro,payload:outerMacro.id))]
        expectInvalid(macroConfig,"nested macro cycle rejected")
        macroConfig.macros = [innerMacro]; expectInvalid(macroConfig,"missing macro reference rejected")
        macroConfig.mappings = []; macroConfig.macros[0].steps[0].delayMilliseconds = 600001; expectInvalid(macroConfig,"macro delay range")
        macroConfig.macros[0] = innerMacro; macroConfig.macros[0].steps = [MacroStep(phase:.move,x:.infinity)]; expectInvalid(macroConfig,"macro nonfinite position rejected")
        macroConfig.macros[0].steps = [MacroStep(phase:.down)]; expectInvalid(macroConfig,"macro Down without key rejected")
        let centerAction = Mapping(layer:.normal,input:0,action:.key,output:8)
        let rightAction = Mapping(layer:.normal,input:0,action:.key,output:9)
        var definition = GestureDefinition()
        definition.actions = ["center":centerAction,"right":rightAction,"left":centerAction,"up":centerAction,"down":rightAction]
        var session = GestureSession(definition:definition,source:257,lastMotion:0)
        expect(session.finish() == centerAction,"gesture short press executes center")
        session.move(x:11,y:0,now:1); expect(session.commit() == nil,"gesture below threshold")
        session.move(x:12,y:12,now:2); expect(session.commit() == rightAction,"45 degree boundary chooses horizontal like Windows")
        session.move(x:20,y:0,now:3); expect(session.commit() == rightAction,"same direction repeats after stop")
        expect(session.finish() == nil,"no center after direction")
        for (x,y,expected) in [(-20.0,0.0,centerAction),(0,-20,centerAction),(0,20,rightAction)] {
            var part = GestureSession(definition:definition,source:257,lastMotion:0)
            part.move(x:x,y:y,now:1); expect(part.finish() == expected,"release commits final direction")
        }
        definition.threshold = 80; definition.locksCursor = false
        expect(session.definition.threshold == 12 && session.definition.locksCursor,"active gesture keeps definition snapshot")
        var slow = GestureSession(definition:definition,source:258,lastMotion:0)
        slow.move(x:20,y:0,now:1); expect(slow.commit() == nil,"independent definition sensitivity")
        var gestureConfig = Configuration(); gestureConfig.gestures = [definition]
        gestureConfig.mappings = [Mapping(layer:.normal,input:257,action:.gesture,payload:definition.id)]
        try gestureConfig.validate()
        let gestureRoundTrip = try JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(gestureConfig))
        expect(gestureRoundTrip == gestureConfig,"gesture settings persist")
        let gm = LayerMachine(); gm.configuration = gestureConfig
        expect(gm.mouseSource(257,down:true) == .ordinary,"direct gesture precedes mouse layer")
        expect(gm.key(257,down:true) == .gesture(definition.id),"gesture starts on press")
        expect(gm.key(257,down:true,repeating:true) == .consume,"gesture repeat does not restart")
        expect(gm.key(257,down:false) == .consume,"gesture release remains owned")
        gestureConfig.gestures[0].threshold = 0; expectInvalid(gestureConfig,"invalid sensitivity rejected")
        gestureConfig.gestures[0] = definition
        gestureConfig.gestures[0].actions["center"] = gestureConfig.mappings[0]; expectInvalid(gestureConfig,"recursive gesture rejected")
        gestureConfig.gestures = []; expectInvalid(gestureConfig,"missing gesture reference rejected")
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
        _ = spaceOutput.key(49, down: true)
        _ = spaceOutput.key(0, down: true)
        expect(spaceOutput.reset().isEmpty, "disable must not release physically held Space")
        let restoredSpace = machine([spaceA])
        _ = restoredSpace.key(49, down: true)
        _ = restoredSpace.flushPendingSpace()
        expect(restoredSpace.reset().isEmpty, "restored Space retains physical ownership on disable")

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
        invalid = expanded; invalid.deck = (0..<325).map { DeckItem(title: "\($0)", profileID: "default", layer: .normal, input: 11) }
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

        let textAction = Mapping(layer: .space, input: 0, action: .text, payload: "こんにちは🌸\nNext line")
        let textMachine = machine([textAction])
        _ = textMachine.key(49, down: true)
        expect(textMachine.key(0, down: true) == .text(textAction.payload), "text action preserves Unicode payload")
        expect(textMachine.key(0, down: true, repeating: true) == .consume, "text action never repeats while held")
        expect(textMachine.key(0, down: false) == .consume, "text action consumes source release")
        expect(textMachine.reset().isEmpty, "text action is not a held virtual A key")
        let urlMachine = machine([Mapping(layer: .normal, input: 0, action: .url, payload: "https://example.com/path")])
        expect(urlMachine.key(0, down: true) == .url("https://example.com/path"), "URL action keeps exact destination")
        expect(urlMachine.key(0, down: true, repeating: true) == .consume, "URL opens once per press")
        expect(urlMachine.reset().isEmpty, "URL never creates held output")
        let namedDeckMachine = machine([Mapping(layer:.normal,input:0,action:.deck,payload:"specific-deck")])
        expect(namedDeckMachine.key(0,down:true) == .deck("specific-deck"),"Named Deck action retains layout identity")
        expect(namedDeckMachine.key(0,down:false) == .consume,"Named Deck release never toggles twice")
        let deckMachine = machine([Mapping(layer: .normal, input: 0, action: .deck)])
        expect(deckMachine.key(0, down: true) == .deck(""), "Deck toggles on press")
        expect(deckMachine.key(0, down: true, repeating: true) == .consume, "Deck must not oscillate on repeat")
        expect(deckMachine.key(0, down: false) == .consume, "Deck toggle release consumed")
        var textConfig = Configuration(); textConfig.mappings = [textAction]
        try textConfig.validate()
        let textEncoded = try ConfigurationStore.encoded(textConfig)
        let textDecoded = try JSONDecoder().decode(Configuration.self, from: textEncoded)
        expect(textDecoded == textConfig, "Unicode payload survives export/import")
        invalid = textConfig; invalid.mappings[0].payload = ""
        expectInvalid(invalid, "reject empty text")
        invalid = textConfig; invalid.mappings[0].payload = String(repeating: "a", count: 4097)
        expectInvalid(invalid, "bound text output")
        for value in ["file:///etc/passwd", "javascript:alert(1)", "https://", "example.com", "ftp://example.com"] {
            invalid = Configuration(); invalid.mappings = [Mapping(layer: .normal, input: 0, action: .url, payload: value)]
            expectInvalid(invalid, "reject unsupported or malformed URL: \(value)")
        }
        let unicode = String(repeating: "a", count: 19) + "🌸" + "日本語\n" + "👨‍👩‍👧‍👦" + String(repeating: "z", count: 40)
        let chunks = TextOutput.chunks(unicode)
        expect(chunks.allSatisfy { !$0.isEmpty && $0.count <= 20 }, "text chunks stay within the event limit")
        expect(chunks.flatMap { $0 } == Array(unicode.utf16), "chunking preserves every UTF-16 unit")
        expect(chunks.allSatisfy { !(0xD800...0xDBFF).contains($0.last!) && !(0xDC00...0xDFFF).contains($0.first!) }, "never split surrogate pairs")
        expect(TextOutput.chunks("").isEmpty, "empty text has no events")
        // Realistic input sequences; no Quartz event is posted by these tests.
        for mouseLayer in [Layer.mouseRight, .mouseBack, .mouseForward] {
            let source = mouseLayer.mouseCode!
            let mapped = Mapping(layer: mouseLayer, input: 0, action: .shortcut, output: 8, modifiers: .command)
            let m = machine([mapped])
            expect(m.mouseSource(source, down: true) == .consume, "defer mouse layer source")
            expect(m.mouseSource(source, down: false) == .replayClick, "unused mouse source restores native click")
            expect(m.mouseSource(source, down: false) == .ordinary, "unknown mouse Up passes")
            _ = m.mouseSource(source, down: true)
            expect(m.key(0, down: true) == .remap(8, .command), "mouse layer keyboard shortcut")
            expect(m.mouseSource(source, down: false) == .consume, "used mouse source never opens context menu")
            m.configuration.mappings = []
            expect(m.key(0, down: false) == .remap(8, .command), "snapshot survives source release and mapping deletion")
            m.configuration.mappings = [mapped]
            _ = m.mouseSource(source, down: true)
            expect(m.key(256, down: true) == .pass, "unassigned left Down stays ordinary")
            expect(m.mouseSource(source, down: false) == .replayClick, "unused source click preserved around child")
            expect(m.key(256, down: false) == .pass, "unassigned child Up after source Up stays ordinary")
            _ = m.mouseSource(source, down: true)
            expect(m.beginMouseDrag(), "unused source begins native drag")
            expect(!m.beginMouseDrag(), "native drag Down emitted once")
            expect(m.mouseSource(source, down: false) == .endDrag, "native drag has matching Up")
            _ = m.mouseSource(source, down: true); _ = m.key(0, down: true)
            expect(!m.beginMouseDrag(), "used layer cannot start accidental native drag")
            expect(m.reset() == [8], "stop releases mouse-layer generated output")
            expect(m.mouseLayer == nil, "stop clears mouse source state")
            expect(m.key(0, down: false) == .pass, "physical release after stop passes")
            let child = machine([Mapping(layer: mouseLayer, input: 256, action: .key, output: 11)])
            _ = child.mouseSource(source, down: true)
            expect(child.key(256, down: true) == .remap(11, []), "layer left click maps")
            expect(child.key(256, down: false) == .remap(11, []), "mapped child mouse Up releases key")
            expect(child.mouseSource(source, down: false) == .consume, "mapped child consumes source")
            let wheel = machine([Mapping(layer: mouseLayer, input: 270, action: .key, output: 126)])
            _ = wheel.mouseSource(source, down: true)
            for _ in 0..<3 {
                expect(wheel.key(270, down: true) == .remap(126, []), "wheel pulse Down")
                expect(wheel.key(270, down: false) == .remap(126, []), "wheel pulse Up")
            }
            expect(wheel.reset().isEmpty, "wheel pulses leave no key held")
            var saved = Configuration(); saved.mappings = [mapped]; saved.keyboardLayout = .jis
            saved.deck = [DeckItem(title: "コピー", profileID: "default", layer: mouseLayer, input: 0)]
            let roundTrip = try JSONDecoder().decode(Configuration.self, from: ConfigurationStore.encoded(saved))
            expect(roundTrip == saved, "mouse layer, JIS and shared Deck survive serialization")
            saved.mappings = [Mapping(layer: mouseLayer, input: source, action: .key, output: 0)]; saved.deck = []
            expectInvalid(saved, "source cannot assign itself")
        }
        let untouched = machine([])
        expect(untouched.mouseSource(257, down: true) == .ordinary, "no layer mapping means no interception")
        expect(untouched.mouseSource(256, down: true) == .ordinary, "left button never becomes a source")
        var protected = Configuration(); protected.mappings = [Mapping(layer: .normal, input: 256, action: .key, output: 0)]
        expectInvalid(protected, "normal left click protected in imported settings")
        untouched.configuration = protected
        expect(untouched.key(256, down: true) == .pass, "runtime also protects malformed normal left mapping")
        expect(untouched.key(256, down: false) == .pass, "protected left Up passes")
        var jis = Configuration()
        jis.mappings = [Mapping(layer: .normal, input: 93, action: .key, output: 94)]
        try jis.validate()
        var legacy = try JSONSerialization.jsonObject(with: ConfigurationStore.encoded(jis)) as! [String: Any]
        legacy.removeValue(forKey: "keyboardLayout")
        let legacyConfig = try JSONDecoder().decode(Configuration.self, from: JSONSerialization.data(withJSONObject: legacy))
        expect(legacyConfig.keyboardLayout == .us && legacyConfig.mappings == jis.mappings, "old settings keep their physical mappings and US view")
        let shortAction = Mapping(layer: .normal, input: 0, action: .key, output: 11)
        let longAction = Mapping(layer: .normal, input: 0, action: .shortcut, output: 8, modifiers: .command, trigger: .hold)
        let timed = machine([shortAction, longAction])
        expect(timed.key(0, down: true, now: 10) == .deferPress, "HOLD delays TAP")
        expect(timed.advance(now: 10.49).isEmpty, "HOLD does not fire early")
        expect(timed.key(0, down: false, now: 10.49) == .pulse(shortAction, []), "short release chooses TAP once")
        expect(timed.advance(now: 11).isEmpty, "released timer cannot fire")
        _ = timed.key(0, down: true, now: 20)
        expect(timed.key(0, down: true, repeating: true, now: 20.1) == .consume, "repeat cannot duplicate deferred TAP")
        let fired = timed.advance(now: 20.5)
        expect(fired.count == 1 && fired[0].0 == longAction, "HOLD fires at threshold")
        expect(timed.advance(now: 21).isEmpty, "HOLD fires once")
        expect(timed.key(0, down: false, now: 21) == .consume, "long release never fires TAP")
        _ = timed.key(0, down: true, now: 30)
        timed.configuration.mappings = []
        expect(timed.key(0, down: false, now: 30.6) == .pulse(longAction, []), "late timer and changed settings retain captured HOLD")
        timed.configuration.mappings = [longAction]
        _ = timed.key(0, down: true, now: 40)
        expect(timed.key(0, down: false, now: 40.1) == .restoreTap(0), "HOLD-only key restores original short input")
        _ = timed.key(0, down: true, now: 50); _ = timed.reset()
        expect(timed.advance(now: 60).isEmpty, "stop cancels all pending HOLD")
        var holdConfiguration = Configuration(); holdConfiguration.mappings = [shortAction,longAction]
        try holdConfiguration.validate()
        let holdDecoded = try JSONDecoder().decode(Configuration.self, from: ConfigurationStore.encoded(holdConfiguration))
        expect(holdDecoded == holdConfiguration, "TAP and HOLD coexist through persistence")
        holdConfiguration.mappings[1].holdMilliseconds = 49
        expectInvalid(holdConfiguration, "invalid hold duration rejected")
        holdConfiguration.mappings[1] = Mapping(layer: .normal, input: 270, action: .key, output: 0, trigger: .hold)
        expectInvalid(holdConfiguration, "wheel cannot hold")
        let mouseLong = Mapping(layer: .normal, input: 257, action: .key, output: 11, trigger: .hold)
        let sourceHold = machine([mouseLong, Mapping(layer: .mouseRight, input: 0, action: .key, output: 8)])
        _ = sourceHold.mouseSource(257, down: true, now: 0)
        expect(sourceHold.advance(now: 0.5).first?.0 == mouseLong, "source button HOLD works while layer configured")
        expect(sourceHold.mouseSource(257, down: false, now: 1) == .consume, "source HOLD suppresses native click")
        _ = sourceHold.mouseSource(257, down: true, now: 2)
        _ = sourceHold.key(0, down: true, now: 2.1)
        expect(sourceHold.advance(now: 3).isEmpty, "using layer cancels source HOLD")
        _ = sourceHold.reset()
        let caps = machine([Mapping(layer: .capsLock, input: 0, action: .key, output: 11)])
        expect(caps.key(105, down: true) == .consume, "F13 starts CapsLock layer")
        expect(caps.key(0, down: true) == .remap(11, []), "CapsLock chord")
        expect(caps.key(105, down: false) == .consume, "used F13 suppressed")
        expect(caps.key(0, down: false) == .remap(11, []), "CapsLock child releases after source")
        _ = caps.key(105, down: true)
        expect(caps.key(105, down: false) == .restoreTap(105), "unused F13 restores original input")
        _ = caps.key(105, down: true)
        expect(caps.key(2, down: true) == .layerDownThenPass(105), "unassigned CapsLock chord preserves input order")
        expect(caps.key(105, down: false) == .pass, "fallback F13 release passes")
        expect(!InputPolicy.allows(256, layer: .menuBar), "menu bar left TAP reserved")
        expect(!InputPolicy.allows(256, layer: .menuBar, trigger: .hold), "menu bar left HOLD reserved")
        expect(!InputPolicy.allows(257, layer: .menuBar), "menu bar right TAP reserved")
        expect(InputPolicy.allows(257, layer: .menuBar, trigger: .hold), "menu bar right HOLD available")
        let menuHold = Mapping(layer: .menuBar, input: 257, action: .window, payload: WindowCommand.toggleMaximize.rawValue, trigger: .hold)
        let menu = machine([menuHold])
        expect(menu.key(257, down: true, now: 0, qualifiedLayer: .menuBar) == .deferPress, "menu bar right hold deferred")
        expect(menu.key(257, down: false, now: 0.1) == .restoreTap(257), "menu bar short right restores click")
        _ = menu.key(257, down: true, now: 1, qualifiedLayer: .menuBar)
        expect(menu.advance(now: 1.5).first?.0 == menuHold, "menu bar hold action fires once")
        expect(menu.key(257, down: false, now: 2) == .consume, "menu bar long right release consumed")
        let middle = machine([Mapping(layer: .menuBar, input: 258, action: .window, payload: WindowCommand.toggleMaximize.rawValue)])
        expect(middle.key(258, down: true, qualifiedLayer: .menuBar) == .window(.toggleMaximize,true), "menu bar action forces active target")
        expect(middle.key(258, down: false) == .consume, "menu bar press owns release outside region")
        expect(middle.key(258, down: true) == .pass, "same input outside menu bar untouched")
        let ctrl = machine([Mapping(layer: .control,input:38,action:.shortcut,output:44,modifiers:.shift)])
        expect(ctrl.usesControlLayer(38,modifiers:.control), "Control selects layer")
        expect(ctrl.key(38,down:true,modifiers:.control) == .remap(44,.shift), "Control J maps to question mark")
        expect(ctrl.usesControlLayer(38,modifiers:[]), "Control release before J retains ownership")
        expect(ctrl.key(38,down:false) == .remap(44,.shift), "Control child releases")
        expect(ctrl.key(8,down:true,modifiers:.control) == .pass, "Unassigned Control C remains native")
        expect(ctrl.key(8,down:false) == .pass, "Unassigned release remains native")
        expect(!InputPolicy.allows(256,layer:.control), "Control click remains protected")
        var scrollConfig = Configuration()
        scrollConfig.reversedWheelLayers = [.normal,.mouseRight]
        let scrollDecoded = try JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(scrollConfig))
        expect(scrollDecoded.reversesWheel(in:.normal) && scrollDecoded.reversesWheel(in:.mouseRight), "Wheel preferences persist")
        expect(scrollDecoded.reversesMouseScroll, "Legacy reversal migrates to global scroll-only option")
        scrollConfig.reverseMouseScroll = false
        expect(!scrollConfig.reversesMouseScroll,"Explicit global OFF overrides legacy layers")
        expect(InputPolicy.allows(0,layer:.menuBar),"Menu bar accepts keyboard mappings")
        expect(Modifiers.fn.rawValue == 16 && Modifiers.all.contains(.fn),"Fn modifier is persistent and supported")
        let fnSplit = machine([Mapping(layer:.normal,input:0,action:.shortcut,output:123,modifiers:[.control,.fn])])
        expect(fnSplit.key(0,down:true) == .window(.snapLeft,false),"Fn Control Left resolves to placement, never desktop shortcut")
        expect(fnSplit.key(0,down:false) == .consume,"Fn placement release emits no key")

        let reversedLayer = machine([])
        reversedLayer.configuration = scrollDecoded
        expect(reversedLayer.mouseSource(257,down:true) == .ordinary, "Scroll reversal alone never captures mouse layer")
        reversedLayer.useWheelLayer()
        expect(reversedLayer.mouseSource(257,down:false) == .ordinary, "Scroll reversal preserves normal mouse release")
        let volume = machine([Mapping(layer:.normal,input:0,action:.system,payload:"volumeUp")])
        expect(volume.key(0,down:true) == .system(.volumeUp), "Volume dispatch")
        expect(volume.key(0,down:false) == .consume, "Volume release does not repeat")
        let modifiedWheel = Mapping(layer:.mouseRight,input:270,action:.system,modifiers:.command,payload:SystemCommand.scrollUp.rawValue)
        let wheelMachine = machine([modifiedWheel])
        _ = wheelMachine.mouseSource(257,down:true)
        expect(wheelMachine.key(270,down:true) == .system(.scrollUp,.command), "Mouse layer preserves wheel modifiers")
        _ = wheelMachine.key(270,down:false); _ = wheelMachine.mouseSource(257,down:false)
        let savedWheel = try JSONDecoder().decode(Mapping.self,from:JSONEncoder().encode(modifiedWheel))
        expect(savedWheel == modifiedWheel, "Modified wheel mapping persists")
        var mouseButtons = Configuration()
        expect(mouseButtons.mouseInput(4) == 260 && mouseButtons.mouseInput(3) == 259, "Legacy side button defaults")
        mouseButtons.registerMouseButton(3,forward:true)
        expect(mouseButtons.mouseInput(3) == 260 && mouseButtons.mouseInput(4) == 259, "Registration swaps reversed side buttons")
        mouseButtons.registerMouseButton(7,forward:true)
        expect(mouseButtons.mouseInput(7) == 260 && mouseButtons.mouseInput(3) == 303, "Extra hardware button registration")
        let mouseRoundTrip = try JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(mouseButtons))
        expect(mouseRoundTrip == mouseButtons, "Hardware button identity persists")
        expect(mouseButtons.mouseInput(0) == 256 && mouseButtons.mouseInput(1) == 257, "Registration never replaces primary mouse buttons")
        mouseButtons.registerMouseButton(4, forward:true, deviceID:"mx")
        expect(mouseButtons.mouseInput(4, deviceID:"mx") == 260 && mouseButtons.mouseInput(7, deviceID:"mx") == 259,
               "Side-button swap is local to the selected mouse")
        expect(mouseButtons.mouseInput(7, deviceID:"ist") == 260 && mouseButtons.mouseInput(4, deviceID:"ist") == 259,
               "Other mice retain the shared side buttons")
        let sideRolesRoundTrip = try JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(mouseButtons))
        expect(sideRolesRoundTrip == mouseButtons, "Per-mouse layer button roles persist")
        mouseButtons.forwardMouseButton = 0
        expectInvalid(mouseButtons,"Left button cannot become a layer source")
        for physical in 3...31 {
            var extra = Configuration()
            let code = extra.mouseInput(Int64(physical))!
            expect(!(270...273).contains(code), "Mouse buttons never alias wheel inputs")
            extra.mappings = [Mapping(layer:.space,input:code,action:.mouse,output:0,modifiers:.shift)]
            try extra.validate()
            let m = machine(extra.mappings)
            _ = m.key(49,down:true)
            expect(m.key(code,down:true) == .mouse(0,.shift), "Extra button maps in Space layer")
            m.configuration.mappings = []
            _ = m.key(49,down:false)
            expect(m.key(code,down:false) == .mouse(0,.shift), "Mouse release snapshots action across edits")
        }
        var namedMouse = Configuration()
        namedMouse.mouseButtonNames = ["305":"Fn1"]
        namedMouse.mappings = [Mapping(layer:.space,input:305,action:.mouse,output:0,modifiers:.control)]
        let namedRoundTrip = try JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(namedMouse))
        expect(namedRoundTrip.inputDetail(305) == "Fn1（macOSボタン番号 5）", "Named mouse input retains physical number in details")
        expect(mouseRoundTrip.inputDetail(260).contains("7"), "Registered forward details use physical button number")
        expect(namedRoundTrip == namedMouse && namedRoundTrip.inputName(305) == "Fn1", "Named mouse actions round-trip without changing assignments")
        var mxMaster = Configuration()
        mxMaster.mappings = [Mapping(layer:.space,input:340,action:.key,output:11)]
        expect(mxMaster.mouseInput(32) == 340 && mxMaster.inputName(340).contains("モード切替"), "MX Master 4 vendor button has a distinct input")
        expect(InputPolicy.allows(340,layer:.space), "MX Master 4 vendor button can be assigned")
        let mxRoundTrip = try JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(mxMaster))
        expect(mxRoundTrip == mxMaster, "MX Master 4 assignment survives export and import")
        let mxMachine = machine(mxMaster.mappings)
        _ = mxMachine.key(49,down:true)
        expect(mxMachine.key(340,down:true) == .remap(11,[]), "Space plus mode key emits configured action")
        expect(mxMachine.key(340,down:false) == .remap(11,[]), "Mode key release balances configured action")
        expect(MouseLayoutItem(source:340,x:0,y:0).valid, "MX Master 4 mode key can be placed in mouse layout")
        namedMouse.mouseButtonNames = ["49":"Fn1"]
        expectInvalid(namedMouse,"Cannot rename keyboard input as a mouse button")
        let defaults = machine([])
        expect(defaults.key(259,down:true) == .system(.navigateBack), "Standalone Back navigates without vendor driver")
        expect(defaults.key(259,down:false) == .consume, "Back release consumed exactly once")
        expect(defaults.key(260,down:true) == .system(.navigateForward), "Standalone Forward navigates")
        expect(defaults.key(260,down:false) == .consume, "Forward release consumed exactly once")
        let customBack = machine([Mapping(layer:.normal,input:259,action:.key,output:11)])
        expect(customBack.key(259,down:true) == .remap(11,[]), "User Back assignment overrides navigation default")
        expect(customBack.key(259,down:false) == .remap(11,[]), "User Back release retained")
        expect(SystemCommand.navigateBack.shortcut(layout:.jis)?.0 == 30 && SystemCommand.navigateForward.shortcut(layout:.jis)?.0 == 42, "JIS bracket navigation uses physical bracket positions")
        expect(SystemCommand.desktop1.shortcut?.0 == 18 && SystemCommand.desktop9.shortcut?.0 == 25, "Numbered desktop shortcuts use number keys")
        var launchPreferences = Configuration()
        expect(launchPreferences.resumeInputOnLaunch != true, "Legacy configuration does not auto-enable input")
        launchPreferences.resumeInputOnLaunch = true
        let restoredPreferences = try JSONDecoder().decode(Configuration.self,from:JSONEncoder().encode(launchPreferences))
        expect(restoredPreferences.resumeInputOnLaunch == true, "Explicit startup preference persists")
        let namesConfig = Configuration()
        var namedApp = DeckItem(title:"Microsoft PowerPoint",profileID:"default",layer:.normal,input:0,directAction:Mapping(layer:.normal,input:0,action:.launch,application:"/Applications/Microsoft PowerPoint.app"))
        expect(namesConfig.deckTitle(namedApp) == "PowerPoint" && namedApp.title == "Microsoft PowerPoint","Office label shortens without modifying saved title")
        namedApp.title = "仕事の資料"
        expect(namesConfig.deckTitle(namedApp) == "仕事の資料","Custom app label remains intact")
        print("PASS: \(checks) core checks plus invalid configuration cases; no physical input generated")
    }
}
