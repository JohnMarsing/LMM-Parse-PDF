# LMM Parse PDF

Parses Living Messiah Shabbat service agenda PDFs and saves the teaching block as Markdown.

| | |
|---|---|
| **Source** | Local PDF or Azure `shabbat-service` (`YYYY-MM-DD-Citation.pdf`) |
| **Destination** | Local `.md` or private Azure `shabbat-service-md` |
| **Stack** | .NET 8, Core library + Console CLI |

## Status

| Piece | Status |
|-------|--------|
| Models / options | Done |
| PdfPig line extract | Done (`PdfPig` **0.1.15**) |
| Anchors + intro skip | Done |
| Markdown builder | Done |
| CLI local mode | Done |
| **Azure blob I/O** | **Done** (`--blob`, temp download, MD upload) |
| **Teaching PDF slice** | **Done** (`*-teaching.pdf` next to `.md` / in `shabbat-service`) |
| Optional Azure Function | Not yet (PR 7) |
| Markdown from teaching PDF | Not yet (PR 8b) |

See [docs/design-lmm-parse-pdf.md](docs/design-lmm-parse-pdf.md) for the full design.

## Build & test

```powershell
dotnet build LMM-Parse-PDF.sln
dotnet test LMM-Parse-PDF.sln
```

## Configure Azure (one-time)

### 1. Create private destination container

```bash
az storage container create \
  --name shabbat-service-md \
  --account-name livingmessiahstorage \
  --auth-mode login \
  --public-access off
```

### 2. Store the connection string (do not commit secrets)

```powershell
cd C:\Source\repos\LMM-Parse-PDF

dotnet user-secrets set "Blob:ConnectionString" "<your-storage-connection-string>" `
  --project src/LivingMessiah.ShabbatPdf.Cli
```

Or set environment variable: `Blob__ConnectionString`

`appsettings.json` holds non-secret defaults (container names). Connection string stays empty there on purpose.

## Run the CLI

### Local PDF → local Markdown

```powershell
dotnet run --project src/LivingMessiah.ShabbatPdf.Cli -- `
  --input "C:\Users\JohnM\Downloads\2026-07-04-Lev-16.pdf" `
  --output ".\out\2026-07-04-Lev-16.md"
```

### Azure blob → Azure Markdown

```powershell
dotnet run --project src/LivingMessiah.ShabbatPdf.Cli -- `
  --blob "2026-07-04-Lev-16.pdf"
```

### Batch all agendas in the source container

For a one-time backfill (~100 PDFs). Requires **Azure CLI** (`az login`) and the same `Blob:ConnectionString` as a single run. Skips `*-teaching.pdf` and uses `--skip-existing` by default so you can re-run after failures.

```powershell
# Preview list only
.\scripts\batch-blob-parse.ps1 -WhatIf

# First 5 (smoke)
.\scripts\batch-blob-parse.ps1 -MaxCount 5

# Full container
.\scripts\batch-blob-parse.ps1

# Create destination container if missing (once)
.\scripts\batch-blob-parse.ps1 -EnsureContainer
```

Logs go under `out\batch-blob-parse-*.log`. See the script header for more parameters.

Downloads the PDF to a **temp file** (handles large agendas), extracts, then:

1. Uploads a **teaching-only PDF** (content page range) to the **source** container:  
   `https://livingmessiahstorage.blob.core.windows.net/shabbat-service/2026-07-04-Lev-16-teaching.pdf`
2. Uploads Markdown to the **destination** container:  
   `https://livingmessiahstorage.blob.core.windows.net/shabbat-service-md/2026-07-04-Lev-16.md`  
   with content-type `text/markdown; charset=utf-8`.

Local mode also writes `*-teaching.pdf` in the same folder as the `.md`.

### Flags

| Flag | Meaning |
|------|---------|
| `--input` / `-i` | Local PDF path |
| `--output` / `-o` | Local Markdown path (local mode) |
| `--blob` / `-b` | Source blob name in `shabbat-service` |
| `--dry-run` | Parse only; no write/upload |
| `--skip-existing` | Skip if destination already exists |
| `--ensure-container` | Create `shabbat-service-md` if missing |
| `--allow-nonstandard-name` | Allow non `YYYY-MM-DD-…` names in blob mode |

Exactly one of `--input` or `--blob` is required.

### Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Validation / anchors / invalid name |
| 2 | I/O / Azure / missing container |
| 3 | Unexpected |

### Visual Studio

1. Set **LivingMessiah.ShabbatPdf.Cli** as startup project  
2. Debug args examples:

```text
--blob 2026-07-04-Lev-16.pdf
```

```text
--input "C:\Users\JohnM\Downloads\agenda.pdf" --output "C:\Temp\out.md"
```

3. User Secrets (same as CLI): right-click project → **Manage User Secrets**, or the `dotnet user-secrets` command above.

## Operator checklist (first Azure success)

1. Create **private** `shabbat-service-md`  
2. Set `Blob:ConnectionString` (read source + write destination)  
3. Run `--blob 2026-07-04-Lev-16.pdf` (or your weekly file)  
4. Confirm MD blob exists, content-type, and page range in front matter  

## Extract rules

1. **Start** after full lines `Welcome` + `Bienvenido` / `Bienvenidos`  
2. **Skip** intro pages (Fair Use / agenda title patterns)  
3. **End** before `The Avinu Prayer`  
4. **Text layer only** — no OCR, no images in v1  

## License / content

Agenda PDFs and extracted Scripture text are used for congregational study. Destination Markdown is intended to stay **private** until policy review.
