
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace WebCodeWork.Services
{
    public interface IFileStorageService
    {
        
        Task<(string StoredFileName, string RelativePath)> SaveSubmissionFileAsync(int submissionId, IFormFile file);
        Task<bool> DeleteSubmissionFileAsync(string relativePath, string storedFileName);
        Task<(Stream? FileStream, string? ContentType, string DownloadName)> GetSubmissionFileAsync(string relativePath, string storedFileName, string originalFileName);
        Task<(string StoredFileName, string RelativePath)> CreateEmptyFileAsync(int submissionId, string desiredFileName);
        Task<bool> OverwriteSubmissionFileAsync(string relativePath, string storedFileName, string newContent);

        
        Task<(string StoredFileName, string RelativePath)> SaveTestCaseFileAsync(int assignmentId, string fileTypeDir, IFormFile file);
        Task<bool> DeleteTestCaseFileAsync(string relativePath, string storedFileName);
        Task<(Stream? FileStream, string? ContentType, string DownloadName)> GetTestCaseFileAsync(string relativePath, string storedFileName, string originalFileName);

        
        Task<(string StoredFileName, string RelativePath)> SaveClassroomPhotoAsync(int classroomId, IFormFile photoFile);
        Task<bool> DeleteClassroomPhotoAsync(string relativePath, string storedFileName);

        
        Task<(string StoredFileName, string RelativePath)> SaveUserProfilePhotoAsync(int userId, IFormFile photoFile);
        Task<bool> DeleteUserProfilePhotoAsync(string relativePath, string storedFileName);

        
        public string? GetPublicUserProfilePhotoUrl(string ProfilePhotoPath, string ProfilePhotoStoredName);
    }
}


namespace WebCodeWork.Services
{
    public class AzureBlobStorageService : IFileStorageService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly string _privateContainerName;
        private readonly string _publicClassroomPhotosContainerName;
        private readonly string _publicProfilePhotosContainerName;
        private readonly ILogger<AzureBlobStorageService> _logger;

        private readonly string _publicStorageBaseUrl;

        private string GetTestCaseBlobDir(int assignmentId, string fileTypeDir) => $"testcases/{assignmentId}/{fileTypeDir}"; 

        public AzureBlobStorageService(IConfiguration configuration, ILogger<AzureBlobStorageService> logger)
        {
            var connectionString = configuration.GetValue<string>("AzureStorage:ConnectionString");
            _privateContainerName = configuration.GetValue<string>("AzureStorage:PrivateContainerName") ?? "submissions";
            _publicClassroomPhotosContainerName = configuration.GetValue<string>("AzureStorage:PublicPhotosContainerName") ?? "classroom-photos";
            _publicProfilePhotosContainerName = configuration.GetValue<string>("AzureStorage:PublicProfilePhotosContainerName") ?? "user-profile-photos";
            _publicStorageBaseUrl = configuration.GetValue<string>("AzureStorage:PublicStorageBaseUrl")!;
            _logger = logger;

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Azure Storage connection string 'AzureStorage:ConnectionString' not configured.");
            }
            _blobServiceClient = new BlobServiceClient(connectionString);

            EnsureContainerExistsAsync(_privateContainerName, PublicAccessType.None)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            EnsureContainerExistsAsync(_publicClassroomPhotosContainerName, PublicAccessType.Blob)
                .ConfigureAwait(false).GetAwaiter().GetResult();
            EnsureContainerExistsAsync(_publicProfilePhotosContainerName, PublicAccessType.Blob)
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        private async Task EnsureContainerExistsAsync(string containerName, PublicAccessType accessType)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                var response = await containerClient.CreateIfNotExistsAsync(accessType);
                if (response != null && response.GetRawResponse().Status == 201) 
                {
                    _logger.LogInformation("Blob container '{ContainerName}' created with access type '{AccessType}'.", containerName, accessType);
                }
                else
                {
                    _logger.LogInformation("Blob container '{ContainerName}' already exists or no action taken. Ensure access type is '{AccessType}'.", containerName, accessType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure blob container '{ContainerName}' with access type '{AccessType}' exists.", containerName, accessType);
                throw; 
            }
        }

