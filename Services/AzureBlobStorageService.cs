// Services/AzureBlobStorageService.cs
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace WebCodeWork.Services
{
    public interface IFileStorageService
    {
        // Saves the file and returns (stored unique filename, relative storage path)
        Task<(string StoredFileName, string RelativePath)> SaveSubmissionFileAsync(int submissionId, IFormFile file);

        // Deletes the file from storage
        Task<bool> DeleteSubmissionFileAsync(string relativePath, string storedFileName);

        // Gets a stream for downloading, content type, and preferred download name
        Task<(Stream? FileStream, string? ContentType, string DownloadName)> GetSubmissionFileAsync(string relativePath, string storedFileName, string originalFileName);
    }
}


namespace WebCodeWork.Services
{
    public class AzureBlobStorageService : IFileStorageService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly string _containerName;
        private readonly ILogger<AzureBlobStorageService> _logger;

        public AzureBlobStorageService(IConfiguration configuration, ILogger<AzureBlobStorageService> logger)
        {
            // Use connection string from config (handles Azurite or real Azure)
            var connectionString = configuration.GetValue<string>("AzureStorage:ConnectionString");
            _containerName = configuration.GetValue<string>("AzureStorage:ContainerName") ?? "submissions"; // Default container name
            _logger = logger;

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Azure Storage connection string 'AzureStorage:ConnectionString' not configured.");
            }

            _blobServiceClient = new BlobServiceClient(connectionString);

            // Ensure container exists (optional: could require manual creation)
             EnsureContainerExistsAsync().ConfigureAwait(false).GetAwaiter().GetResult(); // Run synchronously in constructor (or use async factory)
        }

        private async Task EnsureContainerExistsAsync()
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None); // Private container
            _logger.LogInformation("Blob container '{ContainerName}' ensured.", _containerName);
        }

        public async Task<(string StoredFileName, string RelativePath)> SaveSubmissionFileAsync(int submissionId, IFormFile file)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            // Use a path structure within the container, e.g., submissions/{submissionId}/{guid}.ext
            var extension = Path.GetExtension(file.FileName);
            var uniqueBlobName = $"{Guid.NewGuid()}{extension}";
            var blobPath = $"submissions/{submissionId}/{uniqueBlobName}"; // Relative path within container
            var blobClient = containerClient.GetBlobClient(blobPath);

            _logger.LogInformation("Uploading file {FileName} to blob {BlobPath} for submission {SubmissionId}...", file.FileName, blobPath, submissionId);

            try
            {
                using (var stream = file.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = file.ContentType });
                }
                _logger.LogInformation("Successfully uploaded file {FileName} to blob {BlobPath}", file.FileName, blobPath);
                // For Azure Blob, the "StoredFileName" is the full blobPath within the container.
                // The "RelativePath" concept from local storage isn't directly applicable in the same way.
                // We'll return the uniqueBlobName and the directory structure as the relative path.
                return (uniqueBlobName, $"submissions/{submissionId}"); // Return just the filename and its 'folder'
                // OR return (blobPath, _containerName); // Alternative: return full path and container
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Error uploading file {FileName} to blob {BlobPath}", file.FileName, blobPath);
                throw; // Re-throw to indicate failure
            }
        }

         public async Task<bool> DeleteSubmissionFileAsync(string relativePath, string storedFileName) // relativePath here is the directory structure, storedFileName is the unique blob name
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobPath = Path.Combine(relativePath, storedFileName).Replace("\\", "/"); // Construct full blob path
            var blobClient = containerClient.GetBlobClient(blobPath);

             _logger.LogInformation("Attempting to delete blob {BlobPath}...", blobPath);
            try
            {
                var response = await blobClient.DeleteIfExistsAsync();
                if (response.Value) // Check if deletion actually happened
                {
                    _logger.LogInformation("Successfully deleted blob {BlobPath}", blobPath);
                    return true;
                }
                else
                {
                     _logger.LogWarning("Blob {BlobPath} did not exist or already deleted.", blobPath);
                     return false; // Indicate file didn't exist
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting blob {BlobPath}", blobPath);
                return false; // Indicate failure
            }
        }

          public async Task<(Stream? FileStream, string? ContentType, string DownloadName)> GetSubmissionFileAsync(string relativePath, string storedFileName, string originalFileName)
         {
             var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
             var blobPath = Path.Combine(relativePath, storedFileName).Replace("\\", "/");
             var blobClient = containerClient.GetBlobClient(blobPath);

             _logger.LogInformation("Attempting to download blob {BlobPath}...", blobPath);

             try
             {
                 if (!await blobClient.ExistsAsync())
                 {
                      _logger.LogWarning("Blob {BlobPath} not found for download.", blobPath);
                     return (null, null, originalFileName);
                 }

                 // Get properties to fetch content type
                 BlobProperties properties = await blobClient.GetPropertiesAsync();
                 // Get stream
                 var response = await blobClient.DownloadStreamingAsync(); // Use DownloadStreamingAsync for efficiency

                 return (response.Value.Content, properties.ContentType, originalFileName);
             }
              catch (Exception ex)
             {
                  _logger.LogError(ex, "Error downloading blob {BlobPath}", blobPath);
                  return (null, null, originalFileName); // Indicate failure
             }
         }
    }
}