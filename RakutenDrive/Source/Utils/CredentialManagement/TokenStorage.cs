using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Text;


namespace RakutenDrive.Utils.CredentialManagement;

/// <summary>
///     Provides secure storage for access and refresh tokens, facilitating their encryption, decryption, and management.
/// </summary>
[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
internal class TokenStorage
{
	// ----------------------------------------------------------------------------------------
	// PROPERTIES
	// ----------------------------------------------------------------------------------------
	#region PROPERTIES

	public static string? TeamID
	{
		get
		{
			try
			{
				var jwtToken = GetAccessToken();
				var jsonPayload = JWTParser.Parse(jwtToken);
				return jsonPayload.TeamID;
			}
			catch (Exception ex)
			{
				Log.Error($"TokenStorage - Error getting team ID: {ex}");
				return null;
			}
		}
	}

	#endregion
	
	// ----------------------------------------------------------------------------------------
	// CONSTANTS
	// ----------------------------------------------------------------------------------------
	#region CONSTANTS

	private static readonly string AccessTokenFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "token.dat");
	private static readonly string RefreshTokenFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "refresh_token.dat");

	#endregion

	// ----------------------------------------------------------------------------------------
	// PUBLIC METHODS
	// ----------------------------------------------------------------------------------------
	#region PUBLIC METHODS

	/// <summary>
	///     Encrypts the provided access token and stores it in a secure file location.
	/// </summary>
	/// <param name="token">The access token to be stored securely.</param>
	public static void SetAccessToken(string token)
	{
		var encryptedToken = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
		File.WriteAllBytes(AccessTokenFilePath, encryptedToken);
	}


	/// <summary>
	///     Retrieves the encrypted access token from secure storage, decrypts it, and returns it as a plain string.
	/// </summary>
	/// <returns>The decrypted access token, or null if no token exists in storage.</returns>
	public static string? GetAccessToken()
	{
		if (!File.Exists(AccessTokenFilePath))
		{
			return null;
		}

		var encryptedToken = File.ReadAllBytes(AccessTokenFilePath);
		var decryptedToken = ProtectedData.Unprotect(encryptedToken, null, DataProtectionScope.CurrentUser);
		return Encoding.UTF8.GetString(decryptedToken);
	}


	/// <summary>
	///     Deletes the stored access token from secure storage, if it exists.
	/// </summary>
	public static void ClearAccessToken()
	{
		if (File.Exists(AccessTokenFilePath))
		{
			File.Delete(AccessTokenFilePath);
		}
	}


	/// <summary>
	///     Encrypts the provided refresh token and stores it in a secure file location.
	/// </summary>
	/// <param name="token">The refresh token to be stored securely.</param>
	public static void SetRefreshToken(string token)
	{
		var encryptedToken = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
		File.WriteAllBytes(RefreshTokenFilePath, encryptedToken);
	}


	/// <summary>
	///     Retrieves the encrypted refresh token from secure storage, decrypts it, and returns its plaintext value.
	/// </summary>
	/// <returns>The decrypted refresh token if it exists; otherwise, null if no refresh token is found in the storage.
	public static string? GetRefreshToken()
	{
		if (!File.Exists(RefreshTokenFilePath))
		{
			return null;
		}

		var encryptedToken = File.ReadAllBytes(RefreshTokenFilePath);
		var decryptedToken = ProtectedData.Unprotect(encryptedToken, null, DataProtectionScope.CurrentUser);
		return Encoding.UTF8.GetString(decryptedToken);
	}


	/// <summary>
	///     Deletes the stored refresh token from secure storage, if it exists.
	/// </summary>
	public static void ClearRefreshToken()
	{
		if (File.Exists(RefreshTokenFilePath))
		{
			File.Delete(RefreshTokenFilePath);
		}
	}


	/// <summary>
	///     Removes all stored tokens, including both the access token and the refresh token, from secure storage.
	/// </summary>
	public static void ClearAllTokens()
	{
		ClearAccessToken();
		ClearRefreshToken();
	}

	#endregion
}
