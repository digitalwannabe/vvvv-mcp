# Third-Party Notices

vvvv-mcp builds on the following third-party components and content.
All are used in compliance with their respective licenses.

## Runtime / library dependencies (all MIT or similarly permissive)

| Component | License | Notes |
|---|---|---|
| ModelContextProtocol SDK (Microsoft/Anthropic) | MIT | NuGet, not modified |
| Microsoft.Extensions.* | MIT | NuGet |
| Microsoft.Data.Sqlite / SQLitePCLRaw | MIT / Apache-2.0 | NuGet |
| WebSocketSharp | MIT | NuGet (HDE SSE server) |
| uv (Astral) | Apache-2.0 / MIT | invoked at runtime, not bundled |
| Open WebUI | BSD-3-Clause (+ branding terms) | pulled at runtime by uv, not bundled |

## vvvv / VL platform

- **VL.Core, VL.HDE, VL.CEF, VL.CoreLib, VL.Stride** and all VL.* packages are
  products of the **vvv group** (https://vvvv.org). They are referenced as NuGet
  dependencies and are **not bundled** with this software. vvvv gamma itself
  requires a license from the vvvv group for commercial use — users of this
  software must comply with vvvv's own licensing terms.

## Content / knowledge files

- **vvvv-skills** by Tebjan Halm — https://github.com/tebjan/vvvv-skills —
  licensed **CC BY-SA 4.0** (https://creativecommons.org/licenses/by-sa/4.0/).
  Knowledge files derived from it (parts of `knowledge/vl-patterns.md` and
  related condensed files) are likewise shared under CC BY-SA 4.0 with
  attribution to the original author.

- **The Gray Book** (vvvv gamma documentation) — https://thegraybook.vvvv.org,
  source: https://github.com/vvvv/The-Gray-Book — official vvvv documentation by
  the vvvv group. The `knowledge/gray-book-*.md` files are condensed summaries
  with attribution. The upstream repository does not state an explicit license;
  summaries are provided as documentation reference. Image text extracted via
  OCR (`knowledge/gray-book-image-text.md`) derives from the same source.

- **Help patches / node catalogs**: the node catalog JSON and help-patch index
  are generated from publicly available VL packages (vvvv group and community
  authors, mostly MIT). The packages themselves are **not** redistributed;
  only factual data (node names, categories, pin signatures) is indexed.

## Trademarks

"vvvv" is a trademark of the vvvv group. This project is an independent
community tool and is not affiliated with or endorsed by the vvvv group.
