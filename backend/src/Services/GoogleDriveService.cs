using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using invoice_v1.src.Application.DTOs;

namespace invoice_v1.src.Services
{
    public class GoogleDriveService : IGoogleDriveService
    {
        private DriveService? _serviceAccountDrive;
        private DriveService? _userDrive;
        private readonly ILogger<GoogleDriveService> _logger;
        private readonly IConfiguration _configuration;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _folderLocks = new();

        public GoogleDriveService(IConfiguration configuration, ILogger<GoogleDriveService> logger)
        {
            _logger = logger;
            _configuration = configuration;

            var keyPath = configuration["GoogleDrive:ServiceAccountKeyPath"];

            if (!string.IsNullOrEmpty(keyPath) && File.Exists(keyPath))
            {
                try
                {
                    var credential = GoogleCredential
                        .FromFile(keyPath)
                        .CreateScoped(DriveService.ScopeConstants.Drive);

                    _serviceAccountDrive = new DriveService(new BaseClientService.Initializer()
                    {
                        HttpClientInitializer = credential,
                        ApplicationName = "Invoice Processing System V2"
                    });

                    _logger.LogInformation("Google Drive service initialized successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize Google Drive service");
                    _serviceAccountDrive = null;
                }
            }
            else
            {
                _logger.LogWarning("Service account key not found at: {Path}. Drive monitoring disabled.", keyPath);
            }
        }

        private async Task<DriveService> GetUserDriveAsync()
        {
            if (_userDrive != null) return _userDrive;

            var secrets = new ClientSecrets
            {
                ClientId = _configuration["GoogleDrive:ClientId"],
                ClientSecret = _configuration["GoogleDrive:ClientSecret"]
            };

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                new[] { DriveService.ScopeConstants.Drive },
                "personal-user",
                CancellationToken.None,
                new FileDataStore("Drive.Personal.Auth", true));

