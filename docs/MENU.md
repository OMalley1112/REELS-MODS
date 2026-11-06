# Menu spec

Panel is built the same way RIFTIX is built: world-space canvas parented to
the active hand, opened with the face button (X/Y left, A/B right).

## Geometry

```
panel   : 0.10 x 1.00 x 1.10   (thin, tall, slightly deep)
anchor  : palm, 0.15m out, tilted 20deg toward the face
accent  : #C79BFF
theme   : index 5 (violet)
rows    : 6 per page
```

## Title

`R E E L S   M O D S` in gradient text, letter-spaced, 1500 dynamic pixels.
Gradient runs accent -> white -> accent across the width.

## Tabs

Three tabs across the top, active tab underlined with the accent bar:

```
[ MAIN ] [ REELS ] [ PAGES ]
```

| Tab | Contents |
|---|---|
| MAIN | menu settings: theme, hand, page size, notifications, close |
| REELS | the mods: Give Coins, Give Bundles, Unlock Every Rod, Instant Reel, Auto Catch, Monster Lure |
| PAGES | page nav + extra rows (future mods) |

Tab switch re-renders the row list in place. No panel rebuild, no canvas
recreate — that is what made the old font texture blow up.

## Row

```
+------------------------------------------+
| Give Coins                    [   ON   ]|
+------------------------------------------+
```

- left: label, best-fit, hard-bounded 14..40 so it can never demand a huge atlas
- right: pill toggle, accent fill when ON, grey when OFF
- click: 1.06 pop scale over 60ms, ease out, snap back
- notification fires on change: `Give Coins: ON`

## Notification

Bottom of the panel, slides up 12px, holds 1.6s, fades. Uses a single shared
Text object and a single shared sprite atlas — one extra texture, not one per
notification.

## Rules learned from RIFTIX

1. Never let `resizeTextForBestFit` run unbounded. Every Text gets
   `resizeTextMinSize`/`resizeTextMaxSize` set explicitly.
2. One `Font` instance shared by every label on the menu. Per-label font
   instances force per-label texture churn.
3. No `Shadow`/`Outline` on the notification text. Two extra full-screen
   quads per frame, and it was the cheapest thing to cut.
4. Rows are pooled and re-texted, not destroyed and rebuilt on tab change.
