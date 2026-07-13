# LMM Parse PDF

Parses Living Messiah Shabbat service agenda PDFs and saves the teaching block as Markdown.

| | |
|---|---|
| **Source** | Local PDF or Azure `shabbat-service` (`YYYY-MM-DD-Citation.pdf`) |
| **Destination** | Local `.md` or private Azure `shabbat-service-md` |
| **Stack** | .NET 8, Core + Console CLI + optional Azure Function |

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
| **Azure Function blob trigger** | **Done** (optional; Flex/Premium recommended for large PDFs) |
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

### Batch teaching PDFs for all agendas

One-time (or rare) backfill of `*-teaching.pdf` only — **no Markdown**. Uses the same `Blob:ConnectionString` as a single CLI run (user secrets or `Blob__ConnectionString`) to list and process blobs. Skips existing `*-teaching.pdf` inputs and uses `--teaching-only --skip-existing` so you can re-run after failures.

```powershell
# Preview list only
.\scripts\batch-blob-parse.ps1 -WhatIf

# First 5 (smoke)
.\scripts\batch-blob-parse.ps1 -MaxCount 5

# Full container → uploads *-teaching.pdf to shabbat-service only
.\scripts\batch-blob-parse.ps1
```

Single-blob equivalent:

```powershell
dotnet run --project src/LivingMessiah.ShabbatPdf.Cli -- `
  --blob "2026-07-04-Lev-16.pdf" --teaching-only
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
| `--teaching-only` | Export `*-teaching.pdf` only; do not build or write Markdown |

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

## Azure Function (optional)

Thin isolated worker that runs when a full agenda PDF is uploaded to `shabbat-service`.

| | |
|---|---|
| Project | `src/LivingMessiah.ShabbatPdf.Functions` |
| Trigger | Blob create/update on `%Blob__SourceContainer%/{name}` |
| Skips | Non-PDF and `*-teaching.pdf` (avoids re-entry when teaching is written back) |
| Work | Copy trigger stream → temp file (no re-download) → same `ParsePipeline` as CLI |
| Outputs | `*-teaching.pdf` in source container + `*.md` in `shabbat-service-md` |

### Local settings

```powershell
copy src\LivingMessiah.ShabbatPdf.Functions\local.settings.json.example `
     src\LivingMessiah.ShabbatPdf.Functions\local.settings.json
# Edit local.settings.json: set Blob and Blob__ConnectionString to your storage connection string
```

`local.settings.json` is gitignored. See `local.settings.json.example`.

### Run locally (needs Azure Functions Core Tools + Azurite or a real storage connection)

```powershell
cd src\LivingMessiah.ShabbatPdf.Functions
func start
```

Or set the Functions project as startup in Visual Studio.

### Deployed app (current)

| | |
|---|---|
| **Name** | `lmm-shabbat-pdf` |
| **Resource group** | `LmmWebAppGroup` |
| **Plan** | Flex Consumption (West US) |
| **URL** | https://lmm-shabbat-pdf.azurewebsites.net |
| **Function** | `ProcessShabbatPdf` |
| **Storage** | `livingmessiahstorage` |

Redeploy after code changes:

```powershell
.\scripts\deploy-function.ps1
```

### Deploy notes

1. Prefer **Flex Consumption** or **Premium** (agendas can be tens of MB).  
2. App settings already configured on `lmm-shabbat-pdf` (connection string style for trigger + uploads):
   - `Blob` / `Blob__ConnectionString` → storage connection string  
   - `Blob__SourceContainer` = `shabbat-service`  
   - `Blob__DestinationContainer` = `shabbat-service-md`  
3. Later hardening: switch to Managed Identity (`Blob__UseDefaultAzureCredential=true` + RBAC) and remove keys from app settings.  
4. CLI remains fully supported for manual / batch runs.  
5. Smoke-test: upload a full agenda PDF to `shabbat-service` (not `*-teaching.pdf`), then confirm `*-teaching.pdf` and `.md` appear.

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