        public async Task<(string StoredFileName, string RelativePath)> SaveSubmissionFileAsync(int submissionId, IFormFile file)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_privateContainerName);
            var extension = Path.GetExtension(file.FileName);
            var uniqueBlobName = $"{Guid.NewGuid()}{extension}";
            var blobPath = $"submissions/{submissionId}/{uniqueBlobName}"; 
            var blobClient = containerClient.GetBlobClient(blobPath);

            _logger.LogInformation("Uploading file {FileName} to blob {BlobPath} for submission {SubmissionId}...", file.FileName, blobPath, submissionId);

            try
            {
                using (var stream = file.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = file.ContentType });
                }
                _logger.LogInformation("Successfully uploaded file {FileName} to blob {BlobPath}", file.FileName, blobPath);
                return (uniqueBlobName, $"submissions/{submissionId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file {FileName} to blob {BlobPath}", file.FileName, blobPath);
                throw;
            }
        }

        public async Task<bool> DeleteSubmissionFileAsync(string relativePath, string storedFileName) 
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_privateContainerName);
            var blobPath = Path.Combine(relativePath, storedFileName).Replace("\\", "/"); 
            var blobClient = containerClient.GetBlobClient(blobPath);

            _logger.LogInformation("Attempting to delete blob {BlobPath}...", blobPath);
            try
            {
                var response = await blobClient.DeleteIfExistsAsync();
                if (response.Value) 
                {
                    _logger.LogInformation("Successfully deleted blob {BlobPath}", blobPath);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Blob {BlobPath} did not exist or already deleted.", blobPath);
                    return false; 
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting blob {BlobPath}", blobPath);
                return false; 
            }
        }

        public async Task<(Stream? FileStream, string? ContentType, string DownloadName)> GetSubmissionFileAsync(string relativePath, string storedFileName, string originalFileName)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_privateContainerName);
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

                
                BlobProperties properties = await blobClient.GetPropertiesAsync();
                
                var response = await blobClient.DownloadStreamingAsync(); 

                return (response.Value.Content, properties.ContentType, originalFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading blob {BlobPath}", blobPath);
                return (null, null, originalFileName); 
            }
        }

        public async Task<(string StoredFileName, string RelativePath)> CreateEmptyFileAsync(int submissionId, string desiredFileName)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_privateContainerName);
            var extension = Path.GetExtension(desiredFileName);
            var uniqueBlobName = $"{Guid.NewGuid()}{extension}";
            var relativePath = $"submissions/{submissionId}"; 
            var blobPath = $"{relativePath}/{uniqueBlobName}"; 
            var blobClient = containerClient.GetBlobClient(blobPath);

            _logger.LogInformation("Creating empty blob {BlobPath} ({OriginalName}) for submission {SubmissionId}...", blobPath, desiredFileName, submissionId);

            try
            {
                
                using (var stream = new MemoryStream()) 
                {
                    
                    var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
                    if (!provider.TryGetContentType(desiredFileName, out var contentType))
                    {
                        contentType = "application/octet-stream"; 
                    }

                    await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = contentType });
                    
                }

                _logger.LogInformation("Successfully created empty blob {BlobPath} ({OriginalName})", blobPath, desiredFileName);
                return (uniqueBlobName, relativePath); 
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating empty blob {BlobPath} ({OriginalName})", blobPath, desiredFileName);
                throw;
            }
        }

        
        public async Task<bool> OverwriteSubmissionFileAsync(string relativePath, string storedFileName, string newContent)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_privateContainerName);
            var blobPath = Path.Combine(relativePath, storedFileName).Replace("\\", "/");
            var blobClient = containerClient.GetBlobClient(blobPath);

            _logger.LogInformation("Attempting to overwrite blob {BlobPath}...", blobPath);

            try
            {
                
                
                
                
                
                

                
                byte[] byteArray = Encoding.UTF8.GetBytes(newContent);
                using (var stream = new MemoryStream(byteArray))
                {
                    
                    var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
                    if (!provider.TryGetContentType(storedFileName, out var contentType))
                    {
                        contentType = "text/plain"; 
                    }

                    BlobUploadOptions uploadOptions = new BlobUploadOptions
                    {
                        HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                        Conditions = null
                    };

                    
                    await blobClient.UploadAsync(stream, uploadOptions);
                }

                _logger.LogInformation("Successfully overwrote blob {BlobPath}", blobPath);
                return true;
            }
            catch (Exception ex) 
            {
                _logger.LogError(ex, "Error overwriting blob {BlobPath}", blobPath);
                return false; 
            }
        }

        public async Task<(string StoredFileName, string RelativePath)> SaveTestCaseFileAsync(int assignmentId, string fileTypeDir, IFormFile file)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_privateContainerName);
            var relativeDirPath = GetTestCaseBlobDir(assignmentId, fileTypeDir);
            var extension = Path.GetExtension(file.FileName);
            var uniqueBlobName = $"{Guid.NewGuid()}{extension}";
            var blobPath = $"{relativeDirPath}/{uniqueBlobName}";
            var blobClient = containerClient.GetBlobClient(blobPath);

            _logger.LogInformation("Uploading test case file {FileName} to blob {BlobPath} ({Type}) for assignment {AssignmentId}...", file.FileName, blobPath, fileTypeDir, assignmentId);
            try
            {
                using (var stream = file.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = file.ContentType });
                }
                _logger.LogInformation("Successfully uploaded test case file {FileName} to blob {BlobPath}", file.FileName, blobPath);
                return (uniqueBlobName, relativeDirPath);
            }
            catch (Exception ex) { /* ... Log error, throw ... */ throw; }
        }

        public Task<bool> DeleteTestCaseFileAsync(string relativePath, string storedFileName)
        {
            return DeleteSubmissionFileAsync(relativePath, storedFileName);
        }

        public Task<(Stream? FileStream, string? ContentType, string DownloadName)> GetTestCaseFileAsync(string relativePath, string storedFileName, string originalFileName)
        {
            return GetSubmissionFileAsync(relativePath, storedFileName, originalFileName);
        }

        
        public async Task<(string StoredFileName, string RelativePath)> SaveClassroomPhotoAsync(int classroomId, IFormFile photoFile)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_publicClassroomPhotosContainerName);
            
            var relativePath = $"classrooms/{classroomId}/photo"; 
            var extension = Path.GetExtension(photoFile.FileName);
            var uniqueBlobName = $"{Guid.NewGuid()}{extension}"; 
            var blobPath = $"{relativePath}/{uniqueBlobName}";   
            var blobClient = containerClient.GetBlobClient(blobPath);

            _logger.LogInformation("Uploading classroom photo {FileName} to public blob {BlobPath}", photoFile.FileName, blobPath);
            try
            {
                using (var stream = photoFile.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = photoFile.ContentType }); 
                }
                _logger.LogInformation("Successfully uploaded classroom photo {FileName} to public blob {BlobPath}", photoFile.FileName, blobPath);
                return (uniqueBlobName, relativePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading classroom photo {FileName} to public blob {BlobPath}", photoFile.FileName, blobPath);
                throw;
            }
        }

        public async Task<bool> DeleteClassroomPhotoAsync(string relativePath, string storedFileName)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_publicClassroomPhotosContainerName);
            var blobPath = Path.Combine(relativePath, storedFileName).Replace("\\", "/");
            var blobClient = containerClient.GetBlobClient(blobPath);

            _logger.LogInformation("Attempting to delete public blob (classroom photo) {BlobPath}...", blobPath);
            try
            {
                var response = await blobClient.DeleteIfExistsAsync();
                if (response.Value) { _logger.LogInformation("Successfully deleted public blob {BlobPath}", blobPath); return true; }
                else { _logger.LogWarning("Public blob {BlobPath} did not exist or already deleted.", blobPath); return false; }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting public blob {BlobPath}", blobPath);
                return false;
            }
        }


        
        public async Task<(string StoredFileName, string RelativePath)> SaveUserProfilePhotoAsync(int userId, IFormFile photoFile)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_publicProfilePhotosContainerName);
            
            var relativePath = $"users/{userId}/profile"; 
            var extension = Path.GetExtension(photoFile.FileName);
            var uniqueBlobName = $"{Guid.NewGuid()}{extension}";
            var blobPath = $"{relativePath}/{uniqueBlobName}";
            var blobClient = containerClient.GetBlobClient(blobPath);

            _logger.LogInformation("Uploading user profile photo {FileName} to public blob {BlobPath} for User {UserId}", photoFile.FileName, blobPath, userId);
            try
            {
                using (var stream = photoFile.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = photoFile.ContentType });
                }
                _logger.LogInformation("Successfully uploaded user profile photo {FileName} to public blob {BlobPath}", photoFile.FileName, blobPath);
                return (uniqueBlobName, relativePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading user profile photo {FileName} to public blob {BlobPath}", photoFile.FileName, blobPath);
                throw;
            }
        }

        public async Task<bool> DeleteUserProfilePhotoAsync(string relativePath, string storedFileName)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_publicProfilePhotosContainerName);
            var blobPath = Path.Combine(relativePath, storedFileName).Replace("\\", "/");
            var blobClient = containerClient.GetBlobClient(blobPath);

            _logger.LogInformation("Attempting to delete public blob (user profile photo) {BlobPath}...", blobPath);
            try
            {
                var response = await blobClient.DeleteIfExistsAsync();
                if (response.Value) { _logger.LogInformation("Successfully deleted public blob {BlobPath}", blobPath); return true; }
                else { _logger.LogWarning("Public blob {BlobPath} did not exist or was already deleted.", blobPath); return false; }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting public blob {BlobPath}", blobPath);
                return false;
            }
        }
        
        public string? GetPublicUserProfilePhotoUrl(string ProfilePhotoPath, string ProfilePhotoStoredName)
        {
            if (string.IsNullOrEmpty(ProfilePhotoPath) || string.IsNullOrEmpty(ProfilePhotoStoredName))
                return null;

            if (string.IsNullOrEmpty(_publicStorageBaseUrl) || string.IsNullOrEmpty(_publicProfilePhotosContainerName))
            {
                _logger.LogWarning("PublicStorageBaseUrl or PublicProfilePhotosContainerName not configured.");
                return null;
            }
            return $"{_publicStorageBaseUrl.TrimEnd('/')}/{_publicProfilePhotosContainerName.TrimEnd('/')}/{ProfilePhotoPath.TrimStart('/')}/{ProfilePhotoStoredName}";
        }
    }
}