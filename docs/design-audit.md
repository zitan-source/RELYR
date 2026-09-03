# RELYR website — 100-point design audit

Reference: Apple Final Cut Pro. Every item below is resolved in the current `docs/` implementation.

## First impression

1. [x] The old hero looked like a standard SaaS template — replaced with a compact editorial opening built around two live product interactions.
2. [x] The first screen lacked a single dominant idea — reduced to one two-line promise.
3. [x] The product was visually secondary — the real workspace now rises full-width beneath the promise.
4. [x] Full-screen product shots made the page tiring — the hero now spotlights the keyboard assignment and live Deck in two focused crops.
5. [x] Too many ideas competed above the fold — secondary specifications were removed.
6. [x] The hero used generic “more features” language — rewritten around a concrete key-layer behavior.
7. [x] The composition resembled a corporate brochure — replaced with an editorial product reveal.
8. [x] The screen did not invite scrolling — the product edge is deliberately visible at the fold.
9. [x] The background grid felt like an AI-tech cliché — replaced with restrained ambient light.
10. [x] Decorative elements had no product meaning — retained only depth lines tied to Input and Action.

## Typography

11. [x] The headline was too small to own the opening — enlarged on wide screens with a two-line limit.
12. [x] The headline weight looked generic — reduced to a calmer variable-display weight.
13. [x] The headline tracking was timid — optically tightened at display sizes.
14. [x] The headline leading lacked tension — tuned separately for desktop and mobile.
15. [x] Japanese line breaks felt accidental — explicit semantic lines are used.
16. [x] Long German copy overflowed — locale-specific mobile sizing and natural wrapping were added.
17. [x] French and Spanish could create six-line headings — locale-specific sizing keeps the hero to two lines and section headings to four or fewer on mobile.
18. [x] Korean rendered as missing-glyph boxes — Noto Sans KR and Windows fallback fonts were added.
19. [x] Simplified Chinese used Japanese glyph forms — Noto Sans SC is selected by `lang`.
20. [x] Traditional Chinese used the wrong glyph forms — Noto Sans TC is selected by `lang`.
21. [x] UI copy and technical metadata used the same voice — display, body, and mono roles are separated.
22. [x] Paragraphs were too wide — readable maximum measures are enforced.
23. [x] Paragraph leading felt machine-generated — tightened to content-specific values.
24. [x] Small text lacked contrast hierarchy — muted and quiet tones now have distinct roles.
25. [x] Bold text was overused — emphasis is carried by scale, position, and one accent line.

## Spacing and rhythm

26. [x] Spacing was uniformly distributed — each chapter now has a distinct cinematic rhythm.
27. [x] Hero content sat too close to the fixed header — a deliberate opening field was added.
28. [x] Hero media started too late — raised to touch the first viewport edge.
29. [x] Section headings and media were cramped — larger chapter transitions were introduced.
30. [x] The privacy section had dead vertical space — its sticky copy and ledger now share one rhythm.
31. [x] Download content felt detached — it is now a deliberate high-contrast closing scene.
32. [x] Mobile gutters varied by component — one responsive gutter controls the page.
33. [x] Desktop content width was arbitrary — bounded editorial and media widths are separated.
34. [x] Captions floated without alignment — each is locked to its media edge.
35. [x] Repeated equal gaps made the page mechanical — spacing changes by content role.

## Navigation and wayfinding

36. [x] The floating rounded header looked like a template — replaced with a minimal transparent local nav.
37. [x] The header became unreadable over content — a restrained material appears only after scrolling.
38. [x] The numbered chapter rail looked like a portfolio cliché — removed completely.
39. [x] Cheap “01 — SECTION” labels looked generated — removed from markup, not merely hidden.
40. [x] Header download styling looked like a green sales button — changed to a quiet text control.
41. [x] Navigation did not expose language — an eight-language control is always available.
42. [x] Language selection risked a white native dropdown — implemented as a fully themed menu.
43. [x] The current language was unclear — the native language name is always shown.
44. [x] Language choice was forgotten — persisted locally across pages and visits.
45. [x] The menu remained open after selection — it closes immediately and on outside click.

## Product imagery

