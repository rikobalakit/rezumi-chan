# RezumiChan Service

Run the local API:

```bash
dotnet run --project RezumiChanService/RezumiChanService.csproj --urls http://127.0.0.1:5078
```

On macOS, build the service path without the WinForms project:

```bash
dotnet build RezumiChanService.slnf
```

Set the OpenRouter key either with `OPENROUTER_API_KEY` or with the existing `config.json`.

## Endpoints

`GET /health`

Returns basic service health and the PDF output directory.

`POST /api/resumes`

JSON request:

```json
{
  "jobPostingText": "Paste the job description here",
  "maxAttempts": 1
}
```

`maxAttempts` is optional, clamped from `1` to `20`, and counts total attempts. `1` means no retry.

Returns `202 Accepted` with a job status URL and, once ready, a download URL.

`POST /api/resumes/text`

Raw text request body. Useful for scripts that already have a `.txt` payload.

```bash
curl -X POST "http://127.0.0.1:5078/api/resumes/text?maxAttempts=3" \
  --data-binary @job-description.txt
```

`POST /api/resumes/upload`

Multipart upload with a file field named `file`.

```bash
curl -X POST "http://127.0.0.1:5078/api/resumes/upload?maxAttempts=3" \
  -F "file=@job-description.txt"
```

`GET /api/resumes/{id}`

Poll this until `status` is `completed` or `failed`.

`GET /api/resumes/{id}/pdf`

Downloads the generated PDF after the job completes.

## Browser Access

Localhost origins are allowed by default for frontend development. To allow additional origins, set `REZUMICHAN_ALLOWED_ORIGINS` to a semicolon-separated list:

```bash
REZUMICHAN_ALLOWED_ORIGINS="https://example.com;https://app.example.com"
```

Set `REZUMICHAN_OUTPUT_DIR` to control where generated PDFs are stored.
