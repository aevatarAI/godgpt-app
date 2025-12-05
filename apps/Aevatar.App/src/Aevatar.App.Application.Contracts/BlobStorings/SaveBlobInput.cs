using Microsoft.AspNetCore.Http;

namespace Aevatar.App.Application.Contracts.BlobStorings;

public class SaveBlobInput
{
    public IFormFile File { get; set; }
}
