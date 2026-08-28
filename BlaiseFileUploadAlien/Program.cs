using BlaiseFileUploadAlien.Configuration;
using BlaiseFileUploadAlien.Services;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Diagnostics.AspNetCore3;
using Google.Cloud.Storage.V1;

var builder = WebApplication.CreateBuilder(args);

// Use Dependency Injection to only create one storage client
builder.Services.AddSingleton<StorageClient>(provider =>
{
    GoogleCredential defaultCredential = GoogleCredential.GetApplicationDefault();
    return StorageClient.Create(defaultCredential);
});

builder.Services.Configure<UploadSettings>(options =>
{
    builder.Configuration.GetSection("UploadSettings").Bind(options);
    var envBucket = Environment.GetEnvironmentVariable("ENV_BLAISE_RAT_BUCKET");
    if (!string.IsNullOrEmpty(envBucket))
    {
        options.BucketName = envBucket;
    }
});

builder.Services.AddScoped<IFileDeletionService, GcpFileDeletionService>();

async Task<bool> IsRunningOnGcpVm()
{
    try
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMilliseconds(300);
        client.DefaultRequestHeaders.Add("Metadata-Flavor", "Google");
        var response = await client.GetAsync("http://metadata.google.internal");
        return response.IsSuccessStatusCode;
    }
    catch
    {
        return false;
    }
}

var runningOnGcp = await IsRunningOnGcpVm();

if (runningOnGcp)
{
    builder.Host.UseWindowsService(options =>
    {
        options.ServiceName = "BlaiseFileUploadAlien";
    });
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

if (runningOnGcp)
{
    var gcpProjectId = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT");
    builder.Services.AddGoogleDiagnosticsForAspNetCore(
        projectId: gcpProjectId,
        serviceName: "BlaiseFileUploadAlien"
    );
}

var port = "5123";
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 60 * 1024 * 1024; // 60 MB Max Limit
});


builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthorization();
app.MapControllers();

app.Run();
