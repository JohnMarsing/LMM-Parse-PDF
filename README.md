# LMM Parse PDF

Parses Living Messiah Shabbat service agenda PDFs and saves the teaching block as Markdown in Azure Blob Storage.

| | |
|---|---|
| **Source** | `shabbat-service` container (`YYYY-MM-DD-Citation.pdf`) |
| **Destination** | private `shabbat-service-md` container (same name, `.md`) |
| **Stack** | .NET 8, C# class library + Console CLI (planned) |

## Status

**PR 1 complete:** solution skeleton and core models only. PDF extraction, Markdown, CLI, and Azure I/O come in later PRs.

See [docs/design-lmm-parse-pdf.md](docs/design-lmm-parse-pdf.md) for the full design.

## Solution layout

```text
LMM-Parse-PDF.sln
src/LivingMessiah.ShabbatPdf.Core/   # models, options, interfaces
tests/LivingMessiah.ShabbatPdf.Tests/
docs/design-lmm-parse-pdf.md
```

## Build & test

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or a newer SDK that can target `net8.0`).

```powershell
dotnet build LMM-Parse-PDF.sln
dotnet test LMM-Parse-PDF.sln
```

## Planned extract rules (not implemented yet)

1. **Start** after a page with full lines `Welcome` and `Bienvenido` / `Bienvenidos`
2. **Skip** intro pages (Fair Use / agenda title patterns)
3. **End** before the page titled `The Avinu Prayer`
4. **Text layer only** — no OCR, no images in v1

## License / content

Agenda PDFs and extracted Scripture text are used for congregational study. Destination Markdown is intended to stay in a **private** blob container until policy review.
