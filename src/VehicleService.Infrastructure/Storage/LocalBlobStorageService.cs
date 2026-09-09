using VehicleService.Application.Interfaces;

namespace VehicleService.Infrastructure.Storage;

public class LocalBlobStorageService : IBlobStorageService
{
    private readonly string _uploadFolder;

    public LocalBlobStorageService()
    {
        _uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
        if (!Directory.Exists(_uploadFolder))
        {
            Directory.CreateDirectory(_uploadFolder);
        }
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType)
    {
        string uniqueName = $"{Guid.NewGuid():N}_{Path.GetFileName(fileName)}";
        string filePath = Path.Combine(_uploadFolder, uniqueName);

        using var fileDest = new FileStream(filePath, FileMode.Create);
        await fileStream.CopyToAsync(fileDest);

        return $"/uploads/{uniqueName}";
    }

    public Task DeleteFileAsync(string fileUrl)
    {
        string fileName = Path.GetFileName(fileUrl);
        string filePath = Path.Combine(_uploadFolder, fileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
        return Task.CompletedTask;
    }
}