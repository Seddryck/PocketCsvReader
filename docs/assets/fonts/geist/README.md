# Geist web fonts

Geist Sans and Geist Mono by Vercel and the Geist Project Authors are distributed
under the [SIL Open Font License 1.1](OFL.txt). The original copyright notice and
license are included alongside the fonts.

These are the unmodified variable WOFF2 files from
[vercel/geist-font](https://github.com/vercel/geist-font/tree/a2357c1398c81d88ff84823a2f50af3152ee8733/fonts),
revision `a2357c1398c81d88ff84823a2f50af3152ee8733`:

- `fonts/Geist/webfonts/Geist[wght].woff2`
- `fonts/Geist/webfonts/Geist-Italic[wght].woff2`
- `fonts/GeistMono/webfonts/GeistMono[wght].woff2`
- `fonts/GeistMono/webfonts/GeistMono-Italic[wght].woff2`

Each file covers weights 100–900. Normal and italic faces are provided for both
families. The browser requests only the faces needed by the page and caches them
according to the hosting server's HTTP cache headers. No font CDN or local font
installation is required.

Font faces are declared in `docs/assets/css/main.scss`, where Jekyll processes
Liquid URLs before compiling Sass. Keep the `relative_url` filter so deployment
under `/PocketCsvReader/` and a root-path local preview both work. The shared theme
variables select Sans for text/UI and Mono for inline code and preformatted blocks.

To verify changes, build with `bundle exec jekyll build` from `docs`, then serve the
output under the configured base path. In a browser with its cache disabled, check
the home page and a documentation page with code at desktop and mobile widths.
Confirm the rendered fonts, successful first-party WOFF2 requests, visible fallback
text while requests are delayed or blocked, and cache reuse after re-enabling the
cache and revisiting a page. Also build with `--baseurl ''` for root-path previews.
