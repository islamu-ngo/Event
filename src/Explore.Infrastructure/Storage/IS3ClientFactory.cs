using Amazon.S3;
using Explore.Application.Models;

namespace Explore.Infrastructure.Storage;

public interface IS3ClientFactory
{
    IAmazonS3 CreateDataClient(S3Configuration config);

    IAmazonS3 CreatePresignClient(S3Configuration config);
}