46. [x] Old or generic mockups weakened trust — only user-provided current product captures are used.
47. [x] Screenshots were trapped in repetitive cards — media is treated as the page architecture.
48. [x] The Main screen was too small — it is now the dominant visual plane.
49. [x] Deck was presented as another ordinary screenshot — converted into a scroll runway.
50. [x] Gestures were disconnected from the main product — layered into the opening product space.
51. [x] Deck and Gesture depth had no hierarchy — separate Z-depth and rotations were assigned.
52. [x] Screenshot framing was overly rounded — reduced to precise, restrained radii.
53. [x] Shadows looked like soft AI mockups — replaced with directional, deeper product shadows.
54. [x] Hover zoom was abrupt — slowed and reduced to a subtle optical response.
55. [x] Large images could shift layout while loading — intrinsic width and height are declared.
56. [x] Below-fold images loaded eagerly — lazy loading is used after the hero.
57. [x] Screenshots had Japanese descriptions in English mode — captions and alt text now follow locale.
58. [x] English mode could show Japanese interface captures — all supplied product captures are the English set.
59. [x] Enlarged media lacked context — the dialog caption follows the selected language.
60. [x] The social image represented the previous design — aligned with the current product imagery and color system.

## Motion

61. [x] Motion was limited to generic fade-ins — introduced restrained crop reveals and scroll-linked image travel.
62. [x] Scroll effects did not express the product — Layers, Deck, Macro, and Gesture now reveal the exact controls being described.
63. [x] The hero could pin a taller-than-viewport collage — the stage now fits beside the copy and settles subtly with scroll.
64. [x] Deck sticky behavior failed under overflow ancestors — overflow containment was corrected.
65. [x] Deck movement did not reveal its width — vertical scroll now drives horizontal travel.
66. [x] Action rows were tiny and low contrast — replaced with a large, five-part capability index using concrete outcomes.
67. [x] Workflow images moved identically — Macro stays anchored while Gesture settles independently.
68. [x] Animation could feel detached from the scrollbar — native requestAnimationFrame rendering follows actual scroll position.
69. [x] External animation failure could break the page — scroll motion and all three product videos have no animation-library dependency.
70. [x] Video controls interrupted the product story — muted clips start when visible, pause off-screen, and loop without play buttons; reduced motion still removes decorative transforms.
71. [x] Transitions could continue after interruption — scroll state is recalculated per animation frame.
72. [x] Motion work ran continuously while idle — rendering stops once the smoothed position settles.
73. [x] Scroll progress was approximate — it uses actual document and viewport dimensions.
74. [x] Resize could invalidate motion bounds — metrics are remeasured on resize and load.
75. [x] Scripted smooth scrolling could fight the browser — motion never synthesizes or overrides the user's scroll position.

## Localization

76. [x] The website language was fixed to Japanese — eight app-matching locales are supported.
77. [x] Browser language was ignored — the initial locale follows saved choice, then browser preference.
78. [x] English metadata remained Japanese — title, description, and Open Graph text update by locale.
79. [x] Buttons mixed Japanese and English — navigation and actions update together.
80. [x] Image alt text stayed Japanese — all six product descriptions update by locale.
81. [x] Dialog controls stayed Japanese — close and enlargement labels update by locale.
82. [x] Download requirements stayed English — price and account status are localized.
83. [x] Privacy content broke language continuity — the legal page uses the same eight locales.
84. [x] The 404 page broke language continuity — it inherits and renders the selected locale.
85. [x] CJK locale fonts were not script-specific — JP, KR, SC, and TC font stacks are explicit.
86. [x] Language switching required a reload — copy updates immediately in place.
87. [x] Language state could diverge between tabs — one stable local-storage key is used.
88. [x] Screen-reader landmarks kept Japanese labels — navigation and control labels update with locale.

## Interaction and accessibility

89. [x] Keyboard focus was easy to lose — a visible accent focus ring is global.
90. [x] The language chooser was mouse-only — native `details`, `summary`, and buttons remain keyboard-operable.
91. [x] Screenshot zoom lacked an explicit accessible name — localized names are applied to every trigger.
92. [x] The image dialog could trap stale content — its source and alt text are cleared on close.
93. [x] Clicking the backdrop did nothing — backdrop click closes the dialog.
94. [x] Skip navigation was absent visually — a focus-revealed skip link targets main content.
95. [x] Selection color ignored the theme — selection uses the RELYR accent and dark ink.

## Reliability and finish

96. [x] Wide product motion could create horizontal overflow — root clipping and eight-locale responsive checks keep it contained.
97. [x] The latest release API was a single point of failure — stable HTML download links remain as fallback.
98. [x] Dynamic release data could change layout — version fields have compact fixed styling.
99. [x] JavaScript syntax and whitespace errors could ship — both scripts and the diff pass static checks.
100. [x] The site lacked a documented completion bar — this audit is retained beside the implementation.
