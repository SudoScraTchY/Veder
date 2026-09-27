# Veder UI — accessibility measurements

Contrast measured with the WCAG 2.1 relative-luminance formula against the actual token values in
`app.tailwind.css`. AA thresholds: **4.5:1** for normal text, **3:1** for large text (≥24px or ≥19px bold).

## Measured results

| Foreground | Background | Ratio | AA normal | Verdict |
|---|---|---|---|---|
| `ink-900` `#241f1a` | `parchment-50` `#fbf9f4` | **15.52** | 4.5 | pass |
| `ink-700` `#453f38` | `parchment-50` | **9.87** | 4.5 | pass |
| `ink-500` `#6b6257` | `parchment-50` | **5.69** | 4.5 | pass |
| `ochre-700` `#9a6224` | `parchment-50` | **4.81** | 4.5 | pass |
| `sage-700` `#55643f` | `parchment-50` | **6.08** | 4.5 | pass |
| `terracotta-700` `#8a4529` | `parchment-50` | **6.73** | 4.5 | pass |
| `ink-900` | `parchment-100` `#f5f1e6` | **14.47** | 4.5 | pass |
| `ink-700` | `parchment-100` | **9.21** | 4.5 | pass |
| `ink-500` | `parchment-100` | **5.30** | 4.5 | pass |
| `sage-700` | `parchment-100` | **5.67** | 4.5 | pass |
| `terracotta-700` | `parchment-100` | **6.28** | 4.5 | pass |
| `ochre-700` | `parchment-100` | **4.49** | 4.5 | **fail (marginal)** |
| `ochre-500` `#c8873c` as text | `parchment-50` | **2.86** | 4.5 | **fail** |
| `sage-500` `#7d9166` as text | `parchment-50` | **3.26** | 4.5 | **fail** |
| `terracotta-500` `#b5613f` as text | `parchment-50` | **4.20** | 4.5 | **fail** |
| `parchment-50` on `ochre-500` fill (primary button) | — | **2.86** | 4.5 | **fail — fixed, see below** |

## What the numbers changed

**The primary button was the real defect.** `bg-ochre-500` with `text-parchment-50` measured **2.86:1** —
well under AA for a 3–4 word label. The 500 shades are tuned to read as *fills and borders*, not as text
against parchment. Fixed by moving the button to `bg-ochre-700` (4.81:1, passes) and giving hover a
`brightness-90` filter, which darkens rather than lightens — the previous `hover:bg-ochre-700` on an
`ochre-500` fill would have *lowered* contrast on hover, which is backwards.

**`ochre-700` on a panel misses AA by 0.01** (4.49 vs 4.5). That is a real miss, not a rounding curiosity,
so the rule below is to use `ink-700` for body text on panels and reserve `ochre-700` for text on the
parchment-50 ground and for focus rings.

## The rule this produces

1. **Text** uses `ink-900` / `ink-700` / `ink-500`, or an accent's **700** shade. Never a 500 shade.
2. **500 shades are fills, borders and chips outlines only** — they are large-area or non-text carriers, so
   the 3:1 non-text threshold applies rather than 4.5:1. The provenance chip follows this: its border uses
   the 500 shade, its label text uses the matching 700 shade.
3. **On `parchment-100` panels**, body text is `ink-700` (9.21) rather than `ochre-700` (4.49).
4. Focus rings use `outline-ochre-700` on parchment grounds (4.81 against the ground, and it is a non-text
   indicator so 3:1 applies with margin).

## Still unmeasured

Focus-ring visibility *on top of* the 500-shade fills, and any state that composites two translucent
colours. Those need a rendered pixel sample rather than a token calculation, so they belong with the
browser verification pass — which remains unrun.
