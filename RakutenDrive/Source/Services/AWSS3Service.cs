using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Transfer;
using RakutenDrive.Utils;


namespace RakutenDrive.Services;

/// <summary>
///     Provides static utility methods for interacting with Amazon S3 using the AWS SDK.
///     This service is specifically designed for handling file uploads to an S3 bucket
///     with options for multipart uploads and access control settings.
/// </summary>
internal class AWSS3Service
{
	/// <summary>
	///     Asynchronously uploads a single file to an Amazon S3 bucket using the TransferUtility.
	/// </summary>
	/// <param name="transferUtil">An instance of <see cref="TransferUtility" /> for performing the file upload.</param>
	/// <param name="bucketName">The name of the Amazon S3 bucket where the file must be uploaded.</param>
	/// <param name="key">The key name within the bucket to store the uploaded file.</param>
	/// <param name="fullPath">The full local file path of the file to be uploaded.</param>
	/// <returns>
	///     A <see cref="Task{TResult}" /> representing the asynchronous operation.
	///     The task result contains a boolean value indicating whether the file upload succeeded.
	/// </returns>
	public static async Task<bool> UploadSingleFileAsync(TransferUtility transferUtil, string bucketName, string key, string fullPath, CancellationToken cancellationToken = default)
	{
		if (File.Exists($"{fullPath}"))
		{
			try
			{
				await transferUtil.UploadAsync(new TransferUtilityUploadRequest
				{
					BucketName   = bucketName,
					Key          = key,
					FilePath     = $"{fullPath}",
					PartSize     = 10 * 1024 * 1024,            // 10 MB (size of each part in multipart upload)
					StorageClass = S3StorageClass.Standard,     // Storage class (e.g., Standard, Infrequent Access)
					CannedACL    = S3CannedACL.Private          // Access control (e.g., Private, PublicRead)
				}, cancellationToken);

				return true;
			}
			catch (AmazonS3Exception s3Ex)
			{
				Log.Warn($"Could not upload {key} from {fullPath} because: {s3Ex.Message}");
				return false;
			}
		}

		Log.Info($"{key} does not exist in {fullPath}");
		return false;
	}
}
