using Google.Apis.Auth.OAuth2;
using Google.Cloud.Diagnostics.AspNetCore3;
using Google.Cloud.Storage.V1;

var builder = WebApplication.CreateBuilder(args);

// Use Dependency Injection to only create one storage client
string targetServiceAccount = "bucket-uploader-sa@ons-blaise-v2-dev-ben1.iam.gserviceaccount.com";
GoogleCredential defaultCredential = GoogleCredential.GetApplicationDefault();
var impersonatedCredential = defaultCredential.Impersonate(
    new ImpersonatedCredential.Initializer(targetServiceAccount)
    {
        Scopes = new[] { "https://www.googleapis.com/auth/devstorage.read_write" }
    }
);
var storageClient = await StorageClient.CreateAsync(impersonatedCredential);
builder.Services.AddSingleton(storageClient);

bool IsRunningOnGcpVm()
{
    try
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMilliseconds(300);
        client.DefaultRequestHeaders.Add("Metadata-Flavor", "Google");
        var response = client.GetAsync("http://metadata.google.internal").Result;
        return response.IsSuccessStatusCode;
    }
    catch
    {
        return false;
    }
}

var runningOnGcp = IsRunningOnGcpVm();

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
