using System.Collections.Concurrent;
using ResumePipeline = RezumiChanCLI.Program;

const int DefaultMaxAttempts = 1;
const int MaxMaxAttempts = 20;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 20 * 1024 * 1024;
});

var outputDirectory = Environment.GetEnvironmentVariable("REZUMICHAN_OUTPUT_DIR");
if (string.IsNullOrWhiteSpace(outputDirectory))
{
    outputDirectory = Path.Combine(AppContext.BaseDirectory, "GeneratedResumes");
}

Directory.CreateDirectory(outputDirectory);

var allowedOrigins = Environment.GetEnvironmentVariable("REZUMICHAN_ALLOWED_ORIGINS")?
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .SetIsOriginAllowed(origin => IsAllowedOrigin(origin, allowedOrigins))
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

var jobs = new ConcurrentDictionary<string, ResumeJob>();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    outputDirectory
}));

app.MapPost("/api/resumes", (CreateResumeRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.JobPostingText))
    {
        return Results.BadRequest(new { error = "jobPostingText is required." });
    }

    var job = EnqueueResumeJob(
        request.JobPostingText,
        jobs,
        outputDirectory,
        NormalizeMaxAttempts(request.MaxAttempts));
    return Results.Accepted($"/api/resumes/{job.Id}", ToStatus(job));
});

app.MapPost("/api/resumes/text", async (HttpRequest request, int? maxAttempts) =>
{
    using var reader = new StreamReader(request.Body);
    var jobPostingText = await reader.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(jobPostingText))
    {
        return Results.BadRequest(new { error = "Request body must contain a job description." });
    }

    var job = EnqueueResumeJob(
        jobPostingText,
        jobs,
        outputDirectory,
        NormalizeMaxAttempts(maxAttempts));
    return Results.Accepted($"/api/resumes/{job.Id}", ToStatus(job));
});

app.MapPost("/api/resumes/upload", async (IFormFile file, int? maxAttempts) =>
{
    if (file.Length == 0)
    {
        return Results.BadRequest(new { error = "Uploaded file is empty." });
    }

    using var reader = new StreamReader(file.OpenReadStream());
    var jobPostingText = await reader.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(jobPostingText))
    {
        return Results.BadRequest(new { error = "Uploaded file must contain a job description." });
    }

    var job = EnqueueResumeJob(
        jobPostingText,
        jobs,
        outputDirectory,
        NormalizeMaxAttempts(maxAttempts));
    return Results.Accepted($"/api/resumes/{job.Id}", ToStatus(job));
});

app.MapGet("/api/resumes/{id}", (string id) =>
{
    return jobs.TryGetValue(id, out var job)
        ? Results.Ok(ToStatus(job))
        : Results.NotFound(new { error = "Resume job was not found." });
});

app.MapGet("/api/resumes/{id}/pdf", (string id) =>
{
    if (!jobs.TryGetValue(id, out var job))
    {
        return Results.NotFound(new { error = "Resume job was not found." });
    }

    if (job.Status != ResumeJobStatus.Completed)
    {
        return Results.Conflict(ToStatus(job));
    }

    if (string.IsNullOrWhiteSpace(job.FilePath) || !File.Exists(job.FilePath))
    {
        return Results.Problem("The resume job completed, but the PDF file is missing.", statusCode: StatusCodes.Status410Gone);
    }

    return Results.File(job.FilePath, "application/pdf", job.FileName);
});

app.Run();

static ResumeJob EnqueueResumeJob(
    string jobPostingText,
    ConcurrentDictionary<string, ResumeJob> jobs,
    string outputDirectory,
    int maxAttempts)
{
    var job = new ResumeJob
    {
        Id = Guid.NewGuid().ToString("N"),
        Status = ResumeJobStatus.Queued,
        Message = "Queued.",
        Percent = 0,
        MaxAttempts = maxAttempts,
        CreatedAt = DateTimeOffset.UtcNow
    };

    jobs[job.Id] = job;

    _ = Task.Run(async () =>
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            job.Attempt = attempt;

            try
            {
                job.Status = ResumeJobStatus.Processing;
                job.Message = $"Attempt {attempt} of {maxAttempts}: starting.";
                job.Percent = 0;
                job.Error = null;

                var progress = new Progress<ResumePipeline.PipelineProgress>(update =>
                {
                    job.Message = $"Attempt {attempt} of {maxAttempts}: {update.Message}";
                    job.Percent = update.Percent;
                });

                var filePath = await ResumePipeline.RunResumePipeline(
                    jobPostingText,
                    progress,
                    openPdf: false,
                    outputDirectory: outputDirectory);

                job.FilePath = filePath;
                job.FileName = Path.GetFileName(filePath);
                job.Status = ResumeJobStatus.Completed;
                job.Message = "Done.";
                job.Percent = 100;
                job.CompletedAt = DateTimeOffset.UtcNow;
                return;
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;

                if (attempt >= maxAttempts)
                {
                    job.Status = ResumeJobStatus.Failed;
                    job.Message = $"Failed after {maxAttempts} attempt(s).";
                    job.CompletedAt = DateTimeOffset.UtcNow;
                    return;
                }

                job.Message = $"Attempt {attempt} failed. Retrying...";
                await Task.Delay(1500);
            }
        }
    });

    return job;
}

static object ToStatus(ResumeJob job)
{
    return new
    {
        job.Id,
        status = job.Status.ToString().ToLowerInvariant(),
        job.Message,
        job.Percent,
        job.Attempt,
        job.MaxAttempts,
        job.CreatedAt,
        job.CompletedAt,
        job.FileName,
        job.Error,
        statusUrl = $"/api/resumes/{job.Id}",
        downloadUrl = job.Status == ResumeJobStatus.Completed ? $"/api/resumes/{job.Id}/pdf" : null
    };
}

static int NormalizeMaxAttempts(int? maxAttempts)
{
    return Math.Clamp(maxAttempts ?? DefaultMaxAttempts, DefaultMaxAttempts, MaxMaxAttempts);
}

static bool IsAllowedOrigin(string origin, HashSet<string> allowedOrigins)
{
    if (allowedOrigins.Contains(origin))
    {
        return true;
    }

    return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
           && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
               || uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase));
}

sealed class ResumeJob
{
    public required string Id { get; init; }
    public required ResumeJobStatus Status { get; set; }
    public required string Message { get; set; }
    public required int Percent { get; set; }
    public required int MaxAttempts { get; init; }
    public int Attempt { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FilePath { get; set; }
    public string? FileName { get; set; }
    public string? Error { get; set; }
}

enum ResumeJobStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}

sealed record CreateResumeRequest(string JobPostingText, int? MaxAttempts = null);
