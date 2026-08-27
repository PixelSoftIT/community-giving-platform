# PixelSoft Branding Assets

## Primary logo (use this one)

- `pixelsoft-logo-master.png` — the official PixelSoft logo, transparent background.
  Derived from PixelSoft's own subsidiary artwork (PixelSoft Care Services):
  - "Care Services" tagline removed
  - The red medical-cross badge in the center of the heart icon replaced with a navy
    badge containing a `</>` (code brackets) mark — signals "software/tech services"
    for the parent brand, in place of the subsidiary's healthcare-specific cross
  - The heart icon's outer silhouette and the "PixelSoft" wordmark are otherwise
    untouched, original artwork
- `pixelsoft-logo-master.jpg` — same logo, flattened onto a white background, for tools
  that don't support transparency.
- `pixelsoft-icon-only.png` — just the heart icon, no wordmark, cropped from the same
  source. Used for compact placements (the site footer badge, favicons, etc.) where
  the full wordmark doesn't fit.

## Legacy alternates (not currently used)

- `pixelsoft-logo.svg` / `pixelsoft-logo.png` — an earlier hand-drawn "P" monogram
  design, kept in case it's ever preferred over the real logo crop above.

## Business collateral (`collateral/`)

- `pixelsoft-business-card.jpg` / `.svg` — Indika Pothupitiya's business card
  (3.5" × 2", 300dpi), using the master logo.
- `pixelsoft-letterhead.jpg` / `.svg` — company letterhead (A4, 300dpi), using the
  master logo. Company-level only (no individual name) so it's reusable for any staff
  correspondence.

## Where the logo is used in the app

`frontend/src/components/DevelopedByBadge.tsx` renders the site footer's
"Developed by PixelSoft" attribution using `pixelsoft-icon-only.png` (imported from
`frontend/src/assets/pixelsoft-icon.png`). Update the `href` in that file to your real
company website when ready — it currently points to a placeholder URL.
