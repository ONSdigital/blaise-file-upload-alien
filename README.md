# Blaise File Upload 👽

## Overview

Blaise File Upload Alien is a minimal ASP.NET Core Web API (using .NET 10) for uploading files. It is designed as a **proof of concept** to demonstrate file upload handling for Blaise questionnaires. The API works both locally (as a console app) and as a Windows service on a Google Cloud Platform (GCP) VM. It exposes a single endpoint for file uploads and stores files on disk with metadata and unique IDs. Logging is automatically configured for both local development and GCP environments.

> **Note:** Files are currently stored on the local disk, but in a production scenario, files could be stored in a cloud storage bucket (e.g., Google Cloud Storage).

> **Test Questionnaire:** A `test-questionnaire` folder is included, containing a sample questionnaire that will upload a file to the endpoint of this application/service.

## Features

- **File Upload API**: Accepts file uploads via HTTP POST to `/api/file`.
- **Metadata Storage**: Stores files with metadata and a unique identifier.
- **Environment Detection**: Automatically detects if running locally or on a GCP VM.
- **Logging**:
    - Logs to the console when running locally.
    - Logs to both the console and Google Cloud Logging (Stackdriver) when running on a GCP VM.
- **Swagger UI**: API documentation and testing via Swagger at `/swagger`.


## Requirements

- **.NET 10 SDK** is required to build and publish the application. [Download .NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- **.NET Hosting Bundle** is required on the target machine to run the published app as a Windows service. [Download Hosting Bundle](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

## Running Locally

1. Restore and publish the application (requires .NET 10 SDK):
    ```
    dotnet restore
    dotnet publish -c Release -r win-x64 --self-contained false -o \BlaiseServices\BlaiseFileUploadAlien\
    ```

2. You will need to add your personal account to be able to impersonate the bucket-uploader-sa@ons-blaise-v2-dev-<sandbox>.iam.gserviceaccount.com, I did this in terraform for a sandbox like below

    ```
    resource "google_service_account_iam_member" "developer_impersonation_binding" {
        service_account_id = google_service_account.uploader_sa.name
        role               = "roles/iam.serviceAccountTokenCreator"
        member             = "user:<user_email>"
    }
    ```

3. Authenticate your local machine

    ```
    gcloud config set project ons-blaise-v2-dev-<sandbox>
    gcloud auth application-default login
    ```

4. Run the application as a console app (requires .NET 10 Hosting Bundle installed):
    ```
    cd \BlaiseServices\BlaiseFileUploadAlien\
    .\BlaiseFileUploadAlien.exe
    ```

5. Access the API at [http://localhost:5123/swagger](http://localhost:5123/swagger) for documentation and testing.


## Running on a GCP VM (as a Windows Service)

1. Publish and copy the output to your GCP VM (requires .NET 10 SDK for publishing, .NET 10 Hosting Bundle for running).

2. Create and start the Windows service:
    ```
    sc.exe create "BlaiseFileUploadAlien" binpath= "C:\BlaiseServices\BlaiseFileUploadAlien\BlaiseFileUploadAlien.exe"
    sc.exe config "BlaiseFileUploadAlien" start= auto
    sc.exe start "BlaiseFileUploadAlien"
    ```

3. The service will automatically detect it is running on GCP and log to Google Cloud Logging (Stackdriver) as well as the console.


## API Usage

- **POST** `/api/file`
    - Accepts a JSON body with:
        - `Id` (int): Case or file identifier
        - `FileMeta` (string): Metadata for the file
        - `File` (int[]): File contents as an array of bytes
    - Returns: The filename used to store the file (including a short unique ID and extension)

## Environment Detection & Logging

- The application checks for the GCP metadata server to determine if it is running on a GCP VM.
- If running on GCP, it uses the `GOOGLE_CLOUD_PROJECT` environment variable for the project ID (required for logging to Stackdriver).
- Locally, only console logging is enabled.

## Notes

- Uploaded files are stored in `C:\BlaiseFileUploads` by default.
- Ensure the service account on GCP has permission to write logs to Google Cloud Logging.
