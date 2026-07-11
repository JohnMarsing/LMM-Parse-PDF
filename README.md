# LMM Parse PDF

Parses Living Messiah Shabbat service agenda PDFs and saves the teaching block as Markdown.

| | |
|---|---|
| **Source** | Local PDF or (later) `shabbat-service` blob (`YYYY-MM-DD-Citation.pdf`) |
| **Destination** | Local `.md` file or (later) private `shabbat-service-md` |
| **Stack** | .NET 8, Core library + Console CLI |

## Status

| Piece | Status |
|-------|--------|
| Models / options | Done |
| PdfPig line extract | Done (`PdfPig` **0.1.15**) |
| Anchors + intro skip | Done |
| Markdown builder | Done |
| **CLI local mode** | **Done** (`--input` / `--output`) |
| Azure blob I/O | Not yet (PR 5) |

See [docs/design-lmm-parse-pdf.md](docs/design-lmm-parse-pdf.md) for the full design.

## Solution layout

```text
LMM-Parse-PDF.sln
src/LivingMessiah.ShabbatPdf.Core/   # extract, anchors, markdown, pipeline
src/LivingMessiah.ShabbatPdf.Cli/    # console host
tests/LivingMessiah.ShabbatPdf.Tests/
docs/design-lmm-parse-pdf.md
```

## Build & test

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or a newer SDK that can target `net8.0`).

```powershell
dotnet build LMM-Parse-PDF.sln
dotnet test LMM-Parse-PDF.sln
```

## Run the CLI (local PDF → Markdown)

```powershell
cd C:\Source\repos\LMM-Parse-PDF

dotnet run --project src/LivingMessiah.ShabbatPdf.Cli -- `
  --input "C:\Users\JohnM\Downloads\2026-07-04-Lev-16.pdf" `
  --output ".\out\2026-07-04-Lev-16.md"
```

### Flags

| Flag | Meaning |
|------|---------|
| `--input` / `-i` | **Required.** Path to PDF |
| `--output` / `-o` | Markdown path (default: same folder as PDF, `.md` name) |
| `--dry-run` | Parse only; do not write the file |
| `--skip-existing` | If output exists, exit 0 without reprocessing |

### Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Validation / anchors not found |
| 2 | File I/O |
| 3 | Unexpected |

### Visual Studio

1. Set **LivingMessiah.ShabbatPdf.Cli** as the startup project  
2. **Project → Properties → Debug → Command line arguments**, for example:

```text
--input "C:\Users\JohnM\Downloads\your-agenda.pdf" --output "C:\Temp\out.md"
```

3. Press **F5** or **Ctrl+F5**

## Extract rules

1. **Start** after a page with full lines `Welcome` and `Bienvenido` / `Bienvenidos`
2. **Skip** intro pages (Fair Use / agenda title patterns)
3. **End** before the page titled `The Avinu Prayer`
4. **Text layer only** — no OCR, no images in v1

## License / content

Agenda PDFs and extracted Scripture text are used for congregational study. Destination Markdown is intended to stay private until policy review.
