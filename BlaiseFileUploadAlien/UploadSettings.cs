namespace BlaiseFileUploadAlien;

public class UploadSettings
{
    public int MaxAttempts { get; set; } = 3;
    public int DelayBetweenRetriesMs { get; set; } = 5000;
    public string BucketName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = @"C:\BlaiseFileUploads";
}