            _userDrive = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Invoice Processing System V2"
            });

            _logger.LogInformation("Personal OAuth Drive initialized");
            return _userDrive;
        }

        // ==========================================
        // FIX 1: Recursive File Listing (Prevents False Deletes)
        // ==========================================
        public async Task<List<Google.Apis.Drive.v3.Data.File>> ListAllFilesRecursivelyAsync(
            string rootFolderId,
            CancellationToken cancellationToken = default)
        {
            if (_serviceAccountDrive == null)
            {
                _logger.LogWarning("Drive service not initialized");
                return new List<Google.Apis.Drive.v3.Data.File>();
            }

            var allFiles = new List<Google.Apis.Drive.v3.Data.File>();
            var foldersToProcess = new Queue<string>();
            foldersToProcess.Enqueue(rootFolderId);

            // Protection against infinite loops in folder structure
            var processedFolders = new HashSet<string>();

            try
            {
                while (foldersToProcess.Count > 0)
                {
                    var currentFolderId = foldersToProcess.Dequeue();

                    if (processedFolders.Contains(currentFolderId)) continue;
                    processedFolders.Add(currentFolderId);

                    var request = _serviceAccountDrive.Files.List();
                    // Query: Inside current folder AND not in trash
                    request.Q = $"'{currentFolderId}' in parents and trashed=false";
                    request.Fields = "nextPageToken, files(id, name, mimeType, size, modifiedTime, createdTime, owners, parents)";
                    request.PageSize = 1000;

                    do
                    {
                        var result = await request.ExecuteAsync(cancellationToken);

                        if (result.Files != null)
                        {
                            foreach (var file in result.Files)
                            {
                                if (file.MimeType == "application/vnd.google-apps.folder")
                                {
                                    // Found a sub-folder -> Add to queue
                                    foldersToProcess.Enqueue(file.Id);
                                }
                                else
                                {
                                    // Found a file -> Add to list
                                    allFiles.Add(file);
                                }
                            }
                        }

                        // Handle pagination for large folders
                        request.PageToken = result.NextPageToken;

                    } while (!string.IsNullOrEmpty(request.PageToken));
                }

                _logger.LogInformation(
                    "Recursively scanned {FolderCount} folders and found {FileCount} files.",
                    processedFolders.Count,
                    allFiles.Count);

                return allFiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recursively listing files starting from {FolderId}", rootFolderId);
                return new List<Google.Apis.Drive.v3.Data.File>();
            }
        }

        // ==========================================
        // FIX 2: Original Upload Logic Preserved
        // ==========================================
        private async Task<string> GetOrCreateVendorFolderAsync(
            DriveService drive,
            string parentFolderId,
            string email)
        {
            var folderName = email.ToLowerInvariant().Trim();
            var folderLock = _folderLocks.GetOrAdd(folderName, _ => new SemaphoreSlim(1, 1));

            await folderLock.WaitAsync();
            try
            {
                // Check if folder exists
                var listRequest = drive.Files.List();
                listRequest.Q = $"mimeType='application/vnd.google-apps.folder' and name='{folderName}' and '{parentFolderId}' in parents and trashed=false";
                listRequest.Fields = "files(id, name)";

                var response = await listRequest.ExecuteAsync();
                var existing = response.Files?.FirstOrDefault();

                if (existing != null)
                {
                    _logger.LogInformation("Vendor folder exists: {FolderId}", existing.Id);
                    return existing.Id;
                }

                // Create folder if not exists
                var fileMetadata = new Google.Apis.Drive.v3.Data.File
                {
                    Name = folderName,
                    MimeType = "application/vnd.google-apps.folder",
                    Parents = new[] { parentFolderId }
                };

                var createRequest = drive.Files.Create(fileMetadata);
                createRequest.Fields = "id";
                var folder = await createRequest.ExecuteAsync();

                _logger.LogInformation("Created new vendor folder: {FolderId}", folder.Id);
                return folder.Id;
            }
            finally
            {
                folderLock.Release();
            }
        }

        public async Task<DriveFileResult> UploadFileAsync(
            string fileName,
            string mimeType,
            Stream stream,
            string email)
        {
            var drive = await GetUserDriveAsync();
            var rootFolderId = _configuration["GoogleDrive:SharedFolderId"];

            // 1. Ensure vendor folder exists inside root
            var folderId = await GetOrCreateVendorFolderAsync(drive, rootFolderId!, email);

            if (string.IsNullOrWhiteSpace(folderId))
                throw new Exception("GoogleDrive:SharedFolderId is missing or folder creation failed");

            if (stream.CanSeek) stream.Position = 0;

            try
            {
                var fileMetadata = new Google.Apis.Drive.v3.Data.File
                {
                    Name = fileName,
                    Parents = new[] { folderId } // 2. Upload file INTO vendor folder
                };

                var request = drive.Files.Create(fileMetadata, stream, mimeType);
                request.Fields = "id, name, webViewLink, modifiedTime";

                var uploadResult = await request.UploadAsync();

                if (uploadResult.Status != Google.Apis.Upload.UploadStatus.Completed)
                {
                    throw new Exception($"Drive upload failed: {uploadResult.Exception?.Message}");
                }

                var uploadedFile = request.ResponseBody;
                if (uploadedFile == null) throw new Exception("Drive upload failed: ResponseBody is null");

                _logger.LogInformation("Uploaded file {FileName} to Drive ID {DriveId}", fileName, uploadedFile.Id);

                return new DriveFileResult
                {
                    Id = uploadedFile.Id,
                    Name = uploadedFile.Name,
                    WebViewLink = uploadedFile.WebViewLink,
                    ModifiedTime = uploadedFile.ModifiedTime?.ToUniversalTime()
                };
            }
            catch (Google.GoogleApiException gex)
            {
                _logger.LogError(gex, "Google API error uploading file: {Reason}", gex.Error?.Message);
                throw new Exception($"Google Drive API Error: {gex.Error?.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file {FileName}", fileName);
                throw;
            }
        }

        public async Task DeleteFileAsync(string fileId)
        {
            if (_serviceAccountDrive == null)
                throw new InvalidOperationException("Drive service not initialized");

            await _serviceAccountDrive.Files.Delete(fileId).ExecuteAsync();

            _logger.LogInformation("Deleted file {FileId} from Drive", fileId);
        }
    }
}